namespace SAFETY_STEPS;

/// <summary>
/// Shown for a split second while TryRestoreSessionAsync() runs.
/// Swap this out for your real splash / branding screen if you have one.
/// </summary>
public class LoadingPage : ContentPage
{
    public LoadingPage()
    {
        BackgroundColor = Color.FromArgb("#ab0404");

        Content = new VerticalStackLayout
        {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Spacing = 16,
            Children =
            {
                new ActivityIndicator
                {
                    IsRunning = true,
                    Color     = Colors.White,
                    HeightRequest = 48,
                    WidthRequest  = 48
                },
                new Label
                {
                    Text              = "Safety Steps",
                    TextColor         = Colors.White,
                    FontSize          = 22,
                    FontAttributes    = FontAttributes.Bold,
                    HorizontalOptions = LayoutOptions.Center
                }
            }
        };
    }
}