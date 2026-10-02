using System.Collections.ObjectModel;
using System.Linq;
using SAFETY_STEPS.Models;
using SAFETY_STEPS.Services;
using SAFETY_STEPS.ViewModels;
using SAFETY_STEPS.FireBase;

namespace SAFETY_STEPS;

/// <summary>
/// Admin tab showing the call queue.
/// Displays caller profile picture, name, student number, and action buttons.
/// </summary>
public partial class CallsPage : ContentPage
{
    private readonly AgoraCallService _agora;
    private readonly CallSignalingService _signal;
    private readonly IncomingCallViewModel _incomingVm;
    private readonly FirestoreService _firestore;
    private readonly ObservableCollection<CallQueueItem> _queueItems = new();
    private static readonly TimeSpan QueueRefreshInterval = TimeSpan.FromSeconds(3);
    private CancellationTokenSource? _refreshCts;

    public ObservableCollection<CallQueueItem> QueueItems => _queueItems;
    public string HeaderText => "Call Queue";
    public string QueueSummaryText { get; private set; } = "Loading queue...";

    public CallsPage(AgoraCallService agora, CallSignalingService signal,
                     IncomingCallViewModel incomingVm, FirestoreService firestore)
    {
        InitializeComponent();
        _agora = agora;
        _signal = signal;
        _incomingVm = incomingVm;
        _firestore = firestore;
        BindingContext = this;
    }

    public CallsPage() : this(
        IPlatformApplication.Current!.Services.GetRequiredService<AgoraCallService>(),
        IPlatformApplication.Current!.Services.GetRequiredService<CallSignalingService>(),
        IPlatformApplication.Current!.Services.GetRequiredService<IncomingCallViewModel>(),
        IPlatformApplication.Current!.Services.GetRequiredService<FirestoreService>())
    { }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

#if ANDROID
        _agora.Initialize(Android.App.Application.Context);
#endif
        _refreshCts = new CancellationTokenSource();
        await ReloadQueueAsync();
        _ = StartQueueRefreshLoopAsync(_refreshCts.Token);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = null;
    }

    private async Task StartQueueRefreshLoopAsync(CancellationToken token)
    {
        // One timer per loop: a PeriodicTimer allows only one waiter, and a quick
        // disappear/re-appear could otherwise leave two loops sharing it.
        using var timer = new PeriodicTimer(QueueRefreshInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(token))
                await ReloadQueueAsync();
        }
        catch (OperationCanceledException) { }
    }

    private async Task ReloadQueueAsync()
    {
        var adminUid = UserSession.Uid;
        if (string.IsNullOrEmpty(adminUid)) return;

        List<CallData> pending;
        try
        {
            pending = await _signal.GetPendingCallsForAdminAsync(adminUid);
        }
        catch (Exception ex)
        {
            // Quota/network errors must not crash the page — the next tick retries.
            Console.WriteLine($"[CallsPage] Queue refresh failed: {ex.Message}");
            return;
        }

        // Update on main thread
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Same calls in the same order → just tick the wait times instead of
            // rebuilding every card (and re-decoding every avatar) each refresh.
            if (pending.Select(c => c.ChannelName).SequenceEqual(_queueItems.Select(q => q.ChannelName)))
            {
                foreach (var item in _queueItems)
                    item.RefreshWaitText();
                return;
            }

            var newItems = pending.Select((call, i) => CallQueueItem.FromCall(call, i + 1)).ToList();

            _queueItems.Clear();
            foreach (var item in newItems)
                _queueItems.Add(item);

            QueueSummaryText = $"Pending calls: {_queueItems.Count}";
            OnPropertyChanged(nameof(QueueSummaryText));
        });
    }

    private async void OnAcceptClicked(object sender, EventArgs e)
    {
        if (sender is not Button button || button.CommandParameter is not string channelName) return;

        try
        {
            var call = await ResolveCallAsync(channelName);
            var accepted = false;
            if (call != null)
            {
                _incomingVm.NotifyIncomingCall(call);
                accepted = await _incomingVm.AcceptCallAsync();
            }

            if (!accepted)
            {
                await DisplayAlert("Unavailable", "This call is no longer available.", "OK");
                await ReloadQueueAsync();
                return;
            }

            await AppShell.NavigateAdminToIncomingCallPageAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CallsPage] Accept failed: {ex.Message}");
            await DisplayAlert("Error", "Could not accept the call. Please try again.", "OK");
        }
    }

    private async void OnRejectClicked(object sender, EventArgs e)
    {
        if (sender is not Button button || button.CommandParameter is not string channelName) return;
        try
        {
            await _signal.RejectCallAsync(channelName);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CallsPage] Reject failed: {ex.Message}");
        }
        await ReloadQueueAsync();
    }

    private async void OnViewProfileClicked(object sender, EventArgs e)
    {
        if (sender is not Button button || button.CommandParameter is not string callerId) return;

        if (string.IsNullOrEmpty(callerId))
        {
            await DisplayAlert("Error", "Caller ID is not available.", "OK");
            return;
        }

        await Navigation.PushAsync(new AdminStudentProfilePage(_firestore, callerId));
    }

    private async Task<CallData?> ResolveCallAsync(string channelName)
    {
        var pending = await _signal.GetPendingCallsForAdminAsync(UserSession.Uid);
        return pending.FirstOrDefault(c => c.ChannelName == channelName);
    }
}

