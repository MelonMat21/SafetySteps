using SAFETY_STEPS.FireBase;

namespace SAFETY_STEPS;

public partial class HistoryPage : ContentPage
{
    private double _savedScrollY = 0;

    // Shared singleton — a new FirestoreService per call would open a new HttpClient each time.
    private static FirestoreService Firestore =>
        IPlatformApplication.Current!.Services.GetRequiredService<FirestoreService>();

    // Cancels every card's status poll when the page goes away.
    private CancellationTokenSource? _pollCts;

    public HistoryPage()
    {
        InitializeComponent();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _pollCts?.Cancel();
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

    // ── Load & Render ─────────────────────────────────────────

    private async Task LoadReportsAsync()
    {
        LoadingIndicator.IsVisible = true;
        LoadingIndicator.IsRunning = true;
        EmptyState.IsVisible = false;
        ReportsContainer.Children.Clear();

        _pollCts?.Cancel();
        _pollCts = new CancellationTokenSource();

        // Query only this user's reports
        string uid = App.AuthService.LocalId ?? UserSession.Uid;
        List<Dictionary<string, object>> reports;
        try
        {
            reports = await Firestore.QueryCollectionAsync(
                "emergency_reports", ("submittedBy", uid));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HistoryPage] Load failed: {ex.Message}");
            reports = new();
        }

        LoadingIndicator.IsVisible = false;
        LoadingIndicator.IsRunning = false;

        if (reports == null || reports.Count == 0)
        {
            EmptyState.IsVisible = true;
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
        string incidentType = Get("incidentType");
        string incidentDetail = Get("incidentDetail");
        string location = Get("locationDisplay");
        string contact = Get("contactNumber");

        string incidentDisplay = !string.IsNullOrWhiteSpace(incidentDetail)
            ? $"{incidentType} — {incidentDetail}"
            : incidentType;

        bool isResponded = status == "Responded";

        // ── Status badge ─────────────────────────────────────
        var statusLabel = new Label
        {
            Text = isResponded ? "✅  Responded" : "🔴  Ongoing",
            TextColor = Colors.White,
            FontSize = 12,
            FontAttributes = FontAttributes.Bold
        };

        var statusBadge = new Frame
        {
            BackgroundColor = isResponded ? Color.FromArgb("#388E3C") : Color.FromArgb("#D90000"),
            Padding = new Thickness(12, 4),
            CornerRadius = 6,
            HasShadow = false,
            HorizontalOptions = LayoutOptions.Start,
            Content = statusLabel
        };

        // ── Timestamp ────────────────────────────────────────
        var tsLabel = new Label
        {
            Text = timestamp,
            FontSize = 11,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
            TextColor = Colors.Gray
        };

        var headerGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star }
            }
        };
        Grid.SetColumn(statusBadge, 0);
        Grid.SetColumn(tsLabel, 1);
        headerGrid.Children.Add(statusBadge);
        headerGrid.Children.Add(tsLabel);

        // ── Info rows ────────────────────────────────────────
        View InfoRow(string icon, string labelText, string value, bool useImage = false) =>
            new HorizontalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    useImage
                        ? (View)new Image
                          {
                              Source          = icon,
                              HeightRequest   = 18,
                              WidthRequest    = 18,
                              VerticalOptions = LayoutOptions.Start
                          }
                        : (View)new Label
                          {
                              Text            = icon,
                              FontSize        = 15,
                              VerticalOptions = LayoutOptions.Start
                          },
                    new VerticalStackLayout
                    {
                        Children =
                        {
                            new Label { Text = labelText, FontSize = 11, TextColor = Colors.Gray },
                            new Label
                            {
                                Text           = string.IsNullOrWhiteSpace(value) ? "—" : value,
                                FontSize       = 13,
                                FontAttributes = FontAttributes.Bold,
                                TextColor      = Colors.Black,
                                LineBreakMode  = LineBreakMode.WordWrap
                            }
                        }
                    }
                }
            };

        // Incident row in red
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
                        new Label { Text = "Incident", FontSize = 11, TextColor = Colors.Gray },
                        new Label
                        {
                            Text           = string.IsNullOrWhiteSpace(incidentDisplay) ? "—" : incidentDisplay,
                            FontSize       = 13,
                            FontAttributes = FontAttributes.Bold,
                            TextColor      = Color.FromArgb("#C62828"),
                            LineBreakMode  = LineBreakMode.WordWrap
                        }
                    }
                }
            }
        };

        // ── "Awaiting response" hint (hidden once responded) ─
        var awaitingLabel = new Label
        {
            Text = "⏳  Awaiting admin response…",
            FontSize = 12,
            TextColor = Color.FromArgb("#E65100"),
            IsVisible = !isResponded,
            HorizontalOptions = LayoutOptions.Start
        };

        // ── Responded banner (hidden until responded) ─────────
        var respondedBanner = new Frame
        {
            IsVisible = isResponded,
            BackgroundColor = Color.FromArgb("#E8F5E9"),
            BorderColor = Color.FromArgb("#A5D6A7"),
            CornerRadius = 8,
            Padding = new Thickness(12, 8),
            HasShadow = false,
            HorizontalOptions = LayoutOptions.Fill,
            Content = new Label
            {
                Text = "✅  The admin has responded to your report.",
                FontSize = 12,
                TextColor = Color.FromArgb("#2E7D32"),
                FontAttributes = FontAttributes.Bold
            }
        };

        // ── Assemble card ─────────────────────────────────────
        var cardContent = new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                headerGrid,
                new BoxView { HeightRequest = 1, BackgroundColor = Color.FromArgb("#F0F0F0") },
                incidentRow,
                InfoRow("location2.png", "Location",       location, useImage: true),
                InfoRow("📞",            "Contact",        contact),
                awaitingLabel,
                respondedBanner
            }
        };

        var card = new Frame
        {
            Margin = new Thickness(16, 10, 16, 0),
            BorderColor = isResponded ? Color.FromArgb("#A5D6A7") : Color.FromArgb("#FFCDD2"),
            CornerRadius = 12,
            BackgroundColor = Colors.White,
            Padding = new Thickness(14),
            HasShadow = true,
            Content = cardContent
        };

        // ── Poll for status change if still ongoing ───────────
        if (!isResponded)
            StartPollingStatus(documentId, statusLabel, statusBadge,
                               awaitingLabel, respondedBanner, card);

        return card;
    }

    // ── Status polling (lightweight, per-card) ────────────────

    private void StartPollingStatus(
        string documentId,
        Label statusLabel,
        Frame statusBadge,
        Label awaitingLabel,
        Frame respondedBanner,
        Frame card)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(
            _pollCts?.Token ?? CancellationToken.None);
        var token = cts.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try { await Task.Delay(15_000, token); }
                catch (OperationCanceledException) { break; }

                if (token.IsCancellationRequested) break;

                try
                {
                    var doc = await Firestore.GetDocumentAsync("emergency_reports", documentId);
                    if (doc == null) continue;

                    string liveStatus = doc.TryGetValue("status", out var sv)
                        ? sv?.ToString() ?? "" : "";

                    if (liveStatus != "Responded") continue;

                    // Flip the card to Responded on the main thread
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        statusLabel.Text = "✅  Responded";
                        statusBadge.BackgroundColor = Color.FromArgb("#388E3C");
                        awaitingLabel.IsVisible = false;
                        respondedBanner.IsVisible = true;
                        card.BorderColor = Color.FromArgb("#A5D6A7");
                    });

                    cts.Cancel(); // stop polling — we're done
                }
                catch (OperationCanceledException) { break; }
                catch { /* transient — retry */ }
            }
        }, token);
    }
}