namespace SAFETY_STEPS;

public partial class TutorialPage : ContentPage
{
    public TutorialPage()
    {
        InitializeComponent();
    }

    private async void OnBackClicked(object sender, EventArgs e)
    => await AppShell.GoToProfileAsync();
}