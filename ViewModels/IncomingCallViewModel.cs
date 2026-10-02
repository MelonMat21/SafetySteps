using System.ComponentModel;
using System.Runtime.CompilerServices;
using SAFETY_STEPS.FireBase;
using SAFETY_STEPS.Models;
using SAFETY_STEPS.Services;

namespace SAFETY_STEPS.ViewModels;

public class IncomingCallViewModel : INotifyPropertyChanged
{
    private readonly AgoraCallService _agora;
    private readonly CallSignalingService _signal;
    private readonly AgoraTokenService _tokenService;
    private readonly FirebaseAuthService _auth;   // ← NEW: needed for AuthReadyAsync

    private CallData? _currentCall;

    private string _callerName = "Unknown";
    private string _statusText = "No incoming calls";
    private string _muteIcon = "🎙️";
    private bool _hasIncoming = false;
    private bool _isCallActive = false;
    private bool _isMuted = false;
    private bool _isUrgentCall = false;
    private ImageSource _profileImageSource = "profile2.png";

    private readonly HashSet<string> _seenChannels = new();

    // Lets App.xaml.cs navigate to the calls tab when a call arrives
    public event Action? OnIncomingCallReceived;

    private Action? _registeredIncomingNavigation;

    /// <summary>Replaces any previous navigation handler so login/logout cycles do not stack duplicate navigations.</summary>
    public void RegisterIncomingNavigationHandler(Action handler)
    {
        if (_registeredIncomingNavigation is not null)
            OnIncomingCallReceived -= _registeredIncomingNavigation;
        _registeredIncomingNavigation = handler;
        OnIncomingCallReceived += handler;
    }

    public void ClearIncomingNavigationHandler()
    {
        if (_registeredIncomingNavigation is not null)
        {
            OnIncomingCallReceived -= _registeredIncomingNavigation;
            _registeredIncomingNavigation = null;
        }
    }

    public string CallerName
    {
        get => _callerName;
        set { _callerName = value; OnPropertyChanged(); }
    }

    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    public string MuteIcon
    {
        get => _muteIcon;
        set { _muteIcon = value; OnPropertyChanged(); }
    }

