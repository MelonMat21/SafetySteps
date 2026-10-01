using SAFETY_STEPS.FireBase;
using SAFETY_STEPS.Services;
using SAFETY_STEPS.ViewModels;

#if ANDROID
using SAFETY_STEPS.Platforms.Android;
#endif

namespace SAFETY_STEPS;

public partial class App : Application
{
    public static FirebaseAuthService AuthService { get; private set; } = null!;

    private IncomingCallViewModel? _incomingCallVm;
    private readonly FirestoreService _firestore;

    public App(FirebaseAuthService authService, IncomingCallViewModel incomingCallVm,
               FirestoreService firestore)
    {
        InitializeComponent();
        AuthService = authService;
        _incomingCallVm = incomingCallVm;
        _firestore = firestore;
        MainPage = new AppShell();

        _ = CheckSessionAsync();
    }

    private async Task CheckSessionAsync()
    {
        bool restored = await AuthService.TryRestoreSessionAsync();

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (restored)
            {
                var role = Preferences.Get("user_role", "student");

                if (role == "admin")
                {
#if ANDROID
                    var firebaseUid = AuthService.LocalId ?? "";
                    var savedUserName = Preferences.Get("user_name", "");
                    UserSession.Set(firebaseUid, savedUserName);

                    try
                    {
                        var tcs = new TaskCompletionSource<string?>();
                        Firebase.Messaging.FirebaseMessaging.Instance
                            .GetToken()
                            .AddOnCompleteListener(new TokenListener(tcs));

                        var token = await tcs.Task;
                        Console.WriteLine($"[FCM] Auto-login token: {token}");

                        if (!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(firebaseUid))
                        {
                            Preferences.Set("fcm_token", token);
                            await _firestore.PatchFieldsAsync("users", firebaseUid,
                                new Dictionary<string, object> { { "fcmToken", token } });
                            Console.WriteLine("[FCM] Token saved on auto-login.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[FCM] Auto-login token error: {ex.Message}");
                    }

                    var context = Android.App.Application.Context;
                    _incomingCallVm!.RegisterIncomingNavigationHandler(AppShell.NavigateAdminToIncomingCallPage);
                    _incomingCallVm!.EnsureInitialized(context);
#endif
                    await Shell.Current.GoToAsync("//AdminTabs/AdminHome");
                }
                else
                {
                    // ── NEW: restore student number from Preferences first ─────
                    // (avoids an extra Firestore round-trip on every cold start)
                    var savedStudentNumber = Preferences.Get("student_number", "");
                    if (!string.IsNullOrEmpty(savedStudentNumber))
                        UserSession.SetStudentNumber(savedStudentNumber);

                    var firebaseUid = AuthService.LocalId ?? "";
                    var savedUserName = Preferences.Get("user_name", "");
                    UserSession.Set(firebaseUid, savedUserName);

                    // Re-apply student number (Set() resets it)
                    if (!string.IsNullOrEmpty(savedStudentNumber))
                        UserSession.SetStudentNumber(savedStudentNumber);
                    // ─────────────────────────────────────────────────────────────

                    await Shell.Current.GoToAsync("//StudentTabs/Home");
                }
            }
            else
            {
                await Shell.Current.GoToAsync("//LoginPage");
            }
        });
    }
}