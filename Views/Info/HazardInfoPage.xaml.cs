namespace SAFETY_STEPS;

public partial class HazardInfoPage : ContentPage
{
    public HazardInfoPage(string hazardName)
    {
        InitializeComponent();

        Title = hazardName;
        HazardTitle.Text = hazardName;

        // Map hazard name to its infographic image file
        var imageMap = new Dictionary<string, string>
{
    { "Earthquake",           "earthquake.png" },
    { "Typhoon",              "typhoon.png" },
    { "Flood",                "flood.png" },
    { "Volcanic Eruption",    "volcanic_eruption.png" },
    { "Landslide",            "landslide.png" },
    { "Tsunami",              "tsunami.png" },
    { "Extreme Heat",         "extreme_heat.png" },
    { "Fire",                 "fire.png" },
    { "Electrical Accident",  "electrical_accident.png" },
    { "Structural Collapse",  "structural_collapse.png" },
    { "Stampede",             "stampede.png" },
    { "Asthma Attack",        "asthma_attack.png" },
    { "Fracture",             "fracture.png" },
    { "Sprain",               "sprain.png" },
    { "Strain",               "strain.png" },
    { "Burn",                 "burn.png" },
    { "Cut",                  "cut.png" },
    { "Nosebleed",            "nosebleed.png" },
    { "Fainting",             "fainting.png" },
    { "Heat Stroke",          "heat_stroke.png" },
    { "Choking",              "choking.png" },
};

        if (imageMap.TryGetValue(hazardName, out var imageName))
            HazardImage.Source = imageName;
    }

    private async void OnBackClicked(object sender, EventArgs e)
        => await Navigation.PopAsync();
}








