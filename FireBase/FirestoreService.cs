using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace SAFETY_STEPS.FireBase;

/// <summary>
/// Thrown when Realtime DB returns HTTP 429 (quota exceeded).
/// </summary>
public class FirestoreQuotaExceededException : Exception
{
    public FirestoreQuotaExceededException()
        : base("Realtime Database quota exceeded (HTTP 429). Apply back-off before retrying.") { }
}

/// <summary>
/// Drop-in replacement for FirestoreService using Firebase Realtime Database REST API.
/// All method signatures are identical — no changes needed anywhere else.
///
/// FIX vs previous version:
///   • Removed System.Web.HttpUtility (not available on Android).
///   • NodeUrl() / QueryUrl() read _auth.IdToken fresh every call, so
///     after a token refresh we just rebuild the URL — no string manipulation.
/// </summary>
public class FirestoreService
{
    private readonly HttpClient _http = new();
    private readonly FirebaseAuthService _auth;

    private DateTime _lastTokenRefresh = DateTime.MinValue;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private const string DbUrl =
        "https://safetysteps-f4f77-default-rtdb.asia-southeast1.firebasedatabase.app";

    public FirestoreService(FirebaseAuthService auth) => _auth = auth;

    // ── Token management ──────────────────────────────────────────────────
    private async Task EnsureTokenAsync()
    {
        await _auth.AuthReadyAsync;

        if (_lastTokenRefresh == DateTime.MinValue && _auth.LastTokenRefresh != DateTime.MinValue)
            _lastTokenRefresh = _auth.LastTokenRefresh;

        if (!string.IsNullOrEmpty(_auth.IdToken) &&
            (DateTime.UtcNow - _lastTokenRefresh).TotalMinutes < 50)
            return;

        await _refreshLock.WaitAsync();
        try
        {
            if (!string.IsNullOrEmpty(_auth.IdToken) &&
                (DateTime.UtcNow - _lastTokenRefresh).TotalMinutes < 50)
                return;

            Console.WriteLine("[RTDB] Proactively refreshing auth token…");
            var ok = await _auth.RefreshTokenAsync();
            if (ok) _lastTokenRefresh = DateTime.UtcNow;
            else Console.WriteLine("[RTDB] ⚠️ Token refresh failed.");
        }
        finally { _refreshLock.Release(); }
    }

    // ── URL builders — reads _auth.IdToken FRESH every time ───────────────
    // This means after RefreshTokenAsync() we just call NodeUrl() again
    // and the new token is already embedded. No string replacement needed.
    private string NodeUrl(string collection, string? documentId = null)
    {
        var token = Uri.EscapeDataString(_auth.IdToken ?? "");
        return documentId is null
            ? $"{DbUrl}/{collection}.json?auth={token}"
            : $"{DbUrl}/{collection}/{documentId}.json?auth={token}";
    }

    private string QueryUrl(string collection, string field, string value)
    {
        var token = Uri.EscapeDataString(_auth.IdToken ?? "");
        var orderBy = Uri.EscapeDataString($"\"{field}\"");
        var equalTo = Uri.EscapeDataString($"\"{value}\"");
        return $"{DbUrl}/{collection}.json?auth={token}&orderBy={orderBy}&equalTo={equalTo}";
    }

    // ── Shared send-with-retry (rebuilds URL after token refresh) ──────────
    private async Task<HttpResponseMessage> SendAsync(
    HttpMethod method,
    Func<string> urlFactory,
    object? body = null)
    {
        var response = await _http.SendAsync(Build(method, urlFactory(), body));

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            // ── Distinguish "bad token" from "rules denied" ──────────────────
            // Permission denied = the token IS valid but rules block the request.
            // Retrying with a fresh token will never help; bail out immediately.
            var errBody = await response.Content.ReadAsStringAsync();
            if (errBody.Contains("Permission denied") || errBody.Contains("PERMISSION_DENIED"))
            {
                Console.WriteLine($"[RTDB] 401 Permission denied (rules) — NOT retrying: {Trunc(errBody)}");
                return response;
            }

            Console.WriteLine("[RTDB] 401 — refreshing token and retrying…");
            await _refreshLock.WaitAsync();
            try
            {
                var ok = await _auth.RefreshTokenAsync();
                if (ok) _lastTokenRefresh = DateTime.UtcNow;
            }
            finally { _refreshLock.Release(); }

            response = await _http.SendAsync(Build(method, urlFactory(), body));
        }

