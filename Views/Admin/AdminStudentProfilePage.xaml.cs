using SAFETY_STEPS.FireBase;

namespace SAFETY_STEPS;

/// <summary>
/// Admin-only page: loads and displays a student's profile from Firestore
/// by their Firebase UID (which is passed from the Calls Queue page).
/// </summary>
public partial class AdminStudentProfilePage : ContentPage
{
    private readonly FirestoreService _firestore;
    private readonly string _studentUid;

    public AdminStudentProfilePage(FirestoreService firestore, string studentUid)
    {
        InitializeComponent();
        _firestore = firestore;
        _studentUid = studentUid;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadStudentProfileAsync();
    }

    private async Task LoadStudentProfileAsync()
    {
        LoadingIndicator.IsVisible = true;
        LoadingIndicator.IsRunning = true;
        ProfileContent.IsVisible = false;

        try
        {
            // Try direct document lookup first (fastest)
            var doc = await _firestore.GetDocumentAsync("users", _studentUid);

            // Fallback: query by uid field
            if (doc == null)
            {
                var results = await _firestore.QueryCollectionAsync("users", ("uid", _studentUid));
                doc = results.Count > 0 ? results[0] : null;
            }

            PopulateUI(doc);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AdminStudentProfile] Error loading profile: {ex.Message}");
            PopulateUI(null);
        }
        finally
        {
            LoadingIndicator.IsRunning = false;
            LoadingIndicator.IsVisible = false;
            ProfileContent.IsVisible = true;
        }
    }

    private void PopulateUI(Dictionary<string, object>? doc)
    {
        if (doc == null)
        {
            // Show the UID at minimum so the admin knows who this is
            DisplayNameLabel.Text = "Unknown Student";
            UserIdLabel.Text = _studentUid;
            StudentNumberLabel.Text = "—";
            StudentNumberBadge.Text = "";
            EmailLabel.Text = "—";
            FcmTokenLabel.Text = "—";
            NotFoundBanner.IsVisible = true;
            return;
        }

        NotFoundBanner.IsVisible = false;

        // Returns the first non-empty value among the given keys, or "—".
        // Sign-up writes "name" and "studentID"; older docs may use the other spellings.
        string Get(params string[] keys)
        {
            foreach (var key in keys)
                if (doc.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v?.ToString()))
                    return v!.ToString()!;
            return "—";
        }

        string displayName = Get("name", "displayName");
        string studentNumber = Get("studentNumber", "studentID", "studentId");
        string email = Get("email");
        string uid = Get("uid");
        string fcmToken = Get("fcmToken");

        // Accounts sign in as {studentID}@safetysteps.app
        if (email == "—" && studentNumber != "—")
            email = $"{studentNumber}@safetysteps.app";

        if (displayName == "—" || displayName.Contains('@'))
            displayName = studentNumber != "—" ? studentNumber : "Unknown";

        DisplayNameLabel.Text = displayName;
        UserIdLabel.Text = uid == "—" ? _studentUid : uid;

        if (!string.IsNullOrWhiteSpace(studentNumber) && studentNumber != "—")
        {
            StudentNumberLabel.Text = studentNumber;
            StudentNumberBadge.Text = $"SN: {studentNumber}";
            StudentNumberBadge.IsVisible = true;
        }
        else
        {
            StudentNumberLabel.Text = "Not provided";
            StudentNumberBadge.IsVisible = false;
        }

        EmailLabel.Text = email;

        // Truncate FCM token for display
        FcmTokenLabel.Text = fcmToken == "—" || string.IsNullOrEmpty(fcmToken)
            ? "Not registered"
            : fcmToken.Length > 40
                ? fcmToken[..40] + "…"
                : fcmToken;
    }
}