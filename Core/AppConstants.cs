namespace SAFETY_STEPS;

public static class AppConstants
{
    // 🔑 Your Agora App ID (from .env.local)
    public static string AgoraAppId => FireBase.Env.AgoraAppId;

    // ── Admin UID removed ─────────────────────────────────────────────────
    // Admin is now determined dynamically by role == "admin" in the database.
    // Use AdminService.GetAvailableAdminsAsync() to find admins at call time.
    // This supports multiple admin accounts.
}