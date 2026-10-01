using SAFETY_STEPS.FireBase;
using SAFETY_STEPS.Services;
using SAFETY_STEPS.ViewModels;

namespace SAFETY_STEPS;

public partial class HomePage : ContentPage
{
    private readonly FirebaseAuthService _auth;
    private readonly FirestoreService _firestore;
    private readonly CallSignalingService _signal;
    private readonly AdminService _adminService;
    private bool _listenerStarted = false;

    public HomePage(FirebaseAuthService auth, FirestoreService firestore,
                    CallSignalingService signal, AdminService adminService)
    {
        InitializeComponent();
        _auth = auth;
        _firestore = firestore;
        _signal = signal;
        _adminService = adminService;
    }

    // ── Called from AppShell after login succeeds ─────────────────────────
    public void SetUserData(string studentId, string role)
    {
        // Prefer the student number from UserSession (set during login / session restore)
        var sn = UserSession.StudentNumber;
        StudentIDLabel.Text = !string.IsNullOrWhiteSpace(sn) ? sn : studentId;
        if (!string.IsNullOrWhiteSpace(role))
            RoleLabel.Text = char.ToUpper(role[0]) + role.Substring(1);
    }

    // ── Start listening for incoming calls when HomePage is visible ───────
    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (!UserSession.IsLoggedIn) return;

        // FIX: Show the cached student number immediately (from Preferences) so the
        // label is never blank on app restart while the async fetch is in-flight.
        var cachedSN = UserSession.StudentNumber;
        if (string.IsNullOrWhiteSpace(cachedSN))
            cachedSN = Preferences.Get("student_number", "");

        if (!string.IsNullOrWhiteSpace(cachedSN))
            StudentIDLabel.Text = cachedSN;

        _signal.OnIncomingCall -= HandleIncomingCall;
        _signal.OnIncomingCall += HandleIncomingCall;

        if (!_listenerStarted)
        {
            _signal.ListenForIncomingCalls(UserSession.Uid);
            _listenerStarted = true;
        }

        _ = LoadUserProfileAsync();
        _ = LoadReportCountAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _signal.OnIncomingCall -= HandleIncomingCall;
    }

    // ── Fetch the signed-in user's profile from Firestore ────────────────
    private async Task LoadUserProfileAsync()
    {
        try
        {
            string uid = UserSession.Uid;
            if (string.IsNullOrEmpty(uid)) return;

            var doc = await _firestore.GetDocumentAsync("users", uid);
            if (doc == null) return;

            // FIX: Check all three field name variants used across registrations.
            // DB uses "studentID" (capital ID) — the old code checked "studentId"
            // (lowercase d) which never matched, causing the label to stay blank.
            string studentNumber = "";
            foreach (var key in new[] { "studentNumber", "studentID", "studentId" })
            {
                if (doc.TryGetValue(key, out var val) &&
                    !string.IsNullOrWhiteSpace(val?.ToString()))
                {
                    studentNumber = val!.ToString()!;
                    break;
                }
            }

            string role = doc.TryGetValue("role", out var r) ? r?.ToString() ?? "" : "";

            // Persist to UserSession so other pages can access it without another Firestore call
            if (!string.IsNullOrWhiteSpace(studentNumber))
            {
                UserSession.SetStudentNumber(studentNumber);
                Preferences.Set("student_number", studentNumber);
            }

            // Update labels with the real student number
            if (!string.IsNullOrWhiteSpace(studentNumber))
                StudentIDLabel.Text = studentNumber;

            if (!string.IsNullOrWhiteSpace(role))
                RoleLabel.Text = char.ToUpper(role[0]) + role.Substring(1);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HomePage] LoadUserProfileAsync error: {ex.Message}");
        }
    }

    // ── Incoming call → navigate to IncomingCallPage ──────────────────────
    private async void HandleIncomingCall(Models.CallData call)
    {
        if (Navigation.NavigationStack.LastOrDefault() is IncomingCallPage) return;

        var vm = IPlatformApplication.Current!.Services
                     .GetRequiredService<IncomingCallViewModel>();

        vm.NotifyIncomingCall(call);

        var page = new IncomingCallPage(vm);
        await Navigation.PushAsync(page);
    }

    // ── Call Admin button ─────────────────────────────────────────────────
    private async void OnAdminCallTapped(object sender, EventArgs e)
    {
        if (sender is Border btn)
        {
            await btn.ScaleTo(0.93, 80);
            await btn.ScaleTo(1.0, 80);
        }

        var status = await Permissions.CheckStatusAsync<Permissions.Microphone>();
        if (status != PermissionStatus.Granted)
            status = await Permissions.RequestAsync<Permissions.Microphone>();

        if (status != PermissionStatus.Granted)
        {
            await DisplayAlert("Permission Required",
                "Microphone access is needed to make calls.", "OK");
            return;
        }

        if (Navigation.NavigationStack.LastOrDefault() is CallPage) return;

        // ── Resolve admin(s) dynamically ──────────────────────────────────
        var admins = await _adminService.GetAvailableAdminsAsync();

        if (admins.Count == 0)
        {
            await DisplayAlert("No Admin Available",
                "There are no admin accounts registered yet. Please contact your administrator.",
                "OK");
            return;
        }

        var vm = IPlatformApplication.Current!.Services
                     .GetRequiredService<CallViewModel>();

        if (admins.Count == 1)
        {
            vm.ReceiverUserId = admins[0].EffectiveUid;
            vm.ReceiverName = admins[0].DisplayName;
            await Navigation.PushAsync(new CallPage(vm));
        }
        else
        {
            var names = admins.Select(a => a.DisplayName).ToArray();
            string? chosen = await DisplayActionSheet(
                "Select an Admin to Call", "Cancel", null, names);

            if (chosen == null || chosen == "Cancel") return;

            var selected = admins.FirstOrDefault(a => a.DisplayName == chosen);
            if (selected == null) return;

            vm.ReceiverUserId = selected.EffectiveUid;
            vm.ReceiverName = selected.DisplayName;
            await Navigation.PushAsync(new CallPage(vm));
        }
    }

    // ── Load student's recent report count ───────────────────────────────
    private async Task LoadReportCountAsync()
    {
        try
        {
            string uid = UserSession.Uid;
            if (string.IsNullOrEmpty(uid)) return;

            // AlertPage stores the reporter's uid in "submittedBy"
            var reports = await _firestore.QueryCollectionAsync(
                "emergency_reports", ("submittedBy", uid));

            int count = reports?.Count ?? 0;
            ReportCountLabel.Text = count.ToString();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HomePage] LoadReportCountAsync error: {ex.Message}");
        }
    }

    // ── Emergency Alert button → navigate to AlertPage ───────────────────
    private async void OnEmergencyAlertTapped(object sender, TappedEventArgs e)
    {
        if (sender is Border btn)
        {
            await btn.ScaleTo(0.93, 80);
            await btn.ScaleTo(1.0, 80);
        }

        var alertPage = IPlatformApplication.Current!.Services
                            .GetRequiredService<AlertPage>();
        await Navigation.PushAsync(alertPage);
    }

    private static string GetCurrentUserId() => UserSession.Uid;
}