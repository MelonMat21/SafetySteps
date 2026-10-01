using System.Globalization;
using SAFETY_STEPS.FireBase;

namespace SAFETY_STEPS;

public partial class ReportDetailSheet : ContentPage
{
    // ── State ─────────────────────────────────────────────────
    private readonly Dictionary<string, object> _data;
    private readonly string _collectionId;
    private readonly string _documentId;

    private double _lastLat = 0;
    private double _lastLng = 0;
    private bool _mapReady = false;
    private bool _isResponded = false;

    // ── Photo overlay state ───────────────────────────────────
    private double _photoScale = 1.0;
    private double _pinchStartScale = 1.0;
    private double _translateX = 0;
    private double _translateY = 0;
    private double _panStartX = 0;
    private double _panStartY = 0;

    private CancellationTokenSource? _trackingCts;

    private const int TrackingIntervalMs = 10_000;
    private const int TrackingMaxIntervalMs = 120_000;
    private const int TrackingBackoffMultiple = 2;

    // ── Services ─────────────────────────────────────────────
    // Shared singleton — a new FirestoreService per call would open a new HttpClient each time.
    private static FirestoreService Firestore =>
        IPlatformApplication.Current!.Services.GetRequiredService<FirestoreService>();

    // ── Constructor ───────────────────────────────────────────
    public ReportDetailSheet(Dictionary<string, object> data, string collectionId, string documentId)
    {
        InitializeComponent();
        _data = data;
        _collectionId = collectionId;
        _documentId = documentId;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        try { PopulateDetails(); }
        catch (Exception ex) { Console.WriteLine($"[ReportDetailSheet] OnAppearing error: {ex.Message}"); }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        StopTracking();
        MapWebView.Navigated -= OnMapReady;
    }

    // ── Button Event Handlers ─────────────────────────────────

    private async void Close_Clicked(object sender, EventArgs e)
        => await Navigation.PopModalAsync(animated: true);

    private async void Respond_Clicked(object sender, EventArgs e)
    {
        bool confirmed = await DisplayAlert(
            "Confirm", "Mark this report as Responded?", "Yes", "Cancel");
        if (!confirmed) return;

        try
        {
            RespondButton.IsEnabled = false;
            RespondButton.Text = "Updating…";

            bool saved = await Firestore.PatchFieldsAsync(
                _collectionId, _documentId,
                new Dictionary<string, object> { ["status"] = "Responded" });

            if (!saved)
            {
                RespondButton.IsEnabled = true;
                RespondButton.Text = "✅  Mark Responded";
                await DisplayAlert("Error",
                    "Could not update the report. Please check your connection and try again.", "OK");
                return;
            }

            StatusLabel.Text = "Responded";
            StatusBadge.BackgroundColor = Color.FromArgb("#388E3C");
            RespondButton.Text = "✅  Mark Responded";
            LiveIndicator.IsVisible = false;
            _isResponded = true;

            StopTracking();
            await DisplayAlert("Done", "Report marked as Responded.", "OK");
        }
        catch (Exception ex)
        {
            RespondButton.IsEnabled = true;
            RespondButton.Text = "✅  Mark Responded";
            await DisplayAlert("Error", $"Could not update report:\n{ex.Message}", "OK");
        }
    }

    private async void Delete_Clicked(object sender, EventArgs e)
    {
        bool confirmed = await DisplayAlert(
            "Delete Report",
            "Are you sure you want to permanently delete this report? This cannot be undone.",
            "Delete", "Cancel");
        if (!confirmed) return;

        try
        {
            bool deleted = await Firestore.DeleteDocumentAsync(_collectionId, _documentId);
            if (!deleted)
            {
                await DisplayAlert("Error",
                    "Could not delete the report. Please check your connection and try again.", "OK");
                return;
            }

            await Navigation.PopModalAsync(animated: true);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Could not delete report:\n{ex.Message}", "OK");
        }
    }

    // ── Populate Info Panel ───────────────────────────────────

