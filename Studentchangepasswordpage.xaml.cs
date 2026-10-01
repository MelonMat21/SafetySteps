using SAFETY_STEPS.FireBase;

namespace SAFETY_STEPS;

public partial class StudentChangePasswordPage : ContentPage
{
    private readonly FirebaseAuthService _auth;

    public StudentChangePasswordPage(FirebaseAuthService auth)
    {
        InitializeComponent();
        _auth = auth;
    }

    // ── Back arrow ────────────────────────────────────────────
    private async void OnBackTapped(object sender, TappedEventArgs e)
    {
        await Navigation.PopAsync();
    }

    // ── Save / Update password ────────────────────────────────
    private async void OnSavePasswordClicked(object sender, EventArgs e)
    {
        // Not trimmed: sign-up stores passwords as typed, so spaces are significant.
        var current = CurrentPasswordEntry.Text;
        var newPwd = NewPasswordEntry.Text;
        var confirm = ConfirmPasswordEntry.Text;

        // ── Validation ────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(current))
        {
            ShowStatus("Please enter your current password.", isError: true);
            return;
        }

        if (string.IsNullOrWhiteSpace(newPwd) || newPwd.Length < 6)
        {
            ShowStatus("New password must be at least 6 characters.", isError: true);
            return;
        }

        if (newPwd != confirm)
        {
            ShowStatus("New passwords do not match.", isError: true);
            return;
        }

        if (newPwd == current)
        {
            ShowStatus("New password must be different from current password.", isError: true);
            return;
        }

        // ── Disable button while working ──────────────────────
        SaveButton.IsEnabled = false;
        SaveButton.Text = "Updating…";
        StatusLabel.IsVisible = false;

        try
        {
            // ── Resolve the Firebase email ────────────────────────────────────
            // Primary: use the email stored in UserSession.
            // Fallback: reconstruct it from the student number (format used at sign-up).
            // This handles the case where the app was restarted and SetEmail was never
            // persisted to Preferences in an older version.
            var email = UserSession.Email;

            if (string.IsNullOrWhiteSpace(email))
            {
                var sn = UserSession.StudentNumber;
                if (!string.IsNullOrWhiteSpace(sn))
                    email = $"{sn}@safetysteps.app";
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                ShowStatus("Session error — please log out and sign in again.", isError: true);
                return;
            }

            // Step 1 — re-authenticate with the current password
            bool reAuthOk = await _auth.SignInAsync(email, current);
            if (!reAuthOk)
            {
                ShowStatus("Current password is incorrect.", isError: true);
                return;
            }

            // Step 2 — change to the new password
            var (changed, errorMsg) = await _auth.ChangePasswordAsync(newPwd);
            if (!changed)
            {
                ShowStatus(errorMsg ?? "Failed to update password. Please try again.", isError: true);
                return;
            }

            // ── Success: show message, sign out, then go to Login ─────────────
            ShowStatus("✅ Password updated! Please log in with your new password.", isError: false);

            CurrentPasswordEntry.Text = "";
            NewPasswordEntry.Text = "";
            ConfirmPasswordEntry.Text = "";

            await Task.Delay(2000);

            // Sign out fully so tokens and session are cleared
            _auth.SignOut();
            UserSession.Clear();

            // Navigate back to the Login page
            await Shell.Current.GoToAsync("//LoginPage");
        }
        catch (Exception ex)
        {
            ShowStatus($"Unexpected error: {ex.Message}", isError: true);
        }
        finally
        {
            SaveButton.IsEnabled = true;
            SaveButton.Text = "Update Password";
        }
    }

    // ── Helper ─────────────────────────────────────────────
    private void ShowStatus(string message, bool isError)
    {
        StatusLabel.Text = message;
        StatusLabel.TextColor = isError ? Color.FromArgb("#D32F2F") : Color.FromArgb("#2E7D32");
        StatusLabel.IsVisible = true;
    }
}