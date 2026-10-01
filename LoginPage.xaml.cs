using Android.Webkit;
using Firebase.Messaging;
using SAFETY_STEPS.FireBase;
#if ANDROID
using SAFETY_STEPS.Platforms.Android;
#endif

namespace SAFETY_STEPS;

public partial class LoginPage : ContentPage
{
    private readonly FirebaseAuthService _auth;
    private readonly FirestoreService _firestore;

    public LoginPage(FirebaseAuthService auth, FirestoreService firestore)
    {
        InitializeComponent();
        _auth = auth;
        _firestore = firestore;
    }

    private async void OnSignInClicked(object sender, EventArgs e)
    {
        try
        {
            var studentId = StudentIDEntry.Text?.Trim();
            var password = PasswordEntry.Text;

            if (string.IsNullOrEmpty(studentId) || string.IsNullOrEmpty(password))
            {
                await DisplayAlert("Error", "Please enter your ID and password.", "OK");
                return;
            }

            // ── Enforce exactly 11 digits for the ID number ───────────────────
            if (studentId.Length != 11 || !studentId.All(char.IsDigit))
            {
                await DisplayAlert("Invalid ID", "Your ID number must be exactly 11 digits.", "OK");
                return;
            }

            var fakeEmail = $"{studentId}@safetysteps.app";
            var success = await _auth.SignInAsync(fakeEmail, password);

            if (!success)
            {
                await DisplayAlert("Error", "Invalid ID or password. Please try again.", "OK");
                return;
            }

            // ── FIX (Bug 3): Stamp the Firebase UID into the user doc FIRST ─────
            // RTDB rules that use auth.uid for read access require the uid field to
            // exist before the GetDocumentAsync call below.  On first login after
            // registration this patch writes the uid so the subsequent read succeeds.
            var uidPatched = await _firestore.PatchFieldsAsync("users", _auth.LocalId!,
                new Dictionary<string, object> { { "uid", _auth.LocalId! } });

            if (!uidPatched)
                Console.WriteLine("[Login] ⚠️ uid patch returned false — read may still fail if rules require it.");

            // ── Fetch user profile ────────────────────────────────────────────
            var userData = await _firestore.GetDocumentAsync("users", _auth.LocalId!);

            // ── FIX (Bug 1): Hard-fail instead of silently falling back to "student"
            // If the profile is null the role is genuinely unknown — letting the user
            // in as a student would be a security hole for admins whose signup write
            // failed silently (Bug 2).
            if (userData == null)
            {
                Console.WriteLine($"[Login] Profile not found for UID {_auth.LocalId} — aborting.");
                _auth.SignOut();
                await DisplayAlert("Login Failed",
                    "Your profile could not be found. Please register again or contact support.",
                    "OK");
                return;
            }

            // role field is required; if somehow absent treat it as an error too.
            if (!userData.ContainsKey("role") || string.IsNullOrWhiteSpace(userData["role"]?.ToString()))
            {
                Console.WriteLine($"[Login] Profile for UID {_auth.LocalId} has no role field — aborting.");
                _auth.SignOut();
                await DisplayAlert("Login Failed",
                    "Your account is missing a role. Please contact support.",
                    "OK");
                return;
            }

            var role = userData["role"]!.ToString()!.ToLower();

            // ── Admin access-code gate ────────────────────────────────────────
            if (role == "admin")
            {
                var adminCode = await DisplayPromptAsync(
                    "Admin Verification",
                    "Enter the admin access code to continue:",
                    accept: "Confirm",
                    cancel: "Cancel",
                    placeholder: "Admin code",
                    maxLength: 20,
                    keyboard: Keyboard.Default);

                if (adminCode == null)
                {
                    // User cancelled — sign out and abort
                    _auth.SignOut();
                    return;
                }

                if (adminCode != "Admin123")
                {
                    await DisplayAlert("Access Denied", "Invalid admin access code.", "OK");
                    _auth.SignOut();
                    return;
                }
            }

            // ── Save FCM token for ALL users (student and admin) ──────────────
#if ANDROID
            try
            {
                var tcs = new TaskCompletionSource<string?>();
                FirebaseMessaging.Instance.GetToken()
                    .AddOnCompleteListener(new TokenListener(tcs));

                var fcmToken = await tcs.Task;

                Console.WriteLine($"[FCM] Token fetched for role='{role}': {fcmToken}");

                if (!string.IsNullOrEmpty(fcmToken))
                {
                    Preferences.Set("fcm_token", fcmToken);
                    await _firestore.PatchFieldsAsync("users", _auth.LocalId!,
                        new Dictionary<string, object> { ["fcmToken"] = fcmToken });
                    Console.WriteLine("[FCM] Token saved to DB.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FCM] Token error: {ex.Message}");
            }
#endif

            // Try to get the display name — fall back to studentId if not set
            var userName = userData != null && userData.ContainsKey("name")
                ? userData["name"]?.ToString() ?? studentId
                : studentId;

            // Persist name into session so ProfilePage always sees the correct name
            UserSession.SetName(userName!);

            // FIX: Persist the Firebase email to Preferences so StudentChangePasswordPage
            // can always find it — even after an app restart — without hitting "session error".
            UserSession.SetEmail(fakeEmail);

            // ── Navigate to the correct TabBar via Shell ──────────────
            await AppShell.NavigateByRoleAsync(
                role: role,
                userId: studentId,
                userName: userName,
                auth: _auth,
                firestore: _firestore);

#if ANDROID
            if (role == "admin")
                await AppShell.SyncAdminFcmTokenFromPreferencesAsync(_firestore);
#endif

            // ── Set header labels on HomePage (students only) ─────────
            if (role != "admin" && Shell.Current.CurrentPage is HomePage homePage)
            {
                homePage.SetUserData(studentId, role);
            }
        }
        catch (HttpRequestException)
        {
            await DisplayAlert("Network Error", "Unable to connect. Please check your internet connection and try again.", "OK");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OnSignInClicked] Unexpected error: {ex.Message}");
            await DisplayAlert("Error", "Something went wrong. Please try again.", "OK");
        }
    }

    private async void OnCreateAccountClicked(object sender, EventArgs e)
    {
        try
        {
            await Shell.Current.GoToAsync(nameof(SignupPage));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OnCreateAccountClicked] Navigation error: {ex.Message}");
            await DisplayAlert("Error", "Unable to navigate to sign-up. Please try again.", "OK");
        }
    }
}