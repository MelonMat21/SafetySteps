using SAFETY_STEPS.FireBase;
using SAFETY_STEPS.Models;

namespace SAFETY_STEPS.Services;

public class CallSignalingService
{
    private readonly FirestoreService _db;
    private CancellationTokenSource? _incomingCts;
    private CancellationTokenSource? _statusCts;

    private const string Collection = "calls";

    // ── Polling / back-off constants ─────────────────────────────────────
    // Base intervals are intentionally generous to stay within Firestore's
    // free-tier quota. On a 429 the interval doubles up to the cap.
    private const int IncomingPollBaseMs = 15_000;   // 15 s between incoming-call checks
    private const int StatusPollBaseMs = 5_000;    // 5 s between status checks (active call)
    private const int PollBackoffMultiple = 2;        // double on each quota hit
    private const int PollMaxIntervalMs = 120_000;  // cap at 2 min

    // A call still "calling" after this long was abandoned (e.g. the caller's app
    // was killed). It is marked missed so it stops showing in the admin queue.
    private static readonly TimeSpan StaleCallAge = TimeSpan.FromMinutes(10);

    public event Action<CallData>? OnIncomingCall;
    public event Action<string>? OnCallStatusChanged;

    public CallSignalingService(FirestoreService db)
    {
        _db = db;
    }

    public async Task StartCallAsync(string callerId, string callerName,
                                     string receiverId, string channelName,
                                     string? receiverFirebaseUid = null,
                                     bool isUrgent = false,
                                     string studentNumber = "")
    {
        // ── Resolve student number from the caller's OWN user doc ─────────
        // The student can always read their own doc; the admin cannot.
        // Writing it here guarantees the admin sees it directly on the call
        // document without needing a cross-user DB fetch (which RTDB rules deny).
        if (string.IsNullOrWhiteSpace(studentNumber) && !string.IsNullOrWhiteSpace(callerId))
        {
            try
            {
                var userDoc = await _db.GetDocumentAsync("users", callerId);
                if (userDoc != null)
                {
                    // Try "studentNumber" first, then fall back to "studentID"
                    if (!userDoc.TryGetValue("studentNumber", out var snVal) ||
                        string.IsNullOrWhiteSpace(snVal?.ToString()))
                        userDoc.TryGetValue("studentID", out snVal);

                    studentNumber = snVal?.ToString() ?? "";
                    Console.WriteLine($"[Signal] Resolved studentNumber='{studentNumber}' for callerId='{callerId}'");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Signal] Could not resolve studentNumber from DB: {ex.Message}");
            }
        }

        var createdAtTicks = DateTime.UtcNow.Ticks;
        var data = new Dictionary<string, object>
        {
            ["callId"] = channelName,
            ["callerId"] = callerId,
            ["callerName"] = callerName,
            ["studentNumber"] = studentNumber,          // ← FIX: was patched separately/late
            ["receiverId"] = receiverId,
            // Also store Firebase UID so the admin can match on either field
            ["receiverUid"] = receiverFirebaseUid ?? receiverId,
            ["channelName"] = channelName,
            ["status"] = "calling",
            ["isUrgent"] = isUrgent,
            // Stored as ticks for easy sort without Firestore timestamp parsing.
            ["createdAt"] = createdAtTicks
        };

        await _db.SetDocumentAsync(Collection, channelName, data);
        Console.WriteLine($"[Signal] Call started -> calls/{channelName}");
    }

    public void ListenForCallStatus(string channelName)
    {
        StopListeningForStatus();
        _statusCts = new CancellationTokenSource();
        var token = _statusCts.Token;

        Task.Run(async () =>
        {
            int interval = StatusPollBaseMs;

            while (!token.IsCancellationRequested)
            {
                try { await Task.Delay(interval, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }

                try
                {
                    var doc = await _db.GetDocumentAsync(Collection, channelName);
                    interval = StatusPollBaseMs;   // reset on success

                    if (doc != null &&
                        doc.TryGetValue("status", out var rawStatus) &&
                        rawStatus is string status)
                    {
                        if (status == "rejected" || status == "ended" || status == "missed")
                        {
                            Console.WriteLine($"[Signal] Status -> {status}");
                            MainThread.BeginInvokeOnMainThread(()
                                => OnCallStatusChanged?.Invoke(status));
                            StopListeningForStatus();
                            return;
                        }
                        else if (status == "accepted")
                        {
                            Console.WriteLine($"[Signal] Status -> accepted");
                            MainThread.BeginInvokeOnMainThread(()
                                => OnCallStatusChanged?.Invoke(status));
                            // Keep polling — we still need to detect "ended"
                        }
                    }
                }
                catch (FirestoreQuotaExceededException)
                {
                    interval = Math.Min(interval * PollBackoffMultiple, PollMaxIntervalMs);
                    Console.WriteLine($"[Signal] Status poll quota hit — backing off to {interval / 1000}s");
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Signal] Status poll error: {ex.Message}");
                }
            }
        }, token);
    }

