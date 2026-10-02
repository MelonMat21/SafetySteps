namespace SAFETY_STEPS.FireBase;

public static class FirebaseConfig
{
    public static string ApiKey => Env.FirebaseApiKey;
    public const string ProjectId = "safetysteps-f4f77";

    // ✅ Correct Realtime Database URL (from your Firebase console screenshot)
    public const string DatabaseUrl = "https://safetysteps-f4f77-default-rtdb.asia-southeast1.firebasedatabase.app";

    // ── GOOGLE MAPS ──────────────────────────────────────────────────────
    public const string MapsApiKey = "YOUR_GOOGLE_MAPS_API_KEY_HERE";

    // ── Auth endpoints ────────────────────────────────────────────────────
    public const string AuthSignUp = "https://identitytoolkit.googleapis.com/v1/accounts:signUp";
    public const string AuthSignIn = "https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword";
    public const string AuthRefresh = "https://securetoken.googleapis.com/v1/token";

    // ── Realtime Database base URL ────────────────────────────────────────
    // Use this in RealtimeDatabaseService.cs — no Firestore URL needed anymore.
    public static string RealtimeDb => DatabaseUrl;

    // ── REMOVED: FirestoreUrl ─────────────────────────────────────────────
    // The old Firestore REST URL is no longer used. Delete any references to
    // FirebaseConfig.FirestoreUrl in your project.
}