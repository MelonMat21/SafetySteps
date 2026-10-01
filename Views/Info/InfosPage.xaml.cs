namespace SAFETY_STEPS;

public partial class InfosPage : ContentPage
{
    public InfosPage()
    {
        InitializeComponent();
    }


    // Natural Disasters
    private async void OnEarthquakeClicked(object sender, EventArgs e)
        => await Navigation.PushAsync(new HazardInfoPage("Earthquake"));

    private async void OnTyphoonClicked(object sender, EventArgs e)
        => await Navigation.PushAsync(new HazardInfoPage("Typhoon"));

    private async void OnFloodClicked(object sender, EventArgs e)
        => await Navigation.PushAsync(new HazardInfoPage("Flood"));

    private async void OnVolcanicEruptionClicked(object sender, EventArgs e)
        => await Navigation.PushAsync(new HazardInfoPage("Volcanic Eruption"));

    private async void OnLandslideClicked(object sender, EventArgs e)
        => await Navigation.PushAsync(new HazardInfoPage("Landslide"));

    private async void OnTsunamiClicked(object sender, EventArgs e)
        => await Navigation.PushAsync(new HazardInfoPage("Tsunami"));

    private async void OnExtremeHeatClicked(object sender, EventArgs e)
        => await Navigation.PushAsync(new HazardInfoPage("Extreme Heat"));

    // School Dangers
    private async void OnFireClicked(object sender, EventArgs e)
        => await Navigation.PushAsync(new HazardInfoPage("Fire"));

    private async void OnElectricalAccidentClicked(object sender, EventArgs e)
        => await Navigation.PushAsync(new HazardInfoPage("Electrical Accident"));

    private async void OnStructuralCollapseClicked(object sender, EventArgs e)
        => await Navigation.PushAsync(new HazardInfoPage("Structural Collapse"));

    private async void OnStampedeClicked(object sender, EventArgs e)
        => await Navigation.PushAsync(new HazardInfoPage("Stampede"));

    // Injuries & Medical Conditions
    private async void OnAsthmaAttackClicked(object sender, EventArgs e)
        => await Navigation.PushAsync(new HazardInfoPage("Asthma Attack"));

    private async void OnFractureClicked(object sender, EventArgs e)
        => await Navigation.PushAsync(new HazardInfoPage("Fracture"));

    private async void OnSprainClicked(object sender, EventArgs e)
        => await Navigation.PushAsync(new HazardInfoPage("Sprain"));

    private async void OnStrainClicked(object sender, EventArgs e)
        => await Navigation.PushAsync(new HazardInfoPage("Strain"));

    private async void OnBurnClicked(object sender, EventArgs e)
        => await Navigation.PushAsync(new HazardInfoPage("Burn"));

    private async void OnCutLacerationClicked(object sender, EventArgs e)
        => await Navigation.PushAsync(new HazardInfoPage("Cut"));

    private async void OnNosebleedClicked(object sender, EventArgs e)
        => await Navigation.PushAsync(new HazardInfoPage("Nosebleed"));

    private async void OnFaintingClicked(object sender, EventArgs e)
        => await Navigation.PushAsync(new HazardInfoPage("Fainting"));

    private async void OnHeatStrokeClicked(object sender, EventArgs e)
        => await Navigation.PushAsync(new HazardInfoPage("Heat Stroke"));

    private async void OnChokingClicked(object sender, EventArgs e)
        => await Navigation.PushAsync(new HazardInfoPage("Choking"));
}