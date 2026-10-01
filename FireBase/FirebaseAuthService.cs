using System.Net.Http.Json;
using System.Text.Json;

namespace SAFETY_STEPS.FireBase;

public class FirebaseAuthService
{
    private readonly HttpClient _http = new();

    public string? IdToken { get; private set; }
    public string? RefreshToken { get; private set; }
    public string? LocalId { get; private set; }  // = Firebase User UID

    /// <summary>
    /// UTC time of the last successful token refresh.
    /// FirestoreService uses this to avoid a redundant refresh on the very
    /// first call after session restore.
    /// </summary>
    public DateTime LastTokenRefresh { get; private set; } = DateTime.MinValue;

    // True when the last refresh failed because the device was offline, as opposed
    // to Firebase rejecting the refresh token.
    private bool _lastRefreshFailedOffline;

    // ── Auth-ready gate ──────────────────────────────────────
    // Completes once TryRestoreSessionAsync finishes (success OR failure)
    // OR once SignInAsync / SignUpAsync succeeds.
    // FirestoreService awaits this before making any request, so it can
    // never race ahead of the session restore on cold start.
    private readonly TaskCompletionSource _authReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Await this before using any authenticated Firestore call.
    /// It completes as soon as the startup session-restore attempt finishes
    /// (whether it succeeded or not), or immediately if the user has already
    /// signed in manually.
    /// </summary>
    public Task AuthReadyAsync => _authReady.Task;

    // ── Sign Up ──────────────────────────────────────────────
    public async Task<bool> SignUpAsync(string email, string password)
    {
        try
        {
            var url = $"{FirebaseConfig.AuthSignUp}?key={FirebaseConfig.ApiKey}";
            var body = new { email, password, returnSecureToken = true };

            var response = await _http.PostAsJsonAsync(url, body);
            var success = await HandleAuthResponseAsync(response);

            if (success)
            {
                LastTokenRefresh = DateTime.UtcNow;
                await SessionService.SaveAsync(IdToken!, RefreshToken!, LocalId!);
                UserSession.Set(uid: LocalId!, displayName: email, email: email);
                _authReady.TrySetResult(); // unblock any waiting Firestore calls
            }
            return success;
        }
        catch (HttpRequestException ex) { Console.WriteLine($"[SignUpAsync] Network error: {ex.Message}"); return false; }
        catch (OperationCanceledException ex) { Console.WriteLine($"[SignUpAsync] Cancelled: {ex.Message}"); return false; }
        catch (Exception ex) { Console.WriteLine($"[SignUpAsync] Error: {ex.Message}"); return false; }
    }

    // ── Sign In ──────────────────────────────────────────────
    public async Task<bool> SignInAsync(string email, string password)
    {
        try
        {
            var url = $"{FirebaseConfig.AuthSignIn}?key={FirebaseConfig.ApiKey}";
            var body = new { email, password, returnSecureToken = true };

            var response = await _http.PostAsJsonAsync(url, body);
            var success = await HandleAuthResponseAsync(response);

            if (success)
            {
                LastTokenRefresh = DateTime.UtcNow;
                await SessionService.SaveAsync(IdToken!, RefreshToken!, LocalId!);
                UserSession.Set(uid: LocalId!, displayName: email, email: email);
                _authReady.TrySetResult(); // unblock any waiting Firestore calls
            }
            return success;
        }
        catch (HttpRequestException ex) { Console.WriteLine($"[SignInAsync] Network error: {ex.Message}"); return false; }
        catch (OperationCanceledException ex) { Console.WriteLine($"[SignInAsync] Cancelled: {ex.Message}"); return false; }
        catch (Exception ex) { Console.WriteLine($"[SignInAsync] Error: {ex.Message}"); return false; }
    }

    // ── Restore session from SecureStorage on app start ──────
    public async Task<bool> TryRestoreSessionAsync()
    {
        try
        {
            var (savedIdToken, savedRefreshToken, savedLocalId) = await SessionService.LoadAsync();

            Console.WriteLine($"[Auth] Restoring session — LocalId='{savedLocalId}'");

            if (string.IsNullOrEmpty(savedRefreshToken))
            {
                // No saved session — unblock Firestore immediately so login-page
                // calls (e.g. sign-in) are not deadlocked.
                _authReady.TrySetResult();
                return false;
            }

            IdToken = savedIdToken;
            RefreshToken = savedRefreshToken;
            LocalId = savedLocalId;

            var refreshed = await RefreshTokenAsync();
            if (!refreshed && _lastRefreshFailedOffline)
            {
                // No connection at launch: keep the saved session instead of logging
                // the user out. The token is refreshed on the first request once online.
                Console.WriteLine("[Auth] Offline at startup — keeping saved session.");
            }
            else if (!refreshed)
            {
                SignOut();
                _authReady.TrySetResult(); // failed — unblock so app can proceed to login
                return false;
            }

            Console.WriteLine($"[Auth] Session restored OK — LocalId='{LocalId}'");
            UserSession.Set(uid: LocalId!, displayName: LocalId!);
            // Restore the saved name + student number from Preferences so the
            // profile page shows the correct values without a full re-login.
            UserSession.RestorePersistedFields();
            _authReady.TrySetResult(); // success — unblock Firestore calls
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TryRestoreSessionAsync] Error: {ex.Message}");
            SignOut();
            _authReady.TrySetResult(); // error — still unblock so app doesn't hang
            return false;
        }
    }

    // ── Refresh Token ────────────────────────────────────────
    public async Task<bool> RefreshTokenAsync()
    {
        if (RefreshToken is null) return false;

        _lastRefreshFailedOffline = false;
        try
        {
            var url = $"{FirebaseConfig.AuthRefresh}?key={FirebaseConfig.ApiKey}";
            var body = new { grant_type = "refresh_token", refresh_token = RefreshToken };

            var response = await _http.PostAsJsonAsync(url, body);
            if (!response.IsSuccessStatusCode) return false;

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            IdToken = json.GetProperty("id_token").GetString();
            RefreshToken = json.GetProperty("refresh_token").GetString();

            LastTokenRefresh = DateTime.UtcNow;   // ← stamp the refresh time
            await SessionService.SaveAsync(IdToken!, RefreshToken!, LocalId ?? "");
            return true;
        }
        catch (HttpRequestException ex) { Console.WriteLine($"[RefreshTokenAsync] Network error: {ex.Message}"); _lastRefreshFailedOffline = true; return false; }
        catch (OperationCanceledException ex) { Console.WriteLine($"[RefreshTokenAsync] Cancelled: {ex.Message}"); _lastRefreshFailedOffline = true; return false; }
        catch (Exception ex) { Console.WriteLine($"[RefreshTokenAsync] Error: {ex.Message}"); return false; }
    }

    // ── Change Password ──────────────────────────────────────
    /// <summary>
    /// Updates the current user's password via Firebase accounts:update.
    /// On success the fresh IdToken/RefreshToken pair returned by Firebase is
    /// persisted to SecureStorage so the session stays alive.
    /// </summary>
    /// <returns>
    /// <c>(true, null)</c> on success; <c>(false, humanReadableError)</c> on failure.
    /// </returns>
    public async Task<(bool Success, string? Error)> ChangePasswordAsync(string newPassword)
    {
        if (string.IsNullOrEmpty(IdToken))
            return (false, "You are not signed in.");

        try
        {
            var url = $"https://identitytoolkit.googleapis.com/v1/accounts:update?key={FirebaseConfig.ApiKey}";
            var body = new { idToken = IdToken, password = newPassword, returnSecureToken = true };

            var response = await _http.PostAsJsonAsync(url, body);

            if (!response.IsSuccessStatusCode)
            {
                var raw = await response.Content.ReadAsStringAsync();
                var msg = ParseFirebaseError(raw);
                Console.WriteLine($"[ChangePasswordAsync] Failed: {raw}");
                return (false, msg);
            }

            // Firebase returns a fresh token pair — save it so nothing expires.
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            IdToken = json.GetProperty("idToken").GetString();
            RefreshToken = json.GetProperty("refreshToken").GetString();
            LastTokenRefresh = DateTime.UtcNow;
            await SessionService.SaveAsync(IdToken!, RefreshToken!, LocalId ?? "");

            Console.WriteLine("[ChangePasswordAsync] Password changed and tokens refreshed.");
            return (true, null);
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"[ChangePasswordAsync] Network error: {ex.Message}");
            return (false, "Network error. Please check your connection.");
        }
        catch (OperationCanceledException ex)
        {
            Console.WriteLine($"[ChangePasswordAsync] Cancelled: {ex.Message}");
            return (false, "Request timed out. Please try again.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ChangePasswordAsync] Error: {ex.Message}");
            return (false, "An unexpected error occurred.");
        }
    }

    // ── Sign Out ─────────────────────────────────────────────
    public void SignOut()
    {
        IdToken = null;
        RefreshToken = null;
        LocalId = null;
        LastTokenRefresh = DateTime.MinValue;
        SessionService.Clear();
        UserSession.Clear();
    }

    public bool IsSignedIn => IdToken != null;

    // ── Parse Auth Response ──────────────────────────────────
    private async Task<bool> HandleAuthResponseAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Auth error: {error}");
            return false;
        }

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        IdToken = json.GetProperty("idToken").GetString();
        RefreshToken = json.GetProperty("refreshToken").GetString();
        LocalId = json.GetProperty("localId").GetString();
        return true;
    }

    // ── Human-readable Firebase error messages ───────────────
    private static string ParseFirebaseError(string rawJson)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<JsonElement>(rawJson);
            var code = doc.GetProperty("error").GetProperty("message").GetString() ?? "";
            return code switch
            {
                "WEAK_PASSWORD : Password should be at least 6 characters"
                    => "Password must be at least 6 characters.",
                "CREDENTIAL_TOO_OLD_LOGIN_AGAIN"
                    => "For security, please log out and log back in before changing your password.",
                "INVALID_ID_TOKEN"
                    => "Your session has expired. Please log in again.",
                _ => "Could not change password. Please try again."
            };
        }
        catch
        {
            return "Could not change password. Please try again.";
        }
    }
}