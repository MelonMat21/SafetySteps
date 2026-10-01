using SAFETY_STEPS.FireBase;

namespace SAFETY_STEPS.Services;

/// <summary>
/// Resolves admin accounts dynamically from the Realtime Database.
/// Replaces the old hardcoded AppConstants.AdminFirebaseUid.
/// Supports multiple admin accounts — any user with role == "admin".
/// </summary>
public class AdminService
{
    private readonly FirestoreService _db;

    public AdminService(FirestoreService db) => _db = db;

    /// <summary>
    /// Returns all users in the database whose role is "admin".
    /// Each item has: uid, displayName, fcmToken (may be empty).
    /// </summary>
    public async Task<List<AdminInfo>> GetAvailableAdminsAsync()
    {
        try
        {
            var docs = await _db.QueryCollectionAsync("users", ("role", "admin"));

            return docs
                .Select(d =>
                {
                    // "uid" field inside the doc (may be empty if doc was push-written)
                    var uid = d.TryGetValue("uid", out var u) ? u?.ToString() ?? "" : "";
                    // RTDB node key — always the Firebase UID when written with PUT /users/{uid}
                    var docId = d.TryGetValue("docId", out var id) ? id?.ToString() ?? "" : "";

                    // Read "name" first, fall back to "displayName", then "email"
                    var name = "";
                    foreach (var key in new[] { "name", "displayName", "email" })
                    {
                        if (d.TryGetValue(key, out var n) && !string.IsNullOrWhiteSpace(n?.ToString()))
                        {
                            name = n!.ToString()!;
                            break;
                        }
                    }

                    var effectiveUid = !string.IsNullOrEmpty(uid) ? uid : docId;

                    Console.WriteLine($"[AdminService] Resolved admin — uid='{uid}' docId='{docId}' effectiveUid='{effectiveUid}' name='{name}'");

                    return new AdminInfo
                    {
                        Uid = effectiveUid,
                        DocId = docId,
                        DisplayName = !string.IsNullOrEmpty(name) ? name : "Admin",
                        FcmToken = d.TryGetValue("fcmToken", out var ft) ? ft?.ToString() ?? "" : "",
                    };
                })
                .Where(a => !string.IsNullOrEmpty(a.Uid))
                .ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AdminService] GetAvailableAdminsAsync error: {ex.Message}");
            return new List<AdminInfo>();
        }
    }

    /// <summary>
    /// Returns the first available admin UID. Used when the student just needs
    /// any admin (legacy single-admin behaviour as a fallback).
    /// </summary>
    public async Task<string?> GetFirstAdminUidAsync()
    {
        var admins = await GetAvailableAdminsAsync();
        if (admins.Count == 0) return null;

        // Prefer admin whose docId == uid (means their user doc was written correctly)
        var best = admins.FirstOrDefault(a => a.Uid == a.DocId) ?? admins[0];
        return !string.IsNullOrEmpty(best.Uid) ? best.Uid : best.DocId;
    }
}

public class AdminInfo
{
    public string Uid { get; init; } = "";
    public string DocId { get; init; } = "";
    public string DisplayName { get; init; } = "Admin";
    public string FcmToken { get; init; } = "";

    /// <summary>The effective UID — prefers Uid field, falls back to DocId.</summary>
    public string EffectiveUid => !string.IsNullOrEmpty(Uid) ? Uid : DocId;
}