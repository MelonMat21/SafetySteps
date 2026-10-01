namespace SAFETY_STEPS;

public partial class AboutUs : ContentPage
{
    public AboutUs()
    {
        InitializeComponent();
    }

    private async void OnBackClicked(object sender, EventArgs e)
    => await AppShell.GoToProfileAsync();
}