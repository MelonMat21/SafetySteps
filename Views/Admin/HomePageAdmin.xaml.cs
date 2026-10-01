using SAFETY_STEPS.FireBase;
using SAFETY_STEPS.Services;

namespace SAFETY_STEPS;

public partial class HomePageAdmin : ContentPage
{
    private readonly FirebaseAuthService _auth;
    private readonly FirestoreService _firestore;
    private readonly CallSignalingService _signal;

    // How many recent reports to show in the Report Updates card.
    private const int MaxRecentReports = 5;

    public HomePageAdmin(FirebaseAuthService auth, FirestoreService firestore, CallSignalingService signal)
    {
        InitializeComponent();
        _auth = auth;
        _firestore = firestore;
        _signal = signal;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        var adminId = Preferences.Get("user_id", "Admin");
        AdminIDLabel.Text = adminId;

        // Run both fetches concurrently so the page loads faster.
        await Task.WhenAll(
            RefreshQueueCountersAsync(),
            LoadRecentReportsAsync()
        );
    }

    // ── Call queue counters (existing) ───────────────────────────────────
    private async Task RefreshQueueCountersAsync()
    {
        try
        {
            var pendingCalls = await _signal.GetPendingCallsForAdminAsync(UserSession.Uid);
            var urgentCount = pendingCalls.Count(c => c.IsUrgent);
            CallQueueCountLabel.Text = pendingCalls.Count.ToString();
            UrgentCountLabel.Text = $"URGENT {urgentCount}";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HomePageAdmin] RefreshQueueCounters error: {ex.Message}");
        }
    }

    // ── Recent reports ───────────────────────────────────────────────────
    private async Task LoadRecentReportsAsync()
    {
        try
        {
            // Fetch all reports then sort client-side.
            // If you add a "createdAt" .indexOn rule you can move sorting to the query.
            var allReports = await _firestore.GetCollectionAsync("emergency_reports");

            if (allReports.Count == 0)
            {
                ShowNoReports();
                UpdateReportCountLabels(0);
                return;
            }

            // Sort newest-first. Reports have no "createdAt"; their docId is
            // "report_yyyyMMddHHmmssfff", which sorts chronologically.
            var sorted = allReports
                .OrderByDescending(r =>
                    r.TryGetValue("docId", out var id) ? id?.ToString() ?? "" : "",
                    StringComparer.Ordinal)
                .ToList();

            // Update the count labels with the TOTAL count of all reports.
            UpdateReportCountLabels(sorted.Count);

            // Take only the most recent N for the UI card.
            var recent = sorted.Take(MaxRecentReports).ToList();

            // Pre-fetch user docs for all unique submitters in a single pass.
            var submitterUids = recent
                .Select(r => r.TryGetValue("submittedBy", out var uid) ? uid?.ToString() ?? "" : "")
                .Where(u => !string.IsNullOrEmpty(u))
                .Distinct()
                .ToList();

            var userDocs = new Dictionary<string, Dictionary<string, object>?>();
            foreach (var uid in submitterUids)
            {
                userDocs[uid] = await _firestore.GetDocumentAsync("users", uid);
            }

            // Build UI rows on the main thread.
            RecentReportsStack.Children.Clear();
            NoReportsLabel.IsVisible = false;

            foreach (var report in recent)
            {
                var row = BuildReportRow(report, userDocs);
                RecentReportsStack.Children.Add(row);
            }

            if (RecentReportsStack.Children.Count == 0)
                ShowNoReports();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HomePageAdmin] LoadRecentReports error: {ex.Message}");
            ShowNoReports();
        }
    }

    // ── Build a single report row matching the existing card style ────────
    private static View BuildReportRow(
        Dictionary<string, object> report,
        Dictionary<string, Dictionary<string, object>?> userDocs)
    {
        // Resolve submitter info from the pre-fetched user docs.
        var submitterUid = report.TryGetValue("submittedBy", out var u) ? u?.ToString() ?? "" : "";
        userDocs.TryGetValue(submitterUid, out var userDoc);

        var name = userDoc != null && userDoc.TryGetValue("name", out var n) ? n?.ToString() ?? "" : "";
        var studentId = userDoc != null && userDoc.TryGetValue("studentID", out var s) ? s?.ToString() ?? "" : "";

        // Fall back to UID suffix when the user doc has no name yet.
        if (string.IsNullOrWhiteSpace(name))
            name = submitterUid.Length >= 4 ? $"User …{submitterUid[^4..]}" : "Unknown";

        if (string.IsNullOrWhiteSpace(studentId))
            studentId = "—";

        // Status → colour + badge text.
        var rawStatus = report.TryGetValue("status", out var st) ? st?.ToString()?.ToLower() ?? "" : "";
        var (statusText, statusColor) = rawStatus switch
        {
            "resolved" or "responded" => ("RESOLVED", Color.FromArgb("#2E7D32")),   // dark green
            "pending" or "new" => ("PENDING", Color.FromArgb("#E65100")),   // orange
            "urgent" => ("URGENT", Color.FromArgb("#B71C1C")),   // deep red
            _ => ("SUBMITTED", Color.FromArgb("#1565C0")),   // blue
        };

        var dateStr = report.TryGetValue("timestamp", out var dt) ? dt?.ToString() ?? "" : "";

        // ── Outer card border (matches existing style) ────────────────────
        var border = new Border
        {
            Background = Colors.White,
            Stroke = new SolidColorBrush(Color.FromArgb("#E0E0E0")),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(10),
        };

        // ── Scrollable inner row ──────────────────────────────────────────
        var hStack = new HorizontalStackLayout { Spacing = 10 };

        hStack.Children.Add(new Image
        {
            Source = "alert.png",
            HeightRequest = 18,
            WidthRequest = 18,
            VerticalOptions = LayoutOptions.Center,
        });

        // Name + ID text
        var nameLabel = new Label
        {
            FontSize = 12,
            WidthRequest = 220,
            VerticalOptions = LayoutOptions.Center,
        };
        var fs = new FormattedString();
        fs.Spans.Add(new Span { Text = $"{name} ", FontAttributes = FontAttributes.Bold });
        fs.Spans.Add(new Span { Text = $"({studentId})  ", TextColor = Colors.Gray });
        fs.Spans.Add(new Span { Text = "Report submitted", TextColor = Colors.Gray });
        if (!string.IsNullOrWhiteSpace(dateStr))
            fs.Spans.Add(new Span { Text = $"  {dateStr}", TextColor = Colors.Gray, FontSize = 10 });
        nameLabel.FormattedText = fs;

        hStack.Children.Add(nameLabel);

        // Status badge
        var badge = new Border
        {
            BackgroundColor = statusColor,
            Padding = new Thickness(8, 3),
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            VerticalOptions = LayoutOptions.Center,
        };
        badge.Content = new Label
        {
            Text = statusText,
            FontSize = 10,
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
        };
        hStack.Children.Add(badge);

        var scroll = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = hStack,
        };

        border.Content = scroll;
        return border;
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    private void ShowNoReports()
    {
        RecentReportsStack.Children.Clear();
        NoReportsLabel.IsVisible = true;
    }

    private void UpdateReportCountLabels(int count)
    {
        var text = count.ToString();
        HeaderReportCountLabel.Text = text;
        ReportHistoryCountLabel.Text = text;
    }
}