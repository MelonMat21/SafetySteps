namespace SAFETY_STEPS.FireBase;

/// <summary>
/// Keeps base64 photos out of the report and user docs.
///
/// Photos used to be stored inline (emergency_reports/{id}/photoBase64 and
/// users/{uid}/profileImageBase64), so every report list, name lookup and status
/// poll downloaded every photo. They now live in their own nodes:
///   report_photos/{reportId}/photoBase64
///   user_photos/{uid}/profileImageBase64
///
/// Reads fall back to the old inline field, and writes fall back to it if the new
/// node is rejected (e.g. database rules not deployed yet), so nothing is lost.
/// </summary>
public static class PhotoStore
{
    private const string ReportPhotos = "report_photos";
    private const string UserPhotos = "user_photos";
    private const string ReportField = "photoBase64";
    private const string ProfileField = "profileImageBase64";

    private const string ReportsMigratedKey = "report_photos_migrated_v1";

    // ── Report photos ─────────────────────────────────────────────────────
    public static async Task<string> GetReportPhotoAsync(this FirestoreService db, string reportId)
    {
        var photo = await GetStringAsync(db, ReportPhotos, reportId, ReportField);
        return !string.IsNullOrEmpty(photo)
            ? photo
            : await GetStringAsync(db, "emergency_reports", reportId, ReportField);
    }

    /// <summary>Call after the report doc exists (the DB rules check its submittedBy).</summary>
    public static async Task<bool> SaveReportPhotoAsync(this FirestoreService db, string reportId, string base64)
    {
        if (await db.SetDocumentAsync(ReportPhotos, reportId,
                new Dictionary<string, object> { [ReportField] = base64 }))
            return true;

        Console.WriteLine($"[PhotoStore] {ReportPhotos} write rejected — storing photo inline on the report.");
        return await db.PatchFieldsAsync("emergency_reports", reportId,
            new Dictionary<string, object> { [ReportField] = base64 });
    }

    // ── Profile photos ────────────────────────────────────────────────────
    public static async Task<string> GetProfilePhotoAsync(this FirestoreService db, string uid)
    {
        var photo = await GetStringAsync(db, UserPhotos, uid, ProfileField);
        return !string.IsNullOrEmpty(photo)
            ? photo
            : await GetStringAsync(db, "users", uid, ProfileField);
    }

    public static async Task<bool> SaveProfilePhotoAsync(this FirestoreService db, string uid, string base64)
    {
        if (await db.SetDocumentAsync(UserPhotos, uid,
                new Dictionary<string, object> { [ProfileField] = base64 }))
        {
            // Drop any old inline copy so the user doc stays small.
            await db.PatchFieldsAsync("users", uid, new Dictionary<string, object> { [ProfileField] = null! });
            return true;
        }

        Console.WriteLine($"[PhotoStore] {UserPhotos} write rejected — storing photo inline on the user.");
        return await db.PatchFieldsAsync("users", uid,
            new Dictionary<string, object> { [ProfileField] = base64 });
    }

    // ── One-time moves of existing inline photos ──────────────────────────

    /// <summary>Moves the signed-in user's own inline profile photo (only they can write it).</summary>
    public static async Task MigrateOwnProfilePhotoAsync(this FirestoreService db, string uid)
    {
        if (string.IsNullOrEmpty(uid)) return;
        try
        {
            var inline = await GetStringAsync(db, "users", uid, ProfileField);
            if (string.IsNullOrEmpty(inline)) return;

            if (await db.SetDocumentAsync(UserPhotos, uid,
                    new Dictionary<string, object> { [ProfileField] = inline }))
            {
                await db.PatchFieldsAsync("users", uid, new Dictionary<string, object> { [ProfileField] = null! });
                Console.WriteLine("[PhotoStore] Moved own profile photo out of the user doc.");
            }
        }
        catch (Exception ex) { Console.WriteLine($"[PhotoStore] Profile migration: {ex.Message}"); }
    }

    /// <summary>
    /// Admin only: moves every report's inline photo into report_photos. Runs once per
    /// install; if any move fails it simply runs again next time.
    /// </summary>
    public static async Task MigrateReportPhotosAsync(this FirestoreService db)
    {
        if (Preferences.Get(ReportsMigratedKey, false)) return;
        try
        {
            var allOk = true;
            foreach (var reportId in await db.GetKeysAsync("emergency_reports"))
            {
                var inline = await GetStringAsync(db, "emergency_reports", reportId, ReportField);
                if (string.IsNullOrEmpty(inline)) continue;

                if (await db.SetDocumentAsync(ReportPhotos, reportId,
                        new Dictionary<string, object> { [ReportField] = inline }))
                    await db.PatchFieldsAsync("emergency_reports", reportId,
                        new Dictionary<string, object> { [ReportField] = null! });
                else
                    allOk = false;
            }

            if (allOk) Preferences.Set(ReportsMigratedKey, true);
            Console.WriteLine($"[PhotoStore] Report photo migration finished (complete={allOk}).");
        }
        catch (Exception ex) { Console.WriteLine($"[PhotoStore] Report migration: {ex.Message}"); }
    }

    private static async Task<string> GetStringAsync(FirestoreService db, string collection, string id, string field)
    {
        var fields = await db.GetFieldsAsync(collection, id, field);
        return fields.TryGetValue(field, out var v) ? v?.ToString() ?? "" : "";
    }
}