    private void PopulateDetails()
    {
        string Get(string key) => _data.TryGetValue(key, out var v) ? v?.ToString() ?? "" : "";

        string status = Get("status");
        string name = Get("reporterName");
        string studentNumber = Get("studentNumber");
        string incidentType = Get("incidentType");
        string incidentDetail = Get("incidentDetail");
        string location = Get("locationDisplay");
        string contact = Get("contactNumber");
        string timestamp = Get("timestamp");
        string gpsAddr = Get("gpsAddress");
        string latStr = Get("latitude");
        string lngStr = Get("longitude");

        NameLabel.Text = string.IsNullOrWhiteSpace(name) ? "Anonymous" : name;

        // Student ID Number
        StudentNumberLabel.Text = string.IsNullOrWhiteSpace(studentNumber)
            ? "Not provided"
            : studentNumber;

        // Incident type — show detail on a second line if provided
        if (!string.IsNullOrWhiteSpace(incidentDetail))
            IncidentTypeLabel.Text = $"{incidentType}\n{incidentDetail}";
        else
            IncidentTypeLabel.Text = string.IsNullOrWhiteSpace(incidentType)
                ? "Not specified"
                : incidentType;

        LocationLabel.Text = location;
        ContactLabel.Text = contact;
        TimestampLabel.Text = timestamp;
        GpsLabel.Text = string.IsNullOrWhiteSpace(gpsAddr) ? "Not provided" : gpsAddr;

        StatusLabel.Text = status;
        StatusBadge.BackgroundColor = status == "Responded"
            ? Color.FromArgb("#388E3C")
            : Color.FromArgb("#D90000");

        RespondButton.IsEnabled = status != "Responded";

        bool hasGps =
        double.TryParse(latStr, NumberStyles.Any, CultureInfo.InvariantCulture, out _lastLat) &&
        double.TryParse(lngStr, NumberStyles.Any, CultureInfo.InvariantCulture, out _lastLng) &&
        _lastLat != 0 &&
        _lastLng != 0;

        bool isResponded = status == "Responded";

        // ── Incident Photo ────────────────────────────────────
        string photoB64 = Get("photoBase64");
        if (!string.IsNullOrWhiteSpace(photoB64))
        {
            try
            {
                byte[] imageBytes = Convert.FromBase64String(photoB64);
                IncidentPhotoImage.Source = ImageSource.FromStream(
                    () => new MemoryStream(imageBytes));
                PhotoBorder.IsVisible = true;
                NoPhotoLabel.IsVisible = false;
                PhotoTapHint.IsVisible = true;
                // Mirror source to expanded view
                ExpandedPhotoImage.Source = ImageSource.FromStream(
                    () => new MemoryStream(imageBytes));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ReportDetailSheet] Photo load error: {ex.Message}");
            }
        }