    public bool HasIncomingCall
    {
        get => _hasIncoming;
        set { _hasIncoming = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotCallActive)); }
    }

    public bool IsCallActive
    {
        get => _isCallActive;
        set { _isCallActive = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotCallActive)); }
    }

    public bool IsNotCallActive => !_hasIncoming && !_isCallActive;
    public bool IsUrgentCall
    {
        get => _isUrgentCall;
        set { _isUrgentCall = value; OnPropertyChanged(); }
    }

    public ImageSource ProfileImageSource
    {
        get => _profileImageSource;
        set { _profileImageSource = value; OnPropertyChanged(); }
    }

    public IncomingCallViewModel(AgoraCallService agora, CallSignalingService signal,
                                  AgoraTokenService tokenService, FirebaseAuthService auth)
    {
        _agora = agora;
        _signal = signal;
        _tokenService = tokenService;
        _auth = auth;   // ← NEW
        _agora.OnRemoteUserOffline += _ => HandleRemoteLeft();
    }

    private bool _listening = false;
    private bool _initializationPending = false;  // ← NEW: guard against double-init

    /// <summary>
    /// Call this from the page's OnAppearing / Initialize.
    /// It awaits auth being ready before starting the listener, so it is
    /// safe to call immediately on app start even before session-restore
    /// has finished.
    /// </summary>
    public async void EnsureInitialized(Android.Content.Context context)
    {
        // Initialize Agora synchronously — safe to do before auth is ready.
        _agora.Initialize(context);

        // Prevent a second concurrent initialization if the page is
        // re-appeared while we are still waiting for auth.
        if (_listening || _initializationPending) return;
        _initializationPending = true;

        // KEY FIX: wait for TryRestoreSessionAsync (or SignInAsync) to finish
        // so that UserSession.Uid is populated before we read it.
        await _auth.AuthReadyAsync;

        _initializationPending = false;

        Console.WriteLine($"[IncomingVM] EnsureInitialized: Uid='{UserSession.Uid}' listening={_listening}");

        if (string.IsNullOrEmpty(UserSession.Uid))
        {
            Console.WriteLine("[IncomingVM] ⚠️ UserSession.Uid is EMPTY after auth ready — user is not logged in. Incoming calls will NOT be detected.");
            return;
        }

        if (!_listening)
        {
            _signal.ListenForIncomingCalls(UserSession.Uid, _seenChannels);
            _signal.OnIncomingCall -= HandleIncomingCall;
            _signal.OnIncomingCall += HandleIncomingCall;
            _listening = true;
            Console.WriteLine($"[IncomingVM] Now listening for calls to uid='{UserSession.Uid}'");
        }
    }

    private void HandleIncomingCall(CallData call)
    {
        _currentCall = call;
        CallerName = call.IsUrgent ? $"{call.CallerName} (URGENT)" : call.CallerName;
        StatusText = "Incoming call…";
        HasIncomingCall = true;
        IsCallActive = false;
        IsUrgentCall = call.IsUrgent;

        // Load caller's profile image if available
        if (!string.IsNullOrWhiteSpace(call.ProfileImageBase64))
        {
            try
            {
                var bytes = Convert.FromBase64String(call.ProfileImageBase64);
                ProfileImageSource = ImageSource.FromStream(() => new MemoryStream(bytes));
            }
            catch
            {
                ProfileImageSource = "profile2.png";
            }
        }
        else
        {
            ProfileImageSource = "profile2.png";
        }

        // Only urgent calls force popup navigation.
        if (call.IsUrgent)
            OnIncomingCallReceived?.Invoke();
    }

    public async Task<bool> AcceptCallAsync()
    {
        if (_currentCall == null) return false;

        var micStatus = await Permissions.CheckStatusAsync<Permissions.Microphone>();
        if (micStatus != PermissionStatus.Granted)
            micStatus = await Permissions.RequestAsync<Permissions.Microphone>();

        if (micStatus != PermissionStatus.Granted)
        {
            StatusText = "Microphone permission denied";
            return false;
        }

        // Fetch the token and claim the call at the same time — the caller hears
        // "connected" sooner, and the token server's wake-up time overlaps the DB round-trips.
        var channel = _currentCall.ChannelName;
        var tokenTask = _tokenService.GetTokenAsync(channel, 0);
        var acceptTask = _signal.TryAcceptCallAsync(channel);
        await Task.WhenAll(tokenTask, acceptTask);
        var token = tokenTask.Result;

        if (!acceptTask.Result)
        {
            StatusText = "Call no longer available";
            ResetState();
            return false;
        }

        if (string.IsNullOrEmpty(token))
        {
            // Already marked accepted, so end it rather than reject it.
            await _signal.EndCallAsync(channel);
            ResetState();
            StatusText = "Failed to get token. Check server.";
            return false;
        }

        _agora.JoinChannel(channel, token);

        HasIncomingCall = false;
        IsCallActive = true;
        StatusText = "Connected";
        return true;
    }

    public async Task RejectCallAsync()
    {
        if (_currentCall == null) return;
        await _signal.RejectCallAsync(_currentCall.ChannelName);
        _seenChannels.Remove(_currentCall.ChannelName);
        ResetState();
    }

    public async Task EndCallAsync()
    {
        if (_currentCall == null) return;
        await _signal.EndCallAsync(_currentCall.ChannelName);
        _agora.LeaveChannel();
        _agora.RestoreAudio();
        _seenChannels.Remove(_currentCall.ChannelName);
        ResetState();
    }

    public void ToggleMute()
    {
        _isMuted = !_isMuted;
        _agora.MuteLocalAudio(_isMuted);
        MuteIcon = _isMuted ? "🔇" : "🎙️";
    }

    private void HandleRemoteLeft()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (IsCallActive) await EndCallAsync();
        });
    }

    public void Cleanup()
    {
        _signal.OnIncomingCall -= HandleIncomingCall;
        _signal.StopListeningForIncoming();
        _agora.LeaveChannel();
        _listening = false;
        _initializationPending = false;
    }

    private void ResetState()
    {
        _currentCall = null;
        CallerName = "Unknown";
        StatusText = "No incoming calls";
        HasIncomingCall = false;
        IsCallActive = false;
        _isMuted = false;
        MuteIcon = "🎙️";
        IsUrgentCall = false;
        ProfileImageSource = "profile2.png";
    }

    /// <summary>
    /// Called by HomePage when it detects an incoming call before navigating here.
    /// This seeds the VM so the Accept/Reject UI is ready on first appearance.
    /// </summary>
    public void NotifyIncomingCall(CallData call)
    {
        HandleIncomingCall(call);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}