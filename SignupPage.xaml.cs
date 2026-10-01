using SAFETY_STEPS.FireBase;

namespace SAFETY_STEPS;

public partial class SignupPage : ContentPage
{
    private readonly FirebaseAuthService _auth;
    private readonly FirestoreService _firestore;
    private string _selectedRole = string.Empty;

    public SignupPage(FirebaseAuthService auth, FirestoreService firestore)
    {
        InitializeComponent();
        _auth = auth;
        _firestore = firestore;
    }

    private void OnRoleTapped(object sender, TappedEventArgs e)
    {
        ResetBorders();

        if (e.Parameter is string role)
        {
            _selectedRole = role;

            Border? selected = role switch
            {
                "Student" => StudentBorder,
                "Teacher" => TeacherBorder,
                _ => null
            };

            if (selected != null)
            {
                selected.Stroke = new SolidColorBrush(Colors.Blue);
                selected.StrokeThickness = 2;
                selected.Background = new SolidColorBrush(Color.FromArgb("#1A0000FF"));
            }
        }
    }

    private void ResetBorders()
    {
        foreach (var border in new[] { StudentBorder, TeacherBorder })
        {
            border.Stroke = new SolidColorBrush(Colors.Gray);
            border.StrokeThickness = 1;
            border.Background = new SolidColorBrush(Colors.Transparent);
        }
    }

    private async void OnRegisterClicked(object sender, EventArgs e)
    {
        var studentId = RegisterStudentID.Text?.Trim();
        var password = RegisterPassword.Text;
        var confirmPassword = ConfirmPassword.Text;

        if (string.IsNullOrEmpty(studentId) || string.IsNullOrEmpty(password))
        {
            await DisplayAlert("Error", "Please fill in all fields.", "OK");
            return;
        }

        // ── Enforce exactly 11 digits for the ID number ───────────────────────
        if (studentId.Length != 11 || !studentId.All(char.IsDigit))
        {
            await DisplayAlert("Invalid ID", "Your ID number must be exactly 11 digits.", "OK");
            return;
        }

        if (string.IsNullOrEmpty(_selectedRole))
        {
            await DisplayAlert("Error", "Please select a role.", "OK");
            return;
        }

        if (password != confirmPassword)
        {
            await DisplayAlert("Error", "Passwords do not match.", "OK");
            return;
        }

        var fakeEmail = $"{studentId}@safetysteps.app";
        var success = await _auth.SignUpAsync(fakeEmail, password);

        if (success)
        {
            // ── FIX (Bug 2): Check SetDocumentAsync result — don't silently ignore failure ──
            // If the write fails the auth account exists but has no role, causing the
            // "silently becomes student" bug on next login.  Roll back by signing out
            // and alerting the user so they can try again.
            var profileWritten = await _firestore.SetDocumentAsync("users", _auth.LocalId!, new Dictionary<string, object>
            {
                { "studentID",  studentId! },
                { "role",       _selectedRole.ToLower() },   // "student" | "teacher" | "admin"
                { "uid",        _auth.LocalId! },            // write uid at registration time so first-login read succeeds
                { "createdAt",  DateTime.UtcNow.ToString("yyyy-MM-dd") }
            });

            if (!profileWritten)
            {
                // Auth account was created but profile save failed.
                // Sign out so the orphaned auth entry doesn't masquerade as valid.
                Console.WriteLine($"[Signup] Profile write failed for UID {_auth.LocalId} — signing out.");
                _auth.SignOut();
                await DisplayAlert("Registration Failed",
                    "Your account was created but your profile could not be saved. " +
                    "Please try registering again. If the problem persists, contact support.",
                    "OK");
                return;
            }

            await DisplayAlert("Success", "Account created! Please sign in.", "OK");
            // SignupPage is pushed onto the nav stack from LoginPage, not registered
            // as a root Shell route, so GoToAsync("//LoginPage") throws.
            // Use ".." to pop back one level to wherever we came from (LoginPage).
            await Shell.Current.GoToAsync("..");
        }
        else
        {
            await DisplayAlert("Error", "Could not create account. ID may already be registered.", "OK");
        }
    }

    private async void OnSignInClicked(object sender, EventArgs e)
    {
        // Pop back to LoginPage rather than jumping to an absolute route.
        await Shell.Current.GoToAsync("..");
    }
}