// ── CallQueueItem ─────────────────────────────────────────────────────────────

public class CallQueueItem : System.ComponentModel.INotifyPropertyChanged
{
    public string ChannelName { get; init; } = "";
    public string CallerId { get; init; } = "";
    public bool IsUrgent { get; init; }
    public string QueueLabel { get; init; } = "";
    public string CallerDisplay { get; init; } = "";
    public string StudentNumber { get; init; } = "";
    public string StudentNumberDisplay { get; init; } = "";
    public bool HasStudentNumber { get; init; }
    public DateTime CreatedAtUtc { get; init; }

    private string _waitText = "";
    public string WaitText
    {
        get => _waitText;
        private set
        {
            if (_waitText == value) return;
            _waitText = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(WaitText)));
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public void RefreshWaitText()
    {
        var wait = DateTime.UtcNow - CreatedAtUtc;
        WaitText = wait.TotalMinutes >= 1
            ? $"{(int)wait.TotalMinutes}m waiting"
            : $"{Math.Max(1, wait.Seconds)}s waiting";
    }

    /// <summary>
    /// The profile image shown in the call card.
    /// Sourced from base64 stored in the user's RTDB document,
    /// or a default avatar when not available.
    /// </summary>
    public ImageSource ProfileImageSource { get; init; } = "profile2.png";

    /// <summary>
    /// Creates a CallQueueItem from call data that already includes the
    /// caller's profile image (resolved by CallSignalingService).
    /// </summary>
    public static CallQueueItem FromCall(CallData call, int position)
    {
        bool hasSN = !string.IsNullOrWhiteSpace(call.StudentNumber);

        // Format: "Mathew(02000388350)" when student number is available
        string callerDisplay = hasSN
            ? $"{call.CallerName}({call.StudentNumber})"
            : call.CallerName;

        // ── Load profile image from already-fetched call data ─────────────
        // GetPendingCallsForAdminAsync already called FetchProfileImageBase64Async
        // and stored the result in call.ProfileImageBase64 — no second Firestore
        // round-trip needed here. The previous code's redundant fetch was the
        // reason the avatar never appeared (the call often failed silently).
        ImageSource profileSource = "profile2.png";
        if (!string.IsNullOrWhiteSpace(call.ProfileImageBase64))
        {
            try
            {
                var bytes = Convert.FromBase64String(call.ProfileImageBase64);
                profileSource = ImageSource.FromStream(() => new MemoryStream(bytes));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CallQueueItem] Profile image decode error: {ex.Message}");
            }
        }

        var item = new CallQueueItem
        {
            ChannelName = call.ChannelName,
            CallerId = call.CallerId,
            IsUrgent = call.IsUrgent,
            QueueLabel = $"#{position}",
            CallerDisplay = callerDisplay,
            StudentNumber = call.StudentNumber,
            StudentNumberDisplay = "",
            HasStudentNumber = false,   // name already includes SN inline
            CreatedAtUtc = call.CreatedAtUtc,
            ProfileImageSource = profileSource
        };
        item.RefreshWaitText();
        return item;
    }
}