        return response;
    }

    private static HttpRequestMessage Build(HttpMethod method, string url, object? body)
    {
        var req = new HttpRequestMessage(method, url);
        if (body != null)
            req.Content = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return req;
    }

    // ── Set / Overwrite ───────────────────────────────────────────────────
    public async Task<bool> SetDocumentAsync(string collection, string documentId,
                                             Dictionary<string, object> data)
    {
        await EnsureTokenAsync();
        try
        {
            var r = await SendAsync(HttpMethod.Put,
                () => NodeUrl(collection, documentId), data);
            return r.IsSuccessStatusCode;
        }
        catch (Exception ex) { Console.WriteLine($"[RTDB Set] {ex.Message}"); return false; }
    }

    // ── Get one document ──────────────────────────────────────────────────
    public async Task<Dictionary<string, object>?> GetDocumentAsync(
        string collection, string documentId)
    {
        await EnsureTokenAsync();
        try
        {
            var r = await SendAsync(HttpMethod.Get,
                () => NodeUrl(collection, documentId));

            if (!r.IsSuccessStatusCode)
            {
                var err = await r.Content.ReadAsStringAsync();
                Console.WriteLine($"[RTDB Get] FAILED {collection}/{documentId} → HTTP {(int)r.StatusCode}. {Trunc(err)}");
                return null;
            }

            var body = await r.Content.ReadAsStringAsync();
            if (body.Trim() == "null") return null;
            return FlattenNode(JsonSerializer.Deserialize<JsonElement>(body));
        }
        catch (Exception ex) { Console.WriteLine($"[RTDB Get] {ex.Message}"); return null; }
    }

    // ── Get all docs in a collection ──────────────────────────────────────
    public async Task<List<Dictionary<string, object>>> GetCollectionAsync(string collection)
    {
        await EnsureTokenAsync();
        try
        {
            var r = await SendAsync(HttpMethod.Get, () => NodeUrl(collection));
            if (!r.IsSuccessStatusCode) return new();

            var body = await r.Content.ReadAsStringAsync();
            if (body.Trim() == "null") return new();

            var json = JsonSerializer.Deserialize<JsonElement>(body);
            var results = new List<Dictionary<string, object>>();
            if (json.ValueKind == JsonValueKind.Object)
                foreach (var prop in json.EnumerateObject())
                {
                    var d = FlattenNode(prop.Value);
                    d["docId"] = prop.Name;
                    results.Add(d);
                }
            return results;
        }
        catch (Exception ex) { Console.WriteLine($"[RTDB GetAll] {ex.Message}"); return new(); }
    }

    // ── Delete ────────────────────────────────────────────────────────────
    public async Task<bool> DeleteDocumentAsync(string collection, string documentId)
    {
        await EnsureTokenAsync();
        try
        {
            var r = await SendAsync(HttpMethod.Delete,
                () => NodeUrl(collection, documentId));
            return r.IsSuccessStatusCode;
        }
        catch (Exception ex) { Console.WriteLine($"[RTDB Delete] {ex.Message}"); return false; }
    }

    // ── Patch (merge fields only) ─────────────────────────────────────────
    public async Task<bool> PatchFieldsAsync(string collection, string documentId,
                                             Dictionary<string, object> fields)
    {
        await EnsureTokenAsync();
        try
        {
            var r = await SendAsync(HttpMethod.Patch,
                () => NodeUrl(collection, documentId), fields);

            if (!r.IsSuccessStatusCode)
            {
                var err = await r.Content.ReadAsStringAsync();
                Console.WriteLine($"[RTDB Patch] FAILED {collection}/{documentId} → HTTP {(int)r.StatusCode}. {Trunc(err)}");
            }
            return r.IsSuccessStatusCode;
        }
        catch (Exception ex) { Console.WriteLine($"[RTDB Patch] {ex.Message}"); return false; }
    }

    // ── Query with equality filters ───────────────────────────────────────
    // Requires .indexOn in DB Rules (see database.rules.json):
    //   "calls":            { ".indexOn": ["receiverId","receiverUid","status","callerId"] }
    //   "users":            { ".indexOn": ["uid","role"] }
    //   "emergency_reports":{ ".indexOn": ["submittedBy","status"] }
    public async Task<List<Dictionary<string, object>>> QueryCollectionAsync(
        string collection, params (string field, string value)[] filters)
    {
        await EnsureTokenAsync();
        try
        {
            if (filters.Length == 0) return await GetCollectionAsync(collection);

            var (f0, v0) = filters[0];
            var r = await SendAsync(HttpMethod.Get, () => QueryUrl(collection, f0, v0));

            if (!r.IsSuccessStatusCode)
            {
                var errBody = await r.Content.ReadAsStringAsync();
                Console.WriteLine($"[RTDB Query] FAILED {collection} → HTTP {(int)r.StatusCode}. {Trunc(errBody)}");

                if ((int)r.StatusCode == 429 || errBody.Contains("RESOURCE_EXHAUSTED"))
                    throw new FirestoreQuotaExceededException();

                return new();
            }

            var body = await r.Content.ReadAsStringAsync();
            if (body.Trim() == "null") return new();

            var json = JsonSerializer.Deserialize<JsonElement>(body);
            var results = new List<Dictionary<string, object>>();

            if (json.ValueKind == JsonValueKind.Object)
                foreach (var prop in json.EnumerateObject())
                {
                    var dict = FlattenNode(prop.Value);
                    dict["docId"] = prop.Name;

                    var ok = true;
                    for (int i = 1; i < filters.Length; i++)
                    {
                        var (fi, vi) = filters[i];
                        if (!dict.TryGetValue(fi, out var actual) || actual?.ToString() != vi)
                        { ok = false; break; }
                    }
                    if (ok) results.Add(dict);
                }

            return results;
        }
        catch (FirestoreQuotaExceededException) { throw; }
        catch (Exception ex) { Console.WriteLine($"[RTDB Query] {ex.Message}"); return new(); }
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    private static Dictionary<string, object> FlattenNode(JsonElement el)
    {
        var r = new Dictionary<string, object>();
        if (el.ValueKind != JsonValueKind.Object) return r;
        foreach (var p in el.EnumerateObject())
            r[p.Name] = p.Value.ValueKind switch
            {
                JsonValueKind.String => p.Value.GetString()!,
                JsonValueKind.Number when p.Value.TryGetInt64(out var l) => (object)l,
                JsonValueKind.Number => p.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null!,
                _ => p.Value.ToString()
            };
        return r;
    }

    private static string Trunc(string? s, int max = 400)
    {
        if (string.IsNullOrEmpty(s)) return "(empty)";
        s = s.ReplaceLineEndings(" ");
        return s.Length <= max ? s : s[..max] + "…";
    }
}