    public void ListenForIncomingCalls(string userId, HashSet<string>? seenChannels = null)
    {
        if (string.IsNullOrEmpty(userId)) return;

        StopListeningForIncoming();
        _incomingCts = new CancellationTokenSource();
        var token = _incomingCts.Token;

        var seen = seenChannels ?? new HashSet<string>();

        Task.Run(async () =>
        {
            int interval = IncomingPollBaseMs;

            while (!token.IsCancellationRequested)
            {
                try { await Task.Delay(interval, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }

                try
                {
                    // Query by receiverId (may be student-ID key) AND receiverUid (Firebase UID).
                    // Both queries count against quota — keep the interval generous.
                    var byReceiverId = await _db.QueryCollectionAsync(
                        Collection,
                        ("receiverId", userId),
                        ("status", "calling"));

                    var byReceiverUid = await _db.QueryCollectionAsync(
                        Collection,
                        ("receiverUid", userId),
                        ("status", "calling"));

                    interval = IncomingPollBaseMs;   // reset on success

                    // Merge, de-duplicate by channelName
                    var allDocs = byReceiverId
                        .Concat(byReceiverUid)
                        .GroupBy(d => d.TryGetValue("channelName", out var cn) ? cn?.ToString() : null)
                        .Select(g => g.First())
                        .ToList();

                    foreach (var doc in allDocs)
                    {
                        if (!doc.TryGetValue("channelName", out var rawChannel) ||
                            rawChannel is not string channelName) continue;
                        if (seen.Contains(channelName)) continue;
                        seen.Add(channelName);

                        if (IsStale(doc))
                        {
                            await MarkMissedAsync(channelName);
                            continue;
                        }

                        var callerId = doc.TryGetValue("callerId", out var rawCid)
                            ? rawCid?.ToString() ?? ""
                            : "";
                        var profileB64 = await FetchProfileImageBase64Async(callerId);
                        var call = ToCallData(doc, userId, profileB64);

                        Console.WriteLine($"[Signal] Incoming call from {call.CallerName}");
                        MainThread.BeginInvokeOnMainThread(()
                            => OnIncomingCall?.Invoke(call));
                    }
                }
                catch (FirestoreQuotaExceededException)
                {
                    interval = Math.Min(interval * PollBackoffMultiple, PollMaxIntervalMs);
                    Console.WriteLine($"[Signal] Incoming poll quota hit — backing off to {interval / 1000}s");
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Signal] Incoming poll error: {ex.Message}");
                }
            }
        }, token);
    }

    public Task AcceptCallAsync(string channelName)
        => UpdateStatusAsync(channelName, "accepted");

    public Task RejectCallAsync(string channelName)
        => UpdateStatusAsync(channelName, "rejected");

    public Task EndCallAsync(string channelName)
        => UpdateStatusAsync(channelName, "ended");

    public Task MarkMissedAsync(string channelName)
        => UpdateStatusAsync(channelName, "missed");

    public async Task<bool> TryAcceptCallAsync(string channelName)
    {
        var doc = await _db.GetDocumentAsync(Collection, channelName);
        if (doc == null) return false;

        var currentStatus = doc.TryGetValue("status", out var rawStatus)
            ? rawStatus?.ToString()
            : null;

        if (!string.Equals(currentStatus, "calling", StringComparison.OrdinalIgnoreCase))
            return false;

        var ok = await _db.PatchFieldsAsync(Collection, channelName,
            new Dictionary<string, object> { ["status"] = "accepted" });
        if (ok)
            Console.WriteLine($"[Signal] TryAcceptCall succeeded for '{channelName}'.");
        return ok;
    }

    public async Task<List<CallData>> GetPendingCallsForAdminAsync(string adminUid)
    {
        if (string.IsNullOrWhiteSpace(adminUid)) return new List<CallData>();

        var byReceiverId = await _db.QueryCollectionAsync(
            Collection,
            ("receiverId", adminUid),
            ("status", "calling"));

        var byReceiverUid = await _db.QueryCollectionAsync(
            Collection,
            ("receiverUid", adminUid),
            ("status", "calling"));

        // De-duplicate by channelName (same call may match both queries)
        var uniqueDocs = byReceiverId
            .Concat(byReceiverUid)
            .GroupBy(d => d.TryGetValue("channelName", out var cn) ? cn?.ToString() : null)
            .Select(g => g.First())
            .ToList();

        // FIX: fetch profile image for each call (same as ListenForIncomingCalls does)
        // so CallsPage cards and IncomingCallPage both show the correct avatar.
        var calls = new List<CallData>();
        foreach (var doc in uniqueDocs)
        {
            if (IsStale(doc))
            {
                if (doc.TryGetValue("channelName", out var staleChannel) && staleChannel is string sc)
                    await MarkMissedAsync(sc);
                continue;
            }

            var callerId = doc.TryGetValue("callerId", out var rawCid)
                ? rawCid?.ToString() ?? ""
                : "";
            var callerName = doc.TryGetValue("callerName", out var rawName)
                ? rawName?.ToString() ?? ""
                : "";
            var studentNumber = doc.TryGetValue("studentNumber", out var rawSn)
                ? rawSn?.ToString() ?? ""
                : "";

            Dictionary<string, object>? userDoc = null;
            if (!string.IsNullOrWhiteSpace(callerId))
                userDoc = await _db.GetDocumentAsync("users", callerId);

            if (string.IsNullOrWhiteSpace(callerName) || callerName == "Unknown")
            {
                var resolvedName = userDoc != null && userDoc.TryGetValue("name", out var n) ? n?.ToString() ?? "" : "";
                if (!string.IsNullOrWhiteSpace(resolvedName))
                    callerName = resolvedName;
            }
            if (string.IsNullOrWhiteSpace(studentNumber))
            {
                studentNumber = userDoc != null && userDoc.TryGetValue("studentNumber", out var sn1) ? sn1?.ToString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(studentNumber))
                    studentNumber = userDoc != null && userDoc.TryGetValue("studentID", out var sn2) ? sn2?.ToString() ?? "" : "";
            }

            // Reuse the user doc fetched above instead of downloading it a second time.
            var profileB64 = userDoc != null &&
                             userDoc.TryGetValue("profileImageBase64", out var p) &&
                             p is string b64
                ? b64
                : "";
            doc["callerName"] = callerName;
            doc["studentNumber"] = studentNumber;
            calls.Add(ToCallData(doc, adminUid, profileB64));
        }

