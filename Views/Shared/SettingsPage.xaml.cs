namespace SAFETY_STEPS;

public partial class SettingsPage : ContentPage
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    private async void OnBackClicked(object sender, EventArgs e)
    => await AppShell.GoToProfileAsync();
}