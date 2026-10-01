namespace SAFETY_STEPS;

public static class AppConstants
{
    // 🔑 Your Agora App ID
    public const string AgoraAppId = "b6e546112ffc4f8cbd176532d7e588ba";

    // ── Admin UID removed ─────────────────────────────────────────────────
    // Admin is now determined dynamically by role == "admin" in the database.
    // Use AdminService.GetAvailableAdminsAsync() to find admins at call time.
    // This supports multiple admin accounts.
}