using SAFETY_STEPS.FireBase;
using SAFETY_STEPS.Services;

namespace SAFETY_STEPS;

public partial class StudentProfile : ContentPage
{
    private readonly FirebaseAuthService _auth;
    private readonly FirestoreService _firestore;

    public StudentProfile(FirebaseAuthService auth, FirestoreService firestore)
    {
        InitializeComponent();
        _auth = auth;
        _firestore = firestore;
    }

    // ── Lifecycle ────────────────────────────────────────────
    protected override void OnAppearing()
    {
        base.OnAppearing();
        PopulateProfileData();
        LoadProfileImageAsync();          // tries local first, then DB
        EditNameCard.IsVisible = false;
    }

    // ── Settings icon → open flyout ──────────────────────────
    private void OnSettingsTapped(object sender, TappedEventArgs e)
    {
        AppShell.OpenFlyout();
    }

    // ── Populate labels from DB (name + student number) ──────
    private async void PopulateProfileData()
    {
        // Show whatever is cached in the session while the DB fetch is in-flight.
        DisplayNameLabel.Text = UserSession.DisplayName;

        try
        {
            var userDoc = await _firestore.GetDocumentAsync("users", UserSession.Uid);
            if (userDoc != null)
            {
                // ── Display name ──────────────────────────────────────────────
                // Always prefer the "name" field saved in the DB over the raw
                // email/UID that may be sitting in UserSession from sign-in.
                if (userDoc.TryGetValue("name", out var nameVal) &&
                    !string.IsNullOrWhiteSpace(nameVal?.ToString()))
                {
                    var nameStr = nameVal.ToString()!;
                    // Keep session + Preferences in sync so other pages are correct too.
                    UserSession.SetName(nameStr);
                    DisplayNameLabel.Text = nameStr;
                }

                // ── Student number ────────────────────────────────────────────
                // Try "studentNumber" first, fall back to "studentID".
                object? snVal = null;
                if (!userDoc.TryGetValue("studentNumber", out snVal) ||
                    string.IsNullOrWhiteSpace(snVal?.ToString()))
                    userDoc.TryGetValue("studentID", out snVal);

                var sn = snVal?.ToString() ?? "";

                if (!string.IsNullOrWhiteSpace(sn))
                    UserSession.SetStudentNumber(sn);

                UserIdLabel.Text = string.IsNullOrWhiteSpace(sn) ? "—" : sn;
                return;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[StudentProfile] Could not fetch profile data from DB: {ex.Message}");
        }

        // Fallback: use whatever is already in the session cache.
        var cached = UserSession.StudentNumber;
        UserIdLabel.Text = string.IsNullOrWhiteSpace(cached) ? "—" : cached;
    }

    // ── Profile Image ─────────────────────────────────────────
    private static string PrefKey => $"profile_image_{UserSession.Uid}";

    /// <summary>
    /// Load avatar: prefer the locally cached file; fall back to the
    /// base64 blob stored in the DB.
    /// </summary>
    private async void LoadProfileImageAsync()
    {
        // 1 — try local file cached on-device
        string? savedPath = Preferences.Get(PrefKey, null);
        if (!string.IsNullOrWhiteSpace(savedPath) && File.Exists(savedPath))
        {
            ProfileImage.Source = ImageSource.FromFile(savedPath);
            return;
        }

        // 2 — fall back to base64 stored in Realtime DB
        try
        {
            var userDoc = await _firestore.GetDocumentAsync("users", UserSession.Uid);
            if (userDoc != null &&
                userDoc.TryGetValue("profileImageBase64", out var b64Val) &&
                b64Val is string b64 &&
                !string.IsNullOrWhiteSpace(b64))
            {
                byte[] imageBytes = Convert.FromBase64String(b64);

                string destPath = Path.Combine(
                    FileSystem.AppDataDirectory, $"profile_{UserSession.Uid}.jpg");
                await File.WriteAllBytesAsync(destPath, imageBytes);
                Preferences.Set(PrefKey, destPath);

                ProfileImage.Source = ImageSource.FromFile(destPath);
                Console.WriteLine("[StudentProfile] Avatar restored from DB.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[StudentProfile] Could not load avatar from DB: {ex.Message}");
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

            string destDir = FileSystem.AppDataDirectory;
            string destPath = Path.Combine(destDir, $"profile_{UserSession.Uid}.jpg");

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
            byte[] bytes = await File.ReadAllBytesAsync(filePath);
            string b64 = Convert.ToBase64String(bytes);

            bool ok = await _firestore.PatchFieldsAsync("users", UserSession.Uid,
                new Dictionary<string, object> { ["profileImageBase64"] = b64 });

            Console.WriteLine(ok
                ? "[StudentProfile] Profile image uploaded to DB."
                : "[StudentProfile] Profile image upload failed.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[StudentProfile] Image upload error: {ex.Message}");
        }
    }

    // ── Pencil icon toggle ────────────────────────────────────
    private void OnEditNameTapped(object sender, TappedEventArgs e)
    {
        EditNameCard.IsVisible = !EditNameCard.IsVisible;

        if (EditNameCard.IsVisible)
        {
            // Pre-fill with the current name; hide the email/UID placeholder.
            var current = UserSession.DisplayName;
            NameEntry.Text = (current == "Unknown" || current == UserSession.Uid)
                ? ""
                : current;
            NameEntry.Focus();
        }
    }

    // ── Save Name ────────────────────────────────────────────
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
            // Persist name to Realtime DB under the "name" field.
            bool saved = await _firestore.PatchFieldsAsync("users", UserSession.Uid,
                new Dictionary<string, object> { ["name"] = name });
            if (!saved)
            {
                await DisplayAlert("Error",
                    "Could not save your name. Please check your connection and try again.", "OK");
                return;
            }

            // Update in-memory session + Preferences so every page sees it.
            UserSession.SetName(name);

            DisplayNameLabel.Text = name;
            EditNameCard.IsVisible = false;

            await DisplayAlert("Saved", "Your name has been updated.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Could not save name: {ex.Message}", "OK");
        }
    }

    // ── Cancel Name Edit ─────────────────────────────────────
    private void OnCancelNameClicked(object sender, EventArgs e)
    {
        EditNameCard.IsVisible = false;
    }

    // ── Change Password ──────────────────────────────────────
    private async void OnChangePasswordTapped(object sender, TappedEventArgs e)
    {
        await Navigation.PushAsync(new StudentChangePasswordPage(_auth));
    }

    // ── Logout ───────────────────────────────────────────────
    private async void OnLogoutClicked(object sender, EventArgs e)
    {
        bool confirmed = await DisplayAlert(
            "Log Out",
            "Are you sure you want to log out?",
            "Yes, Log Out",
            "Cancel");

        if (!confirmed) return;

        _auth.SignOut();
        await AppShell.LogoutAsync();
    }
}