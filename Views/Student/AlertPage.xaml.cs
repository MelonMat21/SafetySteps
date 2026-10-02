using SAFETY_STEPS.FireBase;
using SAFETY_STEPS.Services;

namespace SAFETY_STEPS;

public partial class AlertPage : ContentPage
{
    // ── GPS State ────────────────────────────────────────────
    private double? _latitude;
    private double? _longitude;
    private string _gpsAddress = string.Empty;

    // ── Location Type State ──────────────────────────────────
    private string _locationType = string.Empty;   // "School" or "Court"

    // ── Court Position State ─────────────────────────────────
    private string _courtPosition = string.Empty;
    private Dictionary<string, (Border Chip, Label ChipLabel)> _courtChips = new();

    // ── Incident Type State ──────────────────────────────────
    private string _incidentType = string.Empty;
    private Dictionary<string, (Border Chip, Label ChipLabel)> _incidentChips = new();

    // ── Live Tracking State ──────────────────────────────────
    private CancellationTokenSource? _trackingCts;
    private string? _activeReportDocId;

    // ── Priority State ───────────────────────────────────────
    private bool _isHighPriority = false;

    // ── Photo State ──────────────────────────────────────────
    private string? _photoBase64;

    // ── Services ─────────────────────────────────────────────
    private readonly FirestoreService _firestore;

    public AlertPage(FirestoreService firestore)
    {
        InitializeComponent();
        FloorContainer.IsVisible = false;
        BuildingContainer.IsVisible = false;
        CourtPositionContainer.IsVisible = false;
        _firestore = firestore;

        _courtChips = new Dictionary<string, (Border, Label)>
        {
            ["Inside Court A"] = (ChipInsideCourtA, LabelInsideCourtA),
            ["Inside Court B"] = (ChipInsideCourtB, LabelInsideCourtB),
            ["Inside Court"] = (ChipInsideCourt, LabelInsideCourt),
            ["Behind the Court"] = (ChipBehindCourt, LabelBehindCourt),
            ["Near Entrance"] = (ChipCourtEntrance, LabelCourtEntrance),
        };

        _incidentChips = new Dictionary<string, (Border, Label)>
        {
            ["Medical Emergency"] = (ChipMedical, LabelMedical),
            ["Fire"] = (ChipFire, LabelFire),
            ["Fight / Violence"] = (ChipFight, LabelFight),
            ["Flood / Disaster"] = (ChipDisaster, LabelDisaster),
            ["Weapon Threat"] = (ChipWeapon, LabelWeapon),
            ["Other"] = (ChipOther, LabelOther),
        };
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _trackingCts?.Cancel();
        _trackingCts = null;
    }

    // ── Incident Type Selection ──────────────────────────────

    private void OnIncidentTypeSelected(object sender, TappedEventArgs e)
    {
        string selected = e.Parameter?.ToString() ?? string.Empty;
        if (string.IsNullOrEmpty(selected)) return;

        _incidentType = selected;

        foreach (var (chip, label) in _incidentChips.Values)
        {
            chip.BackgroundColor = Color.FromArgb("#F5F5F5");
            chip.Stroke = new SolidColorBrush(Color.FromArgb("#E0E0E0"));
            label.TextColor = Color.FromArgb("#424242");
            label.FontAttributes = FontAttributes.None;
        }

        if (_incidentChips.TryGetValue(selected, out var active))
        {
            active.Chip.BackgroundColor = Color.FromArgb("#FFEBEE");
            active.Chip.Stroke = new SolidColorBrush(Color.FromArgb("#C62828"));
            active.ChipLabel.TextColor = Color.FromArgb("#C62828");
            active.ChipLabel.FontAttributes = FontAttributes.Bold;
        }
    }

    // ── Location Type Buttons ────────────────────────────────

    private void school_clicked(object sender, EventArgs e)
    {
        SchoolImage.Source = "school_clicked.png";
        CourtImage.Source = "court_unclicked.png";

        // Show both building and floor pickers; hide court section
        BuildingContainer.IsVisible = true;
        FloorContainer.IsVisible = true;
        CourtPositionContainer.IsVisible = false;

        _locationType = "School";
        ResetCourtPositionChips();
        _courtPosition = string.Empty;
    }

