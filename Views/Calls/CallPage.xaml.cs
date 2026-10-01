using SAFETY_STEPS.ViewModels;

namespace SAFETY_STEPS;

public partial class CallPage : ContentPage
{
    private readonly CallViewModel _vm;
    private bool _speakerOn = false;

    public string ReceiverUserId { set => _vm.ReceiverUserId = value; }
    public string ReceiverName { set => _vm.ReceiverName = value; }

    public CallPage(CallViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

#if ANDROID
        _vm.Initialize(Android.App.Application.Context);
#endif
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();

        // FIX 5: end the call properly if user swipes back instead of tapping End
        if (_vm.IsCalling || _vm.IsCallActive)
            await _vm.EndCallAsync();

        _vm.Cleanup();
    }

    private async void OnCallTapped(object sender, EventArgs e)
        => await _vm.StartCallAsync();

    private async void OnEndCallTapped(object sender, EventArgs e)
    {
        await _vm.EndCallAsync();
        await Navigation.PopAsync();
    }

    private void OnMuteTapped(object sender, EventArgs e)
        => _vm.ToggleMute();

    private void OnSpeakerTapped(object sender, EventArgs e)
    {
        _speakerOn = !_speakerOn;
        _vm.SetSpeakerphone(_speakerOn);
    }
}