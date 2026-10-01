using SAFETY_STEPS.FireBase;
using SAFETY_STEPS.Services;

namespace SAFETY_STEPS;

public partial class AdminProfile : ContentPage
{
    private readonly FirebaseAuthService _auth;
    private readonly FirestoreService _firestore;

    public AdminProfile(FirebaseAuthService auth, FirestoreService firestore)
    {
        InitializeComponent();
        _auth = auth;
        _firestore = firestore;
    }

    // ── Lifecycle ────────────────────────────────────────────────────────
    protected override void OnAppearing()
    {
        base.OnAppearing();
        PopulateProfileData();
        LoadProfileImageAsync();
        EditNameCard.IsVisible = false;
    }

    // ── Settings icon → open flyout ──────────────────────────────────────
    private void OnSettingsTapped(object sender, TappedEventArgs e)
    {
        AppShell.OpenFlyout();
    }

    // ── Profile data — load from DB, fall back to session cache ──────────
    private async void PopulateProfileData()
    {
        // Show cached values immediately while the DB fetch is in-flight.
        AdminNameLabel.Text = UserSession.DisplayName;

        try
        {
            var userDoc = await _firestore.GetDocumentAsync("users", UserSession.Uid);
            if (userDoc != null)
            {
                // ── Display name ─────────────────────────────────────────────
                if (userDoc.TryGetValue("name", out var nameVal) &&
                    !string.IsNullOrWhiteSpace(nameVal?.ToString()))
                {
                    var name = nameVal.ToString()!;
                    UserSession.SetName(name);
                    Preferences.Set("user_name", name);
                    AdminNameLabel.Text = name;
                }

                // ── ID — same fallback chain as StudentProfile ────────────────
                // Try "studentID" first, then "studentNumber", then "adminId".
                object? idVal = null;
                if (!userDoc.TryGetValue("studentID", out idVal) ||
                    string.IsNullOrWhiteSpace(idVal?.ToString()))
                    userDoc.TryGetValue("studentNumber", out idVal);

                if (string.IsNullOrWhiteSpace(idVal?.ToString()))
                    userDoc.TryGetValue("adminId", out idVal);

                var id = idVal?.ToString() ?? "";
                AdminIDLabel.Text = string.IsNullOrWhiteSpace(id) ? "—" : id;
                return;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AdminProfile] Could not fetch profile data: {ex.Message}");
        }

        // Fallback: session/preferences cache
        var cached = Preferences.Get("user_id", "");
        AdminIDLabel.Text = string.IsNullOrWhiteSpace(cached) ? "—" : cached;
    }

    // ── Profile image ────────────────────────────────────────────────────

    private static string PrefKey => $"profile_image_{UserSession.Uid}";

    private async void LoadProfileImageAsync()
    {
        // 1 — local cache
        var savedPath = Preferences.Get(PrefKey, "");
        if (!string.IsNullOrWhiteSpace(savedPath) && File.Exists(savedPath))
        {
            ProfileImage.Source = ImageSource.FromFile(savedPath);
            return;
        }

        // 2 — base64 blob in DB
        try
        {
            var userDoc = await _firestore.GetDocumentAsync("users", UserSession.Uid);
            if (userDoc != null &&
                userDoc.TryGetValue("profileImageBase64", out var b64Val) &&
                b64Val is string b64 &&
                !string.IsNullOrWhiteSpace(b64))
            {
                var bytes = Convert.FromBase64String(b64);
                var destPath = Path.Combine(
                    FileSystem.AppDataDirectory, $"profile_{UserSession.Uid}.jpg");

                await File.WriteAllBytesAsync(destPath, bytes);
                Preferences.Set(PrefKey, destPath);
                ProfileImage.Source = ImageSource.FromFile(destPath);

                Console.WriteLine("[AdminProfile] Avatar restored from DB.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AdminProfile] Could not load avatar from DB: {ex.Message}");
        }
    }

    private async void OnProfileImageClicked(object sender, EventArgs e)
    {
        try
        {
            var photo = await MediaPicker.Default.PickPhotoAsync(new MediaPickerOptions
            {
                Title = "Choose profile photo"
            });

            if (photo is null) return;

            var destPath = Path.Combine(
                FileSystem.AppDataDirectory, $"profile_{UserSession.Uid}.jpg");

            // Resize before saving/uploading; WriteAllBytes also truncates any older, larger file.
            var bytes = await ImageHelper.LoadCompressedJpegAsync(photo, ImageHelper.ProfilePhotoMaxSize, 80);
            await File.WriteAllBytesAsync(destPath, bytes);

            // FromStream so the Image control doesn't show a cached copy of the old file.
            ProfileImage.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
            Preferences.Set(PrefKey, destPath);

            await UploadProfileImageToDbAsync(destPath);
        }
        catch (PermissionException)
        {
            await DisplayAlert("Permission Required",
                "Please allow access to your photos in your device settings.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Could not load photo: {ex.Message}", "OK");
        }
    }

    private async Task UploadProfileImageToDbAsync(string filePath)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(filePath);
            var b64 = Convert.ToBase64String(bytes);

            bool ok = await _firestore.PatchFieldsAsync("users", UserSession.Uid,
                new Dictionary<string, object> { ["profileImageBase64"] = b64 });

            Console.WriteLine(ok
                ? "[AdminProfile] Profile image uploaded to DB."
                : "[AdminProfile] Profile image upload failed.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AdminProfile] Image upload error: {ex.Message}");
        }
    }

    // ── Inline name editing ──────────────────────────────────────────────
    private void OnEditNameTapped(object sender, TappedEventArgs e)
    {
        EditNameCard.IsVisible = !EditNameCard.IsVisible;

        if (EditNameCard.IsVisible)
        {
            var current = UserSession.DisplayName;
            NameEntry.Text = (current == "Unknown" || current == UserSession.Uid)
                ? ""
                : current;
            NameEntry.Focus();
        }
    }

    private async void OnSaveNameClicked(object sender, EventArgs e)
    {
        var name = NameEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            await DisplayAlert("Error", "Please enter a name.", "OK");
            return;
        }

        try
        {
            bool saved = await _firestore.PatchFieldsAsync("users", UserSession.Uid,
                new Dictionary<string, object> { ["name"] = name });
            if (!saved)
            {
                await DisplayAlert("Error",
                    "Could not save your name. Please check your connection and try again.", "OK");
                return;
            }

            UserSession.SetName(name);
            Preferences.Set("user_name", name);

            AdminNameLabel.Text = name;
            EditNameCard.IsVisible = false;

            await DisplayAlert("Saved", "Your name has been updated.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Could not save name: {ex.Message}", "OK");
        }
    }

    private void OnCancelNameClicked(object sender, EventArgs e)
    {
        EditNameCard.IsVisible = false;
    }

    // ── Existing handlers ────────────────────────────────────────────────
    private async void OnResetPasswordTapped(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new AdminResetPasswordPage(_firestore, _auth));
    }

    private async void OnLogoutTapped(object sender, EventArgs e)
    {
        await LogoutButton.ScaleTo(0.95, 80);
        await LogoutButton.ScaleTo(1.0, 80);

        bool confirm = await DisplayAlert(
            "Log Out", "Are you sure you want to log out?", "Yes", "Cancel");

        if (!confirm) return;

        _auth.SignOut();
        await AppShell.LogoutAsync();
    }
}