    private void court_clicked(object sender, EventArgs e)
    {
        CourtImage.Source = "court_clicked.png";
        SchoolImage.Source = "school_unclicked.png";

        // Hide building/floor, show court position
        BuildingContainer.IsVisible = false;
        FloorContainer.IsVisible = false;
        CourtPositionContainer.IsVisible = true;

        _locationType = "Court";
    }

    // ── Court Position Selection ──────────────────────────────

    private void OnCourtPositionSelected(object sender, TappedEventArgs e)
    {
        string selected = e.Parameter?.ToString() ?? string.Empty;
        if (string.IsNullOrEmpty(selected)) return;

        _courtPosition = selected;
        ResetCourtPositionChips();

        if (_courtChips.TryGetValue(selected, out var active))
        {
            active.Chip.BackgroundColor = Color.FromArgb("#E3F2FD");
            active.Chip.Stroke = new SolidColorBrush(Color.FromArgb("#1565C0"));
            active.ChipLabel.TextColor = Color.FromArgb("#1565C0");
            active.ChipLabel.FontAttributes = FontAttributes.Bold;
        }
    }

    private void ResetCourtPositionChips()
    {
        foreach (var (chip, label) in _courtChips.Values)
        {
            chip.BackgroundColor = Color.FromArgb("#F5F5F5");
            chip.Stroke = new SolidColorBrush(Color.FromArgb("#E0E0E0"));
            label.TextColor = Color.FromArgb("#424242");
            label.FontAttributes = FontAttributes.None;
        }
    }

    // ── Share My Current Location ────────────────────────────

    private async void share_location_clicked(object sender, EventArgs e)
    {
        ShareLocationButton.IsEnabled = false;
        ShareLocationButton.Text = "⏳  Getting location…";

        try
        {
            var status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted)
            {
                await DisplayAlert("Permission Denied",
                    "Location permission is required to share your location.", "OK");
                return;
            }

            var request = new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(10));
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var location = await Geolocation.Default.GetLocationAsync(request, cts.Token);

            if (location == null)
            {
                await DisplayAlert("Location Error",
                    "Could not retrieve your location. Please try again.", "OK");
                return;
            }

            _latitude = location.Latitude;
            _longitude = location.Longitude;

            try
            {
                var placemarks = await Geocoding.Default.GetPlacemarksAsync(
                    location.Latitude, location.Longitude);
                var place = placemarks?.FirstOrDefault();

                if (place != null)
                {
                    var parts = new[]
                    {
                        place.SubThoroughfare, place.Thoroughfare,
                        place.SubLocality, place.Locality, place.AdminArea
                    }.Where(p => !string.IsNullOrWhiteSpace(p));
                    _gpsAddress = string.Join(", ", parts);
                }
                else
                {
                    _gpsAddress = $"{_latitude:F6}, {_longitude:F6}";
                }
            }
            catch
            {
                _gpsAddress = $"{_latitude:F6}, {_longitude:F6}";
            }

            if (string.IsNullOrWhiteSpace(SpecificLocationEntry.Text))
                SpecificLocationEntry.Text = _gpsAddress;