        if (hasGps)
        {
            LiveIndicator.IsVisible = !isResponded;
            LoadLeafletMap(_lastLat, _lastLng, isResponded);
        }
        else
        {
            LoadNoLocationPage();
        }
    }

    // ── Live-Tracking ─────────────────────────────────────────

    private void StartLiveTracking()
    {
        StopTracking();
        _trackingCts = new CancellationTokenSource();
        var token = _trackingCts.Token;

        _ = Task.Run(async () =>
        {
            int currentInterval = TrackingIntervalMs;

            while (!token.IsCancellationRequested)
            {
                try { await Task.Delay(currentInterval, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }

                if (token.IsCancellationRequested) break;

                try
                {
                    var doc = await Firestore.GetDocumentAsync(_collectionId, _documentId);
                    currentInterval = TrackingIntervalMs;

                    if (doc == null) continue;

                    string liveStatus = doc.TryGetValue("status", out var sv) ? sv?.ToString() ?? "" : "";
                    if (liveStatus == "Responded")
                    {
                        await MainThread.InvokeOnMainThreadAsync(() =>
                        {
                            LiveIndicator.IsVisible = false;
                            StatusLabel.Text = "Responded";
                            StatusBadge.BackgroundColor = Color.FromArgb("#388E3C");
                            RespondButton.IsEnabled = false;
                        });
                        break;
                    }

                    string latStr = doc.TryGetValue("latitude", out var la) ? la?.ToString() ?? "" : "";
                    string lngStr = doc.TryGetValue("longitude", out var lo) ? lo?.ToString() ?? "" : "";

                    if (!double.TryParse(latStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double newLat)
                     || !double.TryParse(lngStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double newLng))
                        continue;

                    if (Math.Abs(newLat - _lastLat) < 0.000001
                     && Math.Abs(newLng - _lastLng) < 0.000001)
                        continue;

                    _lastLat = newLat;
                    _lastLng = newLng;

                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        if (!_mapReady) return;
                        string latS = newLat.ToString("F6", CultureInfo.InvariantCulture);
                        string lngS = newLng.ToString("F6", CultureInfo.InvariantCulture);
                        await MapWebView.EvaluateJavaScriptAsync($"updateMarker({latS},{lngS});");
                    });
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) when (ex.Message.Contains("429") || ex.Message.Contains("RESOURCE_EXHAUSTED"))
                {
                    currentInterval = Math.Min(currentInterval * TrackingBackoffMultiple, TrackingMaxIntervalMs);
                }
                catch { /* transient — retry next interval */ }
            }
        }, token);
    }

    private void StopTracking()
    {
        if (_trackingCts is null) return;
        _trackingCts.Cancel();
        _trackingCts.Dispose();
        _trackingCts = null;
        _mapReady = false;
    }

    // ── Map Loading ───────────────────────────────────────────

    private void LoadLeafletMap(double lat, double lng, bool isResponded = false)
    {
        _isResponded = isResponded;

        MapContainer.IsVisible = true;

        // Normal layout: info panel sizes to content, map fills the rest
        RootGrid.RowDefinitions[1].Height = GridLength.Auto;
        RootGrid.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Star);

        MapWebView.Navigated -= OnMapReady;
        MapWebView.Navigated += OnMapReady;

        MapWebView.Source = new HtmlWebViewSource
        {
            Html = BuildMapHtml(lat, lng)
        };
    }

    private void LoadNoLocationPage()
    {
        // Hide the map container
        MapContainer.IsVisible = false;

        // Collapse the map row
        RootGrid.RowDefinitions[2].Height = new GridLength(0, GridUnitType.Absolute);

        // Expand the report content to fill the page
        RootGrid.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);

        // Remove WebView content
        MapWebView.Source = null;
        MapWebView.Navigated -= OnMapReady;
    }

    // ── Map Ready Callback ────────────────────────────────────

    private void OnMapReady(object? sender, WebNavigatedEventArgs e)
    {
        if (e.Result != WebNavigationResult.Success)
        {
            Console.WriteLine($"[ReportDetailSheet] Map navigation failed: {e.Result}");
            return;
        }
        _mapReady = true;
        if (!_isResponded) StartLiveTracking();
    }

    // ── Leaflet HTML ──────────────────────────────────────────
    private static string BuildMapHtml(double lat, double lng)
    {
        string latS = lat.ToString("F6", CultureInfo.InvariantCulture);
        string lngS = lng.ToString("F6", CultureInfo.InvariantCulture);

        return $$"""
        <!DOCTYPE html>
        <html>
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width,initial-scale=1.0,user-scalable=no">
          <link rel="stylesheet"
                href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css"
                crossorigin="anonymous"/>
          <style>
            * { box-sizing:border-box; margin:0; padding:0; }
            html, body { width:100vw; height:100vh; overflow:hidden; }
            #map { width:100%; height:100%; }

            @keyframes pulse {
              0%   { transform:scale(1);   opacity:0.7; }
              70%  { transform:scale(2.8); opacity:0;   }
              100% { transform:scale(1);   opacity:0;   }
            }
            .pulse-wrapper { position:relative; width:22px; height:22px; }
            .pulse-ring {
              position:absolute; inset:0; border-radius:50%;
              background:rgba(217,0,0,0.35);
              animation:pulse 2s ease-out infinite;
            }
            .dot-inner {
              position:absolute; width:14px; height:14px;
              background:#D90000; border:3px solid #fff; border-radius:50%;
              box-shadow:0 1px 5px rgba(0,0,0,0.45);
              top:50%; left:50%; transform:translate(-50%,-50%);
            }
          </style>
        </head>
        <body>
          <div id="map"></div>
          <script src="https://unpkg.com/leaflet@1.9.4/dist/leaflet.js"
                  crossorigin="anonymous"></script>
          <script>
            var map = L.map('map', { zoomControl:true, attributionControl:false })
                       .setView([{{latS}}, {{lngS}}], 17);

            L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
              maxZoom: 19,
              crossOrigin: 'anonymous'
            }).addTo(map);

            var pulseIcon = L.divIcon({
              className: '',
              html: '<div class="pulse-wrapper">'
                  + '<div class="pulse-ring"></div>'
                  + '<div class="dot-inner"></div>'
                  + '</div>',
              iconSize:   [22, 22],
              iconAnchor: [11, 11]
            });

            var marker = L.marker([{{latS}}, {{lngS}}], { icon: pulseIcon }).addTo(map);
            marker.bindPopup('<b>Emergency Location</b>').openPopup();

            setTimeout(function() { map.invalidateSize(); }, 300);

            function updateMarker(newLat, newLng) {
              map.invalidateSize();
              marker.setLatLng([newLat, newLng]);
              map.panTo([newLat, newLng]);
            }
          </script>
        </body>
        </html>
        """;
    }

    // ── Photo Overlay Handlers ────────────────────────────────

    private async void OnPhotoTapped(object? sender, TappedEventArgs e)
    {
        _photoScale = 1.0;
        _translateX = 0;
        _translateY = 0;
        ExpandedPhotoImage.Scale = 1.0;
        ExpandedPhotoImage.TranslationX = 0;
        ExpandedPhotoImage.TranslationY = 0;
        PhotoOverlay.Opacity = 0;
        PhotoOverlay.IsVisible = true;
        await PhotoOverlay.FadeTo(1, 200);
    }

    private async void OnOverlayTapped(object? sender, TappedEventArgs e)
    {
        await PhotoOverlay.FadeTo(0, 180);
        PhotoOverlay.IsVisible = false;
        _photoScale = 1.0;
        _translateX = 0;
        _translateY = 0;
        ExpandedPhotoImage.Scale = 1.0;
        ExpandedPhotoImage.TranslationX = 0;
        ExpandedPhotoImage.TranslationY = 0;
    }

    private void OnPhotoPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        switch (e.Status)
        {
            case GestureStatus.Started:
                _pinchStartScale = _photoScale;
                break;

            case GestureStatus.Running:
                _photoScale = Math.Clamp(_pinchStartScale * e.Scale, 1.0, 5.0);
                ExpandedPhotoImage.Scale = _photoScale;
                break;

            case GestureStatus.Completed:
                if (_photoScale < 1.0)
                {
                    _photoScale = 1.0;
                    ExpandedPhotoImage.ScaleTo(1.0, 150);
                }
                break;
        }
    }

    private void OnPhotoPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        // Only allow panning when zoomed in
        if (_photoScale <= 1.0) return;

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panStartX = _translateX;
                _panStartY = _translateY;
                break;

            case GestureStatus.Running:
                // Max translation is half the overflow in each axis
                double screenW = DeviceDisplay.MainDisplayInfo.Width
                                 / DeviceDisplay.MainDisplayInfo.Density;
                double screenH = DeviceDisplay.MainDisplayInfo.Height
                                 / DeviceDisplay.MainDisplayInfo.Density;
                double maxX = screenW * (_photoScale - 1) / 2;
                double maxY = screenH * (_photoScale - 1) / 2;

                _translateX = Math.Clamp(_panStartX + e.TotalX, -maxX, maxX);
                _translateY = Math.Clamp(_panStartY + e.TotalY, -maxY, maxY);

                ExpandedPhotoImage.TranslationX = _translateX;
                ExpandedPhotoImage.TranslationY = _translateY;
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                // Keep position as-is; _translateX/Y already hold the final value
                break;
        }
    }

    private async void OnPhotoDoubleTapped(object? sender, TappedEventArgs e)
    {
        // Double-tap toggles between 1× (reset) and 2.5×
        _photoScale = _photoScale > 1.05 ? 1.0 : 2.5;

        if (_photoScale <= 1.0)
        {
            _translateX = 0;
            _translateY = 0;
            await Task.WhenAll(
                ExpandedPhotoImage.ScaleTo(1.0, 200, Easing.CubicOut),
                ExpandedPhotoImage.TranslateTo(0, 0, 200, Easing.CubicOut));
        }
        else
        {
            await ExpandedPhotoImage.ScaleTo(_photoScale, 200, Easing.CubicOut);
        }
    }
}