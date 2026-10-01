using SAFETY_STEPS.FireBase;

namespace SAFETY_STEPS;

public partial class ReportHistoryAdmin : ContentPage
{
    private double _savedScrollY = 0;

    // ── Active tab: "Ongoing" (Pending) or "Recent" (Responded) ──
    private string _activeTab = "Ongoing";

    // Shared singleton — a new FirestoreService per call would open a new HttpClient each time.
    private static FirestoreService Firestore =>
        IPlatformApplication.Current!.Services.GetRequiredService<FirestoreService>();

    public ReportHistoryAdmin()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadReportsAsync();

        if (_savedScrollY > 0)
        {
            await Task.Delay(80);
            await ReportsScrollView.ScrollToAsync(0, _savedScrollY, false);
        }
    }

    // ── Tab Switch Handlers ───────────────────────────────────

    private async void OnOngoingTabTapped(object sender, TappedEventArgs e)
    {
        if (_activeTab == "Ongoing") return;
        _activeTab = "Ongoing";
        UpdateTabVisuals();
        _savedScrollY = 0;
        await LoadReportsAsync();
    }

    private async void OnRecentTabTapped(object sender, TappedEventArgs e)
    {
        if (_activeTab == "Recent") return;
        _activeTab = "Recent";
        UpdateTabVisuals();
        _savedScrollY = 0;
        await LoadReportsAsync();
    }

    /// <summary>
    /// Updates the visual state (label weight, colour, indicator bar) for both tabs.
    /// </summary>
    private void UpdateTabVisuals()
    {
        bool ongoingActive = _activeTab == "Ongoing";

        // Ongoing tab
        TabOngoingLabel.FontFamily = ongoingActive ? "PoppinsBold" : "PoppinsRegular";
        TabOngoingLabel.TextColor = ongoingActive ? Color.FromArgb("#ab0404") : Color.FromArgb("#9E9E9E");
        TabOngoingIndicator.BackgroundColor = ongoingActive ? Color.FromArgb("#ab0404") : Colors.Transparent;

        // Recent tab
        TabRecentLabel.FontFamily = ongoingActive ? "PoppinsRegular" : "PoppinsBold";
        TabRecentLabel.TextColor = ongoingActive ? Color.FromArgb("#9E9E9E") : Color.FromArgb("#ab0404");
        TabRecentIndicator.BackgroundColor = ongoingActive ? Colors.Transparent : Color.FromArgb("#ab0404");
    }

    // ── Load & Render Reports ─────────────────────────────────

    private async Task LoadReportsAsync()
    {
        LoadingIndicator.IsVisible = true;
        LoadingIndicator.IsRunning = true;
        EmptyLabel.IsVisible = false;
        ReportsContainer.Children.Clear();

        var allReports = await Firestore.GetCollectionAsync("emergency_reports");

        // ── Filter by active tab ──────────────────────────────
        // Ongoing = any status that is NOT "Responded" (e.g. "Pending", "")
        // Recent  = status == "Responded"
        List<Dictionary<string, object>> reports;

        if (_activeTab == "Ongoing")
        {
            reports = allReports
                .Where(r =>
                {
                    var status = r.TryGetValue("status", out var s) ? s?.ToString() ?? "" : "";
                    return status != "Responded";
                })
                .ToList();
        }
        else
        {
            reports = allReports
                .Where(r =>
                {
                    var status = r.TryGetValue("status", out var s) ? s?.ToString() ?? "" : "";
                    return status == "Responded";
                })
                .ToList();
        }

        LoadingIndicator.IsVisible = false;
        LoadingIndicator.IsRunning = false;

        // Update tab labels with counts
        int ongoingCount = allReports.Count(r =>
        {
            var status = r.TryGetValue("status", out var s) ? s?.ToString() ?? "" : "";
            return status != "Responded";
        });
        int recentCount = allReports.Count(r =>
        {
            var status = r.TryGetValue("status", out var s) ? s?.ToString() ?? "" : "";
            return status == "Responded";
        });

        TabOngoingLabel.Text = ongoingCount > 0 ? $"Ongoing ({ongoingCount})" : "Ongoing";
        TabRecentLabel.Text = recentCount > 0 ? $"Recent ({recentCount})" : "Recent";

        if (reports.Count == 0)
        {
            EmptyLabel.Text = _activeTab == "Ongoing"
                ? "No ongoing reports."
                : "No responded reports yet.";
            EmptyLabel.IsVisible = true;
            return;
        }

        // Sort newest-first
        // docId is "report_yyyyMMddHHmmssfff", which sorts chronologically.
        // (The "timestamp" field is display text like "Mar 05, 2026" and does not.)
        reports.Sort((a, b) =>
        {
            var ta = a.TryGetValue("docId", out var va) ? va?.ToString() ?? "" : "";
            var tb = b.TryGetValue("docId", out var vb) ? vb?.ToString() ?? "" : "";
            return string.Compare(tb, ta, StringComparison.Ordinal);
        });

        foreach (var report in reports)
        {
            string docId = report.TryGetValue("docId", out var id) ? id?.ToString() ?? "" : "";
            ReportsContainer.Children.Add(BuildReportCard(report, docId));
        }
    }

    // ── Build a Single Report Card ────────────────────────────

    private View BuildReportCard(Dictionary<string, object> data, string documentId)
    {
        string Get(string key) =>
            data.TryGetValue(key, out var v) ? v?.ToString() ?? "" : "";

        string status = Get("status");
        string timestamp = Get("timestamp");
        string name = Get("reporterName");
        string studentNumber = Get("studentNumber");
        string incidentType = Get("incidentType");
        string incidentDetail = Get("incidentDetail");
        string location = Get("locationDisplay");
        string contact = Get("contactNumber");
        bool hasGps = !string.IsNullOrWhiteSpace(Get("latitude"));

        string incidentDisplay = !string.IsNullOrWhiteSpace(incidentDetail)
            ? $"{incidentType} — {incidentDetail}"
            : incidentType;

        Color badgeColor = status == "Responded"
            ? Color.FromArgb("#388E3C")
            : Color.FromArgb("#D90000");

        // ── Status + timestamp row ──────────────────────────
        var headerGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star }
            }
        };

        var statusBadge = new Frame
        {
            BackgroundColor = badgeColor,
            Padding = new Thickness(10, 3),
            CornerRadius = 5,
            HasShadow = false,
            HorizontalOptions = LayoutOptions.Start,
            Content = new Label
            {
                Text = status,
                TextColor = Colors.White,
                FontSize = 11,
                FontAttributes = FontAttributes.Bold
            }
        };
        Grid.SetColumn(statusBadge, 0);

        var tsLabel = new Label
        {
            Text = timestamp,
            FontSize = 11,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
            TextColor = Colors.Gray
        };
        Grid.SetColumn(tsLabel, 1);

        headerGrid.Children.Add(statusBadge);
        headerGrid.Children.Add(tsLabel);

        // ── Info rows ───────────────────────────────────────
        View InfoRow(string iconSource, string label, string value, bool useImage = true) =>
            new HorizontalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    useImage
                        ? (View)new Image
                          {
                              Source          = iconSource,
                              HeightRequest   = 18,
                              WidthRequest    = 18,
                              VerticalOptions = LayoutOptions.Start
                          }
                        : (View)new Label
                          {
                              Text            = iconSource,
                              FontSize        = 15,
                              VerticalOptions = LayoutOptions.Start
                          },
                    new VerticalStackLayout
                    {
                        Children =
                        {
                            new Label { Text = label, FontSize = 11, TextColor = Colors.Gray },
                            new Label
                            {
                                Text           = string.IsNullOrWhiteSpace(value) ? "—" : value,
                                FontSize       = 12,
                                FontAttributes = FontAttributes.Bold,
                                TextColor      = Colors.Black,
                                LineBreakMode  = LineBreakMode.WordWrap
                            }
                        }
                    }
                }
            };

        // ── Incident type row (red text) ─────────────────────
        var incidentRow = new HorizontalStackLayout
        {
            Spacing = 10,
            Children =
            {
                new Label { Text = "⚠️", FontSize = 15, VerticalOptions = LayoutOptions.Start },
                new VerticalStackLayout
                {
                    Children =
                    {
                        new Label { Text = "Incident Type", FontSize = 11, TextColor = Colors.Gray },
                        new Label
                        {
                            Text           = string.IsNullOrWhiteSpace(incidentDisplay) ? "—" : incidentDisplay,
                            FontSize       = 12,
                            FontAttributes = FontAttributes.Bold,
                            TextColor      = Color.FromArgb("#C62828"),
                            LineBreakMode  = LineBreakMode.WordWrap
                        }
                    }
                }
            }
        };

        // ── Live-location pill (only on Ongoing tab) ─────────
        var livePill = new Frame
        {
            IsVisible = hasGps && status != "Responded",
            BackgroundColor = Color.FromArgb("#FFF3E0"),
            BorderColor = Color.FromArgb("#FF9800"),
            CornerRadius = 6,
            Padding = new Thickness(8, 3),
            HorizontalOptions = LayoutOptions.Start,
            HasShadow = false,
            Content = new Label
            {
                Text = "📍 Live Location",
                FontSize = 11,
                TextColor = Color.FromArgb("#E65100")
            }
        };

        // ── "View Details" button ────────────────────────────
        var viewButton = new Button
        {
            Text = "View Details & Map →",
            BackgroundColor = Color.FromArgb("#1565C0"),
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            FontSize = 13,
            CornerRadius = 8,
            HeightRequest = 42,
            HorizontalOptions = LayoutOptions.Fill,
            Margin = new Thickness(0, 4, 0, 0)
        };

        var capturedData = data;
        var capturedId = documentId;

        viewButton.Clicked += async (_, _) =>
        {
            _savedScrollY = ReportsScrollView.ScrollY;
            var sheet = new ReportDetailSheet(capturedData, "emergency_reports", capturedId);
            await Navigation.PushModalAsync(sheet, animated: true);
        };

        // ── Assemble card ────────────────────────────────────
        var cardContent = new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                headerGrid,
                new BoxView { HeightRequest = 1, BackgroundColor = Color.FromArgb("#F0F0F0") },
                InfoRow("profile3.png",  "Victim",          string.IsNullOrWhiteSpace(name) ? "Anonymous" : name),
                InfoRow("🪪",            "Student ID Number", string.IsNullOrWhiteSpace(studentNumber) ? "—" : studentNumber, useImage: false),
                incidentRow,
                InfoRow("location2.png", "Location",        location),
                InfoRow("call2.png",     "Contact Number",  contact),
                livePill,
                viewButton
            }
        };

        return new Frame
        {
            Margin = new Thickness(16, 12, 16, 0),
            BorderColor = Color.FromArgb("#E0E0E0"),
            CornerRadius = 12,
            BackgroundColor = Colors.White,
            Padding = new Thickness(14),
            HasShadow = true,
            Content = cardContent
        };
    }
}