        return calls
            .OrderByDescending(c => c.IsUrgent)
            .ThenBy(c => c.CreatedAtUtc)
            .ToList();
    }

    public void StopListeningForIncoming()
    {
        _incomingCts?.Cancel();
        _incomingCts?.Dispose();
        _incomingCts = null;
    }

    public void StopListeningForStatus()
    {
        _statusCts?.Cancel();
        _statusCts?.Dispose();
        _statusCts = null;
    }

    public void StopAll() { StopListeningForIncoming(); StopListeningForStatus(); }

    private async Task UpdateStatusAsync(string channelName, string status)
    {
        var data = new Dictionary<string, object> { ["status"] = status };
        await _db.PatchFieldsAsync(Collection, channelName, data);
        Console.WriteLine($"[Signal] Updated calls/{channelName} -> {status}");
    }

    private async Task<string> FetchProfileImageBase64Async(string callerId)
    {
        if (string.IsNullOrEmpty(callerId)) return "";
        try
        {
            var userDoc = await _db.GetDocumentAsync("users", callerId);
            if (userDoc != null &&
                userDoc.TryGetValue("profileImageBase64", out var raw) &&
                raw is string b64 &&
                !string.IsNullOrWhiteSpace(b64))
                return b64;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Signal] Profile image fetch error: {ex.Message}");
        }
        return "";
    }

    private static bool IsStale(Dictionary<string, object> doc)
    {
        if (!doc.TryGetValue("createdAt", out var raw)) return false;
        long ticks = raw is long l ? l : long.TryParse(raw?.ToString(), out var parsed) ? parsed : 0;
        if (ticks <= 0 || ticks > DateTime.MaxValue.Ticks) return false;
        return DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc) > StaleCallAge;
    }

    private static CallData ToCallData(Dictionary<string, object> doc, string fallbackReceiverId, string profileImageBase64 = "")
    {
        var channelName = doc.TryGetValue("channelName", out var rawChannel)
            ? rawChannel?.ToString() ?? ""
            : "";

        var createdAtUtc = DateTime.UtcNow;
        if (doc.TryGetValue("createdAt", out var rawCreatedAt))
        {
            if (rawCreatedAt is long ticks)
                createdAtUtc = new DateTime(ticks, DateTimeKind.Utc);
            else if (long.TryParse(rawCreatedAt?.ToString(), out var parsedTicks))
                createdAtUtc = new DateTime(parsedTicks, DateTimeKind.Utc);
        }

        var isUrgent = false;
        if (doc.TryGetValue("isUrgent", out var rawUrgent))
        {
            if (rawUrgent is bool b) isUrgent = b;
            else _ = bool.TryParse(rawUrgent?.ToString(), out isUrgent);
        }

        return new CallData
        {
            CallId = channelName,
            CallerId = doc.TryGetValue("callerId", out var v1) ? v1?.ToString() ?? "" : "",
            CallerName = doc.TryGetValue("callerName", out var v2) ? v2?.ToString() ?? "" : "",
            StudentNumber = doc.TryGetValue("studentNumber", out var v6) ? v6?.ToString() ?? "" : "",   // ← FIX: was missing
            ReceiverId = doc.TryGetValue("receiverId", out var v3) ? v3?.ToString() ?? fallbackReceiverId : fallbackReceiverId,
            ReceiverUid = doc.TryGetValue("receiverUid", out var v4) ? v4?.ToString() ?? fallbackReceiverId : fallbackReceiverId,
            ChannelName = channelName,
            IsUrgent = isUrgent,
            CreatedAtUtc = createdAtUtc,
            Status = doc.TryGetValue("status", out var v5) ? v5?.ToString() ?? "calling" : "calling",
            ProfileImageBase64 = profileImageBase64
        };
    }
}