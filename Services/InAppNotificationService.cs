using Microsoft.Maui.Controls.Shapes;

namespace SAFETY_STEPS.Services;

/// <summary>
/// Shows a slide-down banner inside the running app (like iOS banner notifications).
/// Tapping it navigates to the given Shell route.
/// Call Show() from anywhere — it marshals to the UI thread automatically.
/// </summary>
public static class InAppNotificationService
{
    private static bool _isShowing = false;

    // ── Public entry point ────────────────────────────────────────────────
    public static void Show(string title, string body,
                            string? shellRoute = null, bool isUrgent = false)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
            await ShowAsync(title, body, shellRoute, isUrgent));
    }

    // ── Core implementation ───────────────────────────────────────────────
    private static async Task ShowAsync(string title, string body,
                                        string? shellRoute, bool isUrgent)
    {
        if (_isShowing) return;
        _isShowing = true;

        try
        {
            // Resolve the visible ContentPage
            var root = Application.Current?.Windows.FirstOrDefault()?.Page;
            ContentPage? page = root switch
            {
                Shell s => s.CurrentPage as ContentPage,
                NavigationPage n => n.CurrentPage as ContentPage,
                ContentPage cp => cp,
                _ => null
            };

            if (page == null) { _isShowing = false; return; }

            var originalContent = page.Content;

            var bgColor = isUrgent ? Color.FromArgb("#B71C1C") : Color.FromArgb("#0D47A1");
            var subColor = isUrgent ? Color.FromArgb("#FFCDD2") : Color.FromArgb("#BBDEFB");

            bool tapped = false;
            var tap = new TapGestureRecognizer();

            // ── Chevron label (col 1) ─────────────────────────────────
            var chevron = new Label
            {
                Text = !string.IsNullOrEmpty(shellRoute) ? "›" : "×",
                TextColor = Colors.White,
                FontSize = 24,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.End
            };
            Grid.SetColumn(chevron, 1);

            var contentGrid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = 28 }
                }
            };
            contentGrid.Add(new VerticalStackLayout
            {
                Spacing = 4,
                Children =
                {
                    new Label
                    {
                        Text           = title,
                        TextColor      = Colors.White,
                        FontAttributes = FontAttributes.Bold,
                        FontSize       = 15,
                        MaxLines       = 1,
                        LineBreakMode  = LineBreakMode.TailTruncation
                    },
                    new Label
                    {
                        Text          = body,
                        TextColor     = subColor,
                        FontSize      = 13,
                        MaxLines      = 2,
                        LineBreakMode = LineBreakMode.TailTruncation
                    }
                }
            });
            contentGrid.Add(chevron);

            var banner = new Border
            {
                BackgroundColor = bgColor,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 16 },
                Padding = new Thickness(18, 14),
                Margin = new Thickness(12, 50, 12, 0),
                VerticalOptions = LayoutOptions.Start,
                HorizontalOptions = LayoutOptions.Fill,
                TranslationY = -180,
                ZIndex = 9999,
                Shadow = new Shadow
                {
                    Brush = Brush.Black,
                    Opacity = 0.4f,
                    Radius = 14,
                    Offset = new Point(0, 5)
                },
                GestureRecognizers = { tap },
                Content = contentGrid
            };

            // Wire tap
            tap.Tapped += async (_, _) =>
            {
                if (tapped) return;
                tapped = true;
                await banner.TranslateTo(0, -180, 250, Easing.CubicIn);
                page.Content = originalContent;
                _isShowing = false;
                if (!string.IsNullOrEmpty(shellRoute))
                    await Shell.Current.GoToAsync(shellRoute);
            };

            // Wrap page
            var overlay = new Grid();
            overlay.Add(originalContent);
            overlay.Add(banner);
            page.Content = overlay;

            // Animate in
            await banner.TranslateTo(0, 0, 380, Easing.SpringOut);

            // Auto-dismiss after 5 s
            await Task.Delay(5000);

            if (_isShowing && !tapped)
            {
                await banner.TranslateTo(0, -180, 280, Easing.CubicIn);
                page.Content = originalContent;
                _isShowing = false;
            }
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[InAppNotification] Error: {ex.Message}");
            _isShowing = false;
        }
    }
}