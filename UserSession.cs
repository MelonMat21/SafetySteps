namespace SAFETY_STEPS;

/// <summary>
/// Lightweight static store for the currently logged-in user.
/// Name, StudentNumber, and Email are mirrored to Preferences so they survive
/// an app restart when TryRestoreSessionAsync rehydrates the session.
/// </summary>
public static class UserSession
{
    // ── Preferences keys ─────────────────────────────────────
    private const string PrefName = "session_display_name";
    private const string PrefStudentNumber = "session_student_number";
    private const string PrefEmail = "session_email";

    public static string Uid { get; private set; } = "";
    public static string DisplayName { get; private set; } = "Unknown";
    public static string StudentNumber { get; private set; } = "";

    /// <summary>
    /// The Firebase email used to authenticate this account
    /// (format: {studentId}@safetysteps.app).
    /// Needed by ChangePasswordAsync for re-authentication.
    /// </summary>
    public static string Email { get; private set; } = "";

    /// <summary>
    /// Sets the core session fields after a successful sign-in or sign-up.
    /// <paramref name="email"/> is stored in-memory only here; call
    /// <see cref="SetEmail"/> to also persist it to Preferences.
    /// </summary>
    public static void Set(string uid, string displayName, string email = "")
    {
        // Re-setting the same user (e.g. after session restore or role navigation)
        // must not wipe fields that were already loaded for them.
        bool sameUser = uid == Uid;

        Uid = uid;
        if (!string.IsNullOrWhiteSpace(displayName))
            DisplayName = displayName;
        else if (!sameUser)
            DisplayName = "Unknown";

        if (!sameUser)
            StudentNumber = "";

        if (!string.IsNullOrWhiteSpace(email))
            Email = email;
        else if (!sameUser)
            Email = "";
        // NOTE: Do NOT persist here — Set() is called by auth before the real
        // name/studentNumber are known. Persist happens in SetName / SetStudentNumber /
        // SetEmail.
    }

    public static void SetStudentNumber(string studentNumber)
    {
        StudentNumber = studentNumber;
        Preferences.Default.Set(PrefStudentNumber, studentNumber);
    }

    /// <summary>Updates the in-memory display name (call after saving to DB).</summary>
    public static void SetName(string name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            DisplayName = name;
            Preferences.Default.Set(PrefName, name);
        }
    }

    /// <summary>
    /// Persists the Firebase auth email so it survives an app restart.
    /// Call this once after a successful sign-in (LoginPage already knows the email).
    /// </summary>
    public static void SetEmail(string email)
    {
        if (!string.IsNullOrWhiteSpace(email))
        {
            Email = email;
            Preferences.Default.Set(PrefEmail, email);
        }
    }

    /// <summary>
    /// Restores DisplayName, StudentNumber, and Email from Preferences after a
    /// session is rehydrated by TryRestoreSessionAsync on app restart.
    /// Call this immediately after UserSession.Set() inside TryRestoreSessionAsync.
    /// </summary>
    public static void RestorePersistedFields()
    {
        var savedName = Preferences.Default.Get(PrefName, "");
        if (!string.IsNullOrWhiteSpace(savedName))
            DisplayName = savedName;

        var savedSN = Preferences.Default.Get(PrefStudentNumber, "");
        if (!string.IsNullOrWhiteSpace(savedSN))
            StudentNumber = savedSN;

        var savedEmail = Preferences.Default.Get(PrefEmail, "");
        if (!string.IsNullOrWhiteSpace(savedEmail))
            Email = savedEmail;
    }

    public static void Clear()
    {
        Uid = "";
        DisplayName = "Unknown";
        StudentNumber = "";
        Email = "";
        Preferences.Default.Remove(PrefName);
        Preferences.Default.Remove(PrefStudentNumber);
        Preferences.Default.Remove(PrefEmail);
    }

    public static bool IsLoggedIn => !string.IsNullOrEmpty(Uid);
}