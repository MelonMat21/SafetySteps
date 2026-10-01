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
    { "Earthquake",           "InfoPage/earthquake.png" },
    { "Typhoon",              "InfoPage/typhoon.png" },
    { "Flood",                "InfoPage/flood.png" },
    { "Volcanic Eruption",    "InfoPage/volcanic_eruption.png" },
    { "Landslide",            "InfoPage/landslide.png" },
    { "Tsunami",              "InfoPage/tsunami.png" },
    { "Extreme Heat",         "InfoPage/extreme_heat.png" },
    { "Fire",                 "InfoPage/fire.png" },
    { "Electrical Accident",  "InfoPage/electrical_accident.png" },
    { "Structural Collapse",  "InfoPage/structural_collapse.png" },
    { "Stampede",             "InfoPage/stampede.png" },
    { "Asthma Attack",        "InfoPage/asthma_attack.png" },
    { "Fracture",             "InfoPage/fracture.png" },
    { "Sprain",               "InfoPage/sprain.png" },
    { "Strain",               "InfoPage/strain.png" },
    { "Burn",                 "InfoPage/burn.png" },
    { "Cut",                  "InfoPage/cut.png" },
    { "Nosebleed",            "InfoPage/nosebleed.png" },
    { "Fainting",             "InfoPage/fainting.png" },
    { "Heat Stroke",          "InfoPage/heat_stroke.png" },
    { "Choking",              "InfoPage/choking.png" },
};

        if (imageMap.TryGetValue(hazardName, out var imageName))
            HazardImage.Source = imageName;
    }

    private async void OnBackClicked(object sender, EventArgs e)
        => await Navigation.PopAsync();
}