            SharedLocationLabel.Text = _gpsAddress;
            GpsBorder.IsVisible = true;
            ShareLocationButton.Text = "📍  Location Shared ✓";
            ShareLocationButton.BackgroundColor = Color.FromArgb("#388E3C");
        }
        catch (OperationCanceledException)
        {
            await DisplayAlert("Timed Out", "Could not get your location in time. Please try again.", "OK");
        }
        catch (FeatureNotSupportedException)
        {
            await DisplayAlert("Not Supported", "GPS is not supported on this device.", "OK");
        }
        catch (FeatureNotEnabledException)
        {
            await DisplayAlert("GPS Disabled", "Please enable GPS/Location services.", "OK");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AlertPage] share_location_clicked error: {ex}");
            await DisplayAlert("Error", $"Location error: {ex.Message}", "OK");
        }
        finally
        {
            ShareLocationButton.IsEnabled = true;
            if (ShareLocationButton.Text == "⏳  Getting location…")
                ShareLocationButton.Text = "📍  Share My Current Location";
        }
    }

    // ── Send Emergency Report ────────────────────────────────

    private async void send_clicked(object sender, EventArgs e)
    {
        try
        {
            if (!App.AuthService.IsSignedIn)
            {
                await DisplayAlert("Error",
                    "You are not signed in. Please log in and try again.", "OK");
                await Shell.Current.GoToAsync("//LoginPage");
                return;
            }

            string contact = ContactEntry.Text?.Trim() ?? string.Empty;
            string name = NameEntry.Text?.Trim() ?? string.Empty;
            string specificLoc = SpecificLocationEntry.Text?.Trim() ?? string.Empty;
            string building = BuildingPicker.SelectedItem?.ToString() ?? string.Empty;
            string floor = FloorPicker.SelectedItem?.ToString() ?? string.Empty;
            string incidentDetail = SpecificIncidentEntry.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(contact))
            {
                await DisplayAlert("Error", "Please enter a contact number.", "OK");
                return;
            }

            if (string.IsNullOrWhiteSpace(_incidentType))
            {
                await DisplayAlert("Error", "Please select an incident type.", "OK");
                return;
            }

            if (string.IsNullOrWhiteSpace(_locationType))
            {
                await DisplayAlert("Error",
                    "Please select a location type (School or Court).", "OK");
                return;
            }

            // Require building when school is selected
            if (_locationType == "School" && string.IsNullOrWhiteSpace(building))
            {
                await DisplayAlert("Error", "Please select a building (A, B, C, or D).", "OK");
                return;
            }

            if (_locationType == "Court" && string.IsNullOrWhiteSpace(_courtPosition))
            {
                await DisplayAlert("Error",
                    "Please select where in the court the incident is.", "OK");
                return;
            }

            // Photo is required
            if (string.IsNullOrWhiteSpace(_photoBase64))
            {
                await DisplayAlert("Photo Required",
                    "Please attach a photo of the incident before submitting.", "OK");
                return;
            }

            // Build human-readable location string
            string locationDisplay = _locationType;
            if (!string.IsNullOrEmpty(building)) locationDisplay += $" - {building}";
            if (!string.IsNullOrEmpty(_courtPosition) && _locationType == "Court")
                locationDisplay += $" - {_courtPosition}";
            if (!string.IsNullOrEmpty(floor)) locationDisplay += $" - {floor}";
            if (!string.IsNullOrEmpty(specificLoc)) locationDisplay += $" - {specificLoc}";

            string timestamp = DateTime.Now.ToString("MMM dd, yyyy, hh:mm tt");
            string docId = $"report_{DateTime.UtcNow:yyyyMMddHHmmssfff}";
            string currentUid = App.AuthService.LocalId ?? UserSession.Uid;

            string incidentDisplay = string.IsNullOrEmpty(incidentDetail)
                ? _incidentType
                : $"{_incidentType} — {incidentDetail}";

            var reportData = new Dictionary<string, object>
            {
                { "uid",              currentUid },
                { "studentNumber",    UserSession.StudentNumber },
                { "reporterName",     string.IsNullOrEmpty(name) ? "Anonymous" : name },
                { "contactNumber",    contact },
                { "incidentType",     _incidentType },
                { "incidentDetail",   incidentDetail },
                { "incidentDisplay",  incidentDisplay },
                { "locationType",     _locationType },
                { "building",         building },          // ← NEW: building A-D
                { "courtPosition",    _courtPosition },
                { "priority",         _isHighPriority ? "High" : "Low" },
                { "floor",            floor },
                { "specificLocation", specificLoc },
                { "locationDisplay",  locationDisplay },
                { "gpsAddress",       _gpsAddress },
                { "latitude",         _latitude?.ToString("F6") ?? "" },
                { "longitude",        _longitude?.ToString("F6") ?? "" },
                { "timestamp",        timestamp },
                { "status",           "Pending" },
                { "submittedBy",      currentUid },
                // The photo is stored separately (report_photos) so report lists stay small.
            };

            bool saved = await _firestore.SetDocumentAsync("emergency_reports", docId, reportData);
            if (!saved)
            {
                await DisplayAlert("Error",
                    "Failed to send the report. Please check your connection and try again.", "OK");
                return;
            }

            // Upload the photo while the admin alert is written (below).
            var photoTask = _firestore.SaveReportPhotoAsync(docId, _photoBase64!);

            var alertData = new Dictionary<string, object>
            {
                { "reportId",        docId },
                { "timestamp",       timestamp },
                { "location",        locationDisplay },
                { "building",        building },
                { "incidentType",    _incidentType },
                { "incidentDetail",  incidentDetail },
                { "incidentDisplay", incidentDisplay },
                { "studentNumber",   UserSession.StudentNumber },
                { "priority",        _isHighPriority ? "High" : "Low" },
                { "status",          "Unread" },
            };

            // Push to admins right away, in the background — the student
            // shouldn't wait on it to see the confirmation.
            _ = TrySendAdminFcmAsync(locationDisplay, incidentDisplay, docId, _isHighPriority);

            var alertTask = _firestore.SetDocumentAsync("admin_alerts", docId, alertData);
            await Task.WhenAll(photoTask, alertTask);

            if (!alertTask.Result)
                Console.WriteLine($"[AlertPage] ⚠️ admin_alerts write failed for docId='{docId}'");
            if (!photoTask.Result)
                Console.WriteLine($"[AlertPage] ⚠️ photo upload failed for docId='{docId}'");

            await DisplayAlert("✅ Report Sent",
                "Your emergency report has been submitted. Help is on the way!", "OK");

            if (_latitude.HasValue && _longitude.HasValue)
            {
                _activeReportDocId = docId;
                StartLiveLocationTracking(docId);
            }

            ResetForm();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AlertPage] send_clicked unhandled exception: {ex}");
            await DisplayAlert("Unexpected Error",
                "Something went wrong while sending your report. Please try again.", "OK");
        }
    }

    // ── Admin FCM helper ──────────────────────────────────────

    private async Task TrySendAdminFcmAsync(string locationDisplay, string incidentDisplay, string docId, bool isHighPriority)
    {
        try
        {
            // Notify every admin device, not just the first one found.
            var admins = await _firestore.QueryCollectionAsync("users", ("role", "admin"));
            var adminTokens = admins
                .Select(a => a.TryGetValue("fcmToken", out var tok) ? tok?.ToString() ?? "" : "")
                .Where(t => !string.IsNullOrEmpty(t))
                .Distinct()
                .ToList();

            if (adminTokens.Count == 0)
            {
                Console.WriteLine("[AlertPage] No admin FCM tokens found — no push sent.");
                return;
            }

            string notifBody = $"{incidentDisplay} at {locationDisplay}";

            await Task.WhenAll(adminTokens.Select(token => isHighPriority
                ? FcmService.SendHighPriorityAlertAsync(token,
                    title: "🚨 HIGH PRIORITY ALERT",
                    body: $"{notifBody} — immediate response needed!",
                    reportDocId: docId)
                : FcmService.SendLowPriorityAlertAsync(token,
                    title: "⚠️ New Alert Report",
                    body: notifBody,
                    reportDocId: docId)));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AlertPage] Admin FCM error: {ex.Message}");
        }
    }

    // ── Live Location Tracking ────────────────────────────────

    private void StartLiveLocationTracking(string docId)
    {
        _trackingCts?.Cancel();
        _trackingCts = new CancellationTokenSource();
        var token = _trackingCts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(5000, token);
                    var locRequest = new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(5));
                    var location = await Geolocation.Default.GetLocationAsync(locRequest, token);
                    if (location != null)
                    {
                        var update = new Dictionary<string, object>
                        {
                            { "latitude",   location.Latitude.ToString("F6") },
                            { "longitude",  location.Longitude.ToString("F6") },
                            { "lastUpdate", DateTime.UtcNow.ToString("o") },
                        };
                        await _firestore.PatchFieldsAsync("emergency_reports", docId, update);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { Console.WriteLine($"[AlertPage] Live tracking error: {ex.Message}"); }
            }
        }, token);
    }

    // ── Reset Form ────────────────────────────────────────────

    private void ResetForm()
    {
        NameEntry.Text = string.Empty;
        ContactEntry.Text = string.Empty;
        SpecificLocationEntry.Text = string.Empty;
        SpecificIncidentEntry.Text = string.Empty;

        BuildingPicker.SelectedIndex = -1;
        FloorPicker.SelectedIndex = -1;

        BuildingContainer.IsVisible = false;
        FloorContainer.IsVisible = false;

        PrioritySwitch.IsToggled = false;
        _isHighPriority = false;

        SchoolImage.Source = "school_unclicked.png";
        CourtImage.Source = "court_unclicked.png";
        _locationType = string.Empty;

        CourtPositionContainer.IsVisible = false;
        ResetCourtPositionChips();
        _courtPosition = string.Empty;

        _incidentType = string.Empty;
        foreach (var (chip, label) in _incidentChips.Values)
        {
            chip.BackgroundColor = Color.FromArgb("#F5F5F5");
            chip.Stroke = new SolidColorBrush(Color.FromArgb("#E0E0E0"));
            label.TextColor = Color.FromArgb("#424242");
            label.FontAttributes = FontAttributes.None;
        }

        _latitude = null;
        _longitude = null;
        _gpsAddress = string.Empty;

        GpsBorder.IsVisible = false;
        SharedLocationLabel.Text = string.Empty;
        ShareLocationButton.Text = "📍  Share My Current Location";
        ShareLocationButton.BackgroundColor = Color.FromArgb("#1565C0");

        // ── Photo reset ──────────────────────────────────────
        _photoBase64 = null;
        PhotoPreviewImage.Source = null;
        PhotoPreviewContainer.IsVisible = false;
        AttachPhotoButton.Text = "📷  Take / Attach Photo";
    }

    // ── Photo Attach / Remove ────────────────────────────────

    private async void OnAttachPhotoClicked(object sender, EventArgs e)
    {
        try
        {
            string action = await DisplayActionSheet(
                "Incident Photo", "Cancel", null,
                "📷  Take a Photo", "🖼  Choose from Gallery");

            FileResult? photo = action switch
            {
                "📷  Take a Photo" => await MediaPicker.Default.CapturePhotoAsync(),
                "🖼  Choose from Gallery" => await MediaPicker.Default.PickPhotoAsync(
                                                new MediaPickerOptions { Title = "Choose incident photo" }),
                _ => null
            };

            if (photo is null) return;

            // Resize + re-encode so the report stays small in the database
            var bytes = await ImageHelper.LoadCompressedJpegAsync(photo, ImageHelper.IncidentPhotoMaxSize);
            _photoBase64 = Convert.ToBase64String(bytes);

            // Show preview
            PhotoPreviewImage.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
            PhotoPreviewContainer.IsVisible = true;
            AttachPhotoButton.Text = "📷  Change Photo";
        }
        catch (PermissionException)
        {
            await DisplayAlert("Permission Required",
                "Please allow camera/photo access in your device settings.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Could not load photo: {ex.Message}", "OK");
        }
    }

    private void OnRemovePhotoClicked(object sender, EventArgs e)
    {
        _photoBase64 = null;
        PhotoPreviewImage.Source = null;
        PhotoPreviewContainer.IsVisible = false;
        AttachPhotoButton.Text = "📷  Take / Attach Photo";
    }

    private void OnPriorityToggled(object sender, ToggledEventArgs e)
    {
        _isHighPriority = e.Value;

        if (_isHighPriority)
        {
            PriorityTitleLabel.Text = "🔴  High Priority Alert";
            PrioritySubLabel.Text = "Admin will be notified with sound & vibration";
            PriorityBorder.BackgroundColor = Color.FromArgb("#FFEBEE");
            PriorityBorder.Stroke = Color.FromArgb("#EF9A9A");
            PriorityTitleLabel.TextColor = Color.FromArgb("#C62828");
            PrioritySubLabel.TextColor = Color.FromArgb("#E53935");
        }
        else
        {
            PriorityTitleLabel.Text = "🔕  Low Priority";
            PrioritySubLabel.Text = "Toggle ON to alert admin immediately";
            PriorityBorder.BackgroundColor = Color.FromArgb("#FFF3E0");
            PriorityBorder.Stroke = Color.FromArgb("#FFCCBC");
            PriorityTitleLabel.TextColor = Color.FromArgb("#757575");
            PrioritySubLabel.TextColor = Color.FromArgb("#9E9E9E");
        }
    }
}