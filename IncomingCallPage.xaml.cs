using SAFETY_STEPS.Services;
using SAFETY_STEPS.ViewModels;

namespace SAFETY_STEPS;

public partial class IncomingCallPage : ContentPage
{
    private readonly IncomingCallViewModel _vm;

    public IncomingCallPage(IncomingCallViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Console.WriteLine($"[IncomingPage] OnAppearing — Uid='{UserSession.Uid}'");

#if ANDROID
        var context = Android.App.Application.Context;
        _vm.EnsureInitialized(context);
#endif
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Do not call Cleanup() here: it stops the Firestore incoming-call poll, so after rejecting
        // one call the admin would never see another until re-login.
    }

    private async void OnAcceptTapped(object sender, EventArgs e)
    {
        var ok = await _vm.AcceptCallAsync();
        if (!ok)
            await DisplayAlert("Unavailable", "This call is no longer available.", "OK");
    }

    private async void OnRejectTapped(object sender, EventArgs e)
        => await _vm.RejectCallAsync();

    private async void OnEndCallTapped(object sender, EventArgs e)
    {
        await _vm.EndCallAsync();
        // Pop back after ending so admin returns to the calls tab
        if (Navigation.NavigationStack.Count > 1)
            await Navigation.PopAsync();
    }

    private void OnMuteTapped(object sender, EventArgs e)
        => _vm.ToggleMute();
}