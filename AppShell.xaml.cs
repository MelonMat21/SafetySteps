using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using SAFETY_STEPS.FireBase;
using SAFETY_STEPS.ViewModels;

namespace SAFETY_STEPS
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            Routing.RegisterRoute(nameof(SignupPage), typeof(SignupPage));
            Routing.RegisterRoute(nameof(IncomingCallPage), typeof(IncomingCallPage));
            Routing.RegisterRoute(nameof(CallPage), typeof(CallPage));
        }

        // ── Flyout footer: Log Out ────────────────────────────────────
        private async void OnFlyoutLogoutTapped(object sender, TappedEventArgs e)
        {
            // Close the flyout first so the confirm dialog appears over a clean UI.
            Shell.Current.FlyoutIsPresented = false;

            bool confirmed = await Shell.Current.DisplayAlert(
                "Log Out",
                "Are you sure you want to log out?",
                "Yes, Log Out",
                "Cancel");

            if (!confirmed) return;

            // Sign out of Firebase so the token is invalidated locally.
            var auth = IPlatformApplication.Current?.Services
                           .GetService<FirebaseAuthService>();
            auth?.SignOut();   // also calls UserSession.Clear() + SessionService.Clear()

            // Full cleanup (ViewModel teardown, Preferences wipe, navigation).
            await LogoutAsync();
        }

        /// <summary>
        /// Opens the flyout programmatically from a settings button tap.
        /// Temporarily re-enables FlyoutBehavior so the panel can be presented,
        /// then disables the swipe gesture again the moment it closes.
        /// Use this from every page's settings-icon handler instead of
        /// toggling FlyoutBehavior/FlyoutIsPresented manually in each page.
        /// </summary>
        public static void OpenFlyout()
        {
            // FlyoutBehavior.Disabled also blocks programmatic opening,
            // so switch to Flyout first, then present.
            Shell.Current.FlyoutBehavior = FlyoutBehavior.Flyout;
            Shell.Current.FlyoutIsPresented = true;

            // Re-disable the swipe gesture the moment the panel closes.
            void OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(Shell.FlyoutIsPresented) &&
                    !Shell.Current.FlyoutIsPresented)
                {
                    Shell.Current.FlyoutBehavior = FlyoutBehavior.Disabled;
                    Shell.Current.PropertyChanged -= OnPropertyChanged;
                }
            }

            Shell.Current.PropertyChanged += OnPropertyChanged;
        }

        /// <summary>
        /// Writes Preferences fcm_token to Firestore for the current <see cref="UserSession.Uid"/> (admin device).
        /// Call after <see cref="UserSession"/> is set so OnNewToken (which may run before login) is reconciled.
        /// </summary>
        public static async Task SyncAdminFcmTokenFromPreferencesAsync(FirestoreService firestore)
        {
#if ANDROID
            var savedToken = Preferences.Get("fcm_token", "");
            var uid = UserSession.Uid;
            if (string.IsNullOrEmpty(savedToken) || string.IsNullOrEmpty(uid))
            {
                Console.WriteLine($"[FCM] Sync skipped — prefs token empty={string.IsNullOrEmpty(savedToken)}, UserSession.Uid empty={string.IsNullOrEmpty(uid)}");
                return;
            }

            try
            {
                var ok = await firestore.PatchFieldsAsync("users", uid,
                    new Dictionary<string, object> { { "fcmToken", savedToken } });
                Console.WriteLine(ok
                    ? $"[FCM] Token synced to Firestore for uid='{uid}' (length={savedToken.Length})."
                    : "[FCM] Token sync PATCH failed — see [PatchFieldsAsync] FAILED log.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FCM] Token sync error: {ex.Message}");
            }
#else
            await Task.CompletedTask;
#endif
        }

        /// <summary>
        /// Called after login succeeds. Navigates to the correct TabBar based on the user's role.
        /// </summary>
        public static async Task NavigateByRoleAsync(string role,
                                              string userId,
                                              string userName,
                                              FirebaseAuthService auth,
                                              FirestoreService firestore,
                                              IncomingCallViewModel? incomingVm = null,
                                              Dictionary<string, object>? userDoc = null)
        {
            Preferences.Set("user_id", userId);
            Preferences.Set("user_name", userName);
            Preferences.Set("user_role", role);

            var sessionUid = !string.IsNullOrEmpty(auth.LocalId) ? auth.LocalId! : userId;
            UserSession.Set(sessionUid, userName);

            if (role == "admin")
            {
#if ANDROID
                _ = SyncAdminFcmTokenFromPreferencesAsync(firestore);

                var vm = incomingVm
                    ?? IPlatformApplication.Current?.Services.GetService<IncomingCallViewModel>();
                if (vm != null)
                {
                    var context = Android.App.Application.Context;
                    vm.RegisterIncomingNavigationHandler(NavigateAdminToIncomingCallPage);
                    vm.EnsureInitialized(context);
                }
#endif
                await Shell.Current.GoToAsync("//AdminTabs/AdminHome");
            }
            else
            {
                // ── Load student number + display name from Firestore ─────────
                // (reuses the doc the login page already fetched, when given)
                try
                {
                    userDoc ??= await firestore.GetFieldsAsync("users", sessionUid,
                        "name", "studentNumber", "studentID");
                    if (userDoc != null)
                    {
                        // Display name — prefer the saved "name" field over the email
                        if (userDoc.TryGetValue("name", out var nameVal) &&
                            !string.IsNullOrWhiteSpace(nameVal?.ToString()))
                        {
                            UserSession.SetName(nameVal.ToString()!);
                        }

                        // Student number
                        var sn = userDoc.TryGetValue("studentNumber", out var sn1) ? sn1?.ToString() ?? "" : "";
                        if (string.IsNullOrWhiteSpace(sn))
                            sn = userDoc.TryGetValue("studentID", out var sn2) ? sn2?.ToString() ?? "" : "";

                        var snStr = sn?.ToString() ?? "";
                        UserSession.SetStudentNumber(snStr);
                        Preferences.Set("student_number", snStr);
                        Console.WriteLine($"[AppShell] Student number loaded: '{snStr}'");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AppShell] Failed to load student data: {ex.Message}");
                }
                // ─────────────────────────────────────────────────────────────

                await Shell.Current.GoToAsync("//StudentTabs/Home");

                await Task.Delay(100);

                if (Shell.Current.CurrentPage is HomePage homePage)
                {
                    homePage.SetUserData(userId, role);
                }
            }
        }

        /// <summary>Returns to the signed-in user's own Profile tab (admin or student).</summary>
        public static Task GoToProfileAsync()
            => Shell.Current.GoToAsync(Preferences.Get("user_role", "") == "admin"
                ? "//AdminTabs/AdminProfile"
                : "//StudentTabs/Profile");

        /// <summary>Opens the admin Calls tab and the incoming-call UI (shared by App startup and post-login).</summary>
        public static void NavigateAdminToIncomingCallPage()
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Shell.Current.GoToAsync("//AdminTabs/AdminCalls");
                await Shell.Current.GoToAsync(nameof(IncomingCallPage));
            });
        }

        public static Task NavigateAdminToIncomingCallPageAsync()
            => MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await Shell.Current.GoToAsync("//AdminTabs/AdminCalls");
                await Shell.Current.GoToAsync(nameof(IncomingCallPage));
            });

        /// <summary>
        /// Called on logout — cleans up and goes back to Login screen.
        /// </summary>
        public static async Task LogoutAsync()
        {
            try
            {
                if (IPlatformApplication.Current?.Services.GetService<IncomingCallViewModel>() is { } incomingVm)
                {
                    incomingVm.ClearIncomingNavigationHandler();
                    incomingVm.Cleanup();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Logout] IncomingCall cleanup: {ex.Message}");
            }

            Preferences.Remove("user_id");
            Preferences.Remove("user_name");
            Preferences.Remove("user_role");

            UserSession.Clear();

            await Shell.Current.GoToAsync("//LoginPage");
        }
    }
}