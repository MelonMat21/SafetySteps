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
    // Realtime DB bills by bandwidth, not reads, and each poll is now a small
    // limited query, so these can be short. On a 429 the interval doubles up to the cap.
    private const int IncomingPollBaseMs = 5_000;    // 5 s between incoming-call checks
    private const int StatusPollBaseMs = 2_000;    // 2 s between status checks (active call)
    private const int PollBackoffMultiple = 2;        // double on each quota hit
    private const int PollMaxIntervalMs = 120_000;  // cap at 2 min

    // Queries by receiver match every call ever made to that user, so only the
    // newest ones are downloaded. Pending calls are always recent (stale ones
    // are marked missed after StaleCallAge).
    private const int RecentCallsLimit = 20;

    // Caller name / student number / photo, cached so the 3-second queue refresh
    // doesn't re-download the same profile (and photo) on every tick.
    private static readonly TimeSpan CallerCacheAge = TimeSpan.FromMinutes(10);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime FetchedAt, CallerInfo Info)> _callerCache = new();

    private record CallerInfo(string Name, string StudentNumber, string ProfileImageBase64);

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
                var userDoc = await _db.GetFieldsAsync("users", callerId, "studentNumber", "studentID");
                if (userDoc.Count > 0)
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
                    var allDocs = await QueryCallingDocsAsync(userId);
                    interval = IncomingPollBaseMs;   // reset on success

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
                        var caller = await GetCallerInfoAsync(callerId);
                        var call = ToCallData(doc, userId, caller.ProfileImageBase64);

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

        var uniqueDocs = await QueryCallingDocsAsync(adminUid);

        var stale = uniqueDocs.Where(IsStale).ToList();
        foreach (var doc in stale)
            if (doc.TryGetValue("channelName", out var staleChannel) && staleChannel is string sc)
                await MarkMissedAsync(sc);

        // Resolve every caller in parallel (cached), so CallsPage cards and
        // IncomingCallPage both show the correct name, number and avatar.
        var calls = await Task.WhenAll(uniqueDocs.Except(stale).Select(async doc =>
        {
            var callerId = doc.TryGetValue("callerId", out var rawCid)
                ? rawCid?.ToString() ?? ""
                : "";
            var callerName = doc.TryGetValue("callerName", out var rawName)
                ? rawName?.ToString() ?? ""
                : "";
            var studentNumber = doc.TryGetValue("studentNumber", out var rawSn)
                ? rawSn?.ToString() ?? ""
                : "";

            var caller = await GetCallerInfoAsync(callerId);

            if ((string.IsNullOrWhiteSpace(callerName) || callerName == "Unknown") &&
                !string.IsNullOrWhiteSpace(caller.Name))
                callerName = caller.Name;
            if (string.IsNullOrWhiteSpace(studentNumber))
                studentNumber = caller.StudentNumber;

            doc["callerName"] = callerName;
            doc["studentNumber"] = studentNumber;
            return ToCallData(doc, adminUid, caller.ProfileImageBase64);
        }));

        return calls
            .OrderByDescending(c => c.IsUrgent)
            .ThenBy(c => c.CreatedAtUtc)
            .ToList();
    }

    /// <summary>
    /// Recent calls addressed to <paramref name="userId"/> that are still ringing.
    /// Queries receiverId (may be student-ID key) and receiverUid (Firebase UID) in
    /// parallel and de-duplicates by channelName.
    /// </summary>
    private async Task<List<Dictionary<string, object>>> QueryCallingDocsAsync(string userId)
    {
        var byReceiverId = _db.QueryCollectionAsync(
            Collection, RecentCallsLimit,
            ("receiverId", userId),
            ("status", "calling"));

        var byReceiverUid = _db.QueryCollectionAsync(
            Collection, RecentCallsLimit,
            ("receiverUid", userId),
            ("status", "calling"));

        await Task.WhenAll(byReceiverId, byReceiverUid);

        return byReceiverId.Result
            .Concat(byReceiverUid.Result)
            .GroupBy(d => d.TryGetValue("channelName", out var cn) ? cn?.ToString() : null)
            .Select(g => g.First())
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

    private async Task<CallerInfo> GetCallerInfoAsync(string callerId)
    {
        if (string.IsNullOrEmpty(callerId)) return new CallerInfo("", "", "");

        if (_callerCache.TryGetValue(callerId, out var cached) &&
            DateTime.UtcNow - cached.FetchedAt < CallerCacheAge)
            return cached.Info;

        try
        {
            var fieldsTask = _db.GetFieldsAsync("users", callerId, "name", "studentNumber", "studentID");
            var photoTask = _db.GetProfilePhotoAsync(callerId);
            await Task.WhenAll(fieldsTask, photoTask);

            var f = fieldsTask.Result;
            string Get(string key) => f.TryGetValue(key, out var v) ? v?.ToString() ?? "" : "";

            var sn = Get("studentNumber");
            if (string.IsNullOrWhiteSpace(sn)) sn = Get("studentID");

            var info = new CallerInfo(Get("name"), sn, photoTask.Result);
            if (f.Count > 0) // don't cache a failed/empty lookup
                _callerCache[callerId] = (DateTime.UtcNow, info);
            return info;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Signal] Caller info fetch error: {ex.Message}");
            return new CallerInfo("", "", "");
        }
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