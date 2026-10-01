namespace SAFETY_STEPS.FireBase;

/// <summary>
/// Persists the Firebase session (IdToken + RefreshToken + LocalId) across app restarts
/// using the platform's SecureStorage (Keychain on iOS, KeyStore on Android).
/// </summary>
public static class SessionService
{
    private const string KeyIdToken = "firebase_id_token";
    private const string KeyRefreshToken = "firebase_refresh_token";
    private const string KeyLocalId = "firebase_local_id";

    // ── Save session after a successful Sign-In / Sign-Up ────
    public static async Task SaveAsync(string idToken, string refreshToken, string localId)
    {
        await SecureStorage.Default.SetAsync(KeyIdToken, idToken);
        await SecureStorage.Default.SetAsync(KeyRefreshToken, refreshToken);
        await SecureStorage.Default.SetAsync(KeyLocalId, localId);
    }

    // ── Load saved session (returns null values if none found) ─
    public static async Task<(string? IdToken, string? RefreshToken, string? LocalId)> LoadAsync()
    {
        var idToken = await SecureStorage.Default.GetAsync(KeyIdToken);
        var refreshToken = await SecureStorage.Default.GetAsync(KeyRefreshToken);
        var localId = await SecureStorage.Default.GetAsync(KeyLocalId);
        return (idToken, refreshToken, localId);
    }

    // ── Clear session on Logout ──────────────────────────────
    public static void Clear()
    {
        SecureStorage.Default.Remove(KeyIdToken);
        SecureStorage.Default.Remove(KeyRefreshToken);
        SecureStorage.Default.Remove(KeyLocalId);
    }

    public static async Task<bool> HasSessionAsync()
    {
        var token = await SecureStorage.Default.GetAsync(KeyRefreshToken);
        return !string.IsNullOrEmpty(token);
    }
}