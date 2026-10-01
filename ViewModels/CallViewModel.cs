using System.ComponentModel;
using System.Runtime.CompilerServices;
using SAFETY_STEPS.FireBase;
using SAFETY_STEPS.Models;
using SAFETY_STEPS.Services;

namespace SAFETY_STEPS.ViewModels;

public class CallViewModel : INotifyPropertyChanged
{
    private readonly AgoraCallService _agora;
    private readonly CallSignalingService _signal;
    private readonly AgoraTokenService _tokenService;
    private readonly FirestoreService _firestore;

    private Action<uint>? _remoteJoinedHandler;
    private Action<uint>? _remoteOfflineHandler;

    private string _channelName = "";
    private string _muteIcon = "🎙️";
    private string _statusText = "Ready";
    private bool _isCallActive = false;
    private bool _isMuted = false;
    private bool _isCalling = false;
    private bool _isUrgent = false;

    public string ReceiverUserId { get; set; } = "";
    public string ReceiverName { get; set; } = "";

    public bool IsUrgent
    {
        get => _isUrgent;
        set { _isUrgent = value; OnPropertyChanged(); }
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

    public bool IsCallActive
    {
        get => _isCallActive;
        set { _isCallActive = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsIdle)); }
    }

    public bool IsCalling
    {
        get => _isCalling;
        set { _isCalling = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsIdle)); }
    }

    public bool IsIdle => !_isCalling && !_isCallActive;

    public CallViewModel(AgoraCallService agora, CallSignalingService signal,
                         AgoraTokenService tokenService, FirestoreService firestore)
    {
        _agora = agora;
        _signal = signal;
        _tokenService = tokenService;
        _firestore = firestore;

        _signal.OnCallStatusChanged += HandleCallStatusChanged;

        _remoteJoinedHandler = _ => OnRemoteJoined();
        _remoteOfflineHandler = _ => HandleRemoteLeft();
        _agora.OnRemoteUserJoined += _remoteJoinedHandler;
        _agora.OnRemoteUserOffline += _remoteOfflineHandler;
    }

    public void Initialize(Android.Content.Context context)
        => _agora.Initialize(context);

    public async Task StartCallAsync()
    {
        if (string.IsNullOrEmpty(ReceiverUserId))
        {
            StatusText = "No receiver selected";
            return;
        }

        if (IsCalling || IsCallActive) return;

        var micStatus = await Permissions.CheckStatusAsync<Permissions.Microphone>();
        if (micStatus != PermissionStatus.Granted)
            micStatus = await Permissions.RequestAsync<Permissions.Microphone>();
        if (micStatus != PermissionStatus.Granted)
        {
            StatusText = "Microphone permission denied";
            return;
        }

        _channelName = $"call_{DateTime.UtcNow:yyyyMMddHHmmssfff}";
        string myId = UserSession.Uid;
        string myName = UserSession.DisplayName;
        string myStudentNumber = UserSession.StudentNumber;  // ← grab student number

        if (string.IsNullOrWhiteSpace(myStudentNumber) || string.IsNullOrWhiteSpace(myName) || myName == "Unknown")
        {
            try
            {
                var me = await _firestore.GetDocumentAsync("users", myId);
                if (me != null)
                {
                    if (string.IsNullOrWhiteSpace(myStudentNumber))
                    {
                        myStudentNumber = me.TryGetValue("studentNumber", out var sn1) ? sn1?.ToString() ?? "" : "";
                        if (string.IsNullOrWhiteSpace(myStudentNumber))
                            myStudentNumber = me.TryGetValue("studentID", out var sn2) ? sn2?.ToString() ?? "" : "";
                        if (!string.IsNullOrWhiteSpace(myStudentNumber))
                            UserSession.SetStudentNumber(myStudentNumber);
                    }

                    if (string.IsNullOrWhiteSpace(myName) || myName == "Unknown")
                    {
                        var resolvedName = me.TryGetValue("name", out var n) ? n?.ToString() ?? "" : "";
                        if (!string.IsNullOrWhiteSpace(resolvedName))
                        {
                            myName = resolvedName;
                            UserSession.SetName(resolvedName);
                        }
                    }
                }
            }
            catch { }
        }

        Console.WriteLine($"[CallVM] StartCall — callerId='{myId}'  receiverId='{ReceiverUserId}'  " +
                          $"channel='{_channelName}'  studentNumber='{myStudentNumber}'");

        IsCalling = true;
        StatusText = $"Calling {ReceiverName}…";

        // ── Get Agora token ───────────────────────────────────────────────
        var token = await _tokenService.GetTokenAsync(_channelName, 0);
        Console.WriteLine($"[CallVM] Token fetched: " +
            (string.IsNullOrEmpty(token) ? "NULL/EMPTY ← CHECK RAILWAY SERVER URL in FirebaseConfig.AgoraTokenServerBaseUrl" : "OK"));

        if (string.IsNullOrEmpty(token))
        {
            StatusText = "Failed to get call token. Check server URL.";
            IsCalling = false;
            return;
        }

        _agora.JoinChannel(_channelName, token);

        // ── Signal the call (studentNumber is now included in the initial doc) ─
        await _signal.StartCallAsync(myId, myName, ReceiverUserId, _channelName,
                                     receiverFirebaseUid: ReceiverUserId,
                                     isUrgent: IsUrgent,
                                     studentNumber: myStudentNumber);   // ← FIX: pass here directly

        _signal.ListenForCallStatus(_channelName);

        // ── Send FCM to receiver ──────────────────────────────────────────
        try
        {
            string? fcmToken = null;
            string? resolvedReceiverId = null;
            var receiverUid = ReceiverUserId;

            // Step 1: direct GET users/{receiverUid}
            var receiverDoc = await _firestore.GetDocumentAsync("users", receiverUid);
            if (receiverDoc != null &&
                receiverDoc.TryGetValue("fcmToken", out var rawTok) &&
                !string.IsNullOrEmpty(rawTok?.ToString()))
            {
                fcmToken = rawTok.ToString();
                resolvedReceiverId = receiverUid;
            }

            // Step 2: fallback — query by uid field
            if (string.IsNullOrEmpty(fcmToken))
            {
                var results = await _firestore.QueryCollectionAsync("users", ("uid", receiverUid));
                if (results.Count > 0)
                {
                    var doc = results[0];
                    if (doc.TryGetValue("fcmToken", out var tok2))
                        fcmToken = tok2?.ToString() ?? "";
                    if (doc.TryGetValue("docId", out var dId))
                        resolvedReceiverId = dId?.ToString() ?? receiverUid;
                }
            }

            // Step 3: fallback — first admin
            if (string.IsNullOrEmpty(fcmToken))
            {
                var admins = await _firestore.QueryCollectionAsync("users", ("role", "admin"));
                if (admins.Count > 0)
                {
                    var doc = admins[0];
                    if (doc.TryGetValue("fcmToken", out var tok3))
                        fcmToken = tok3?.ToString() ?? "";
                    if (doc.TryGetValue("docId", out var dId))
                        resolvedReceiverId = dId?.ToString() ?? receiverUid;
                }
            }

            if (!string.IsNullOrEmpty(fcmToken))
            {
                if (!string.IsNullOrEmpty(resolvedReceiverId) && resolvedReceiverId != receiverUid)
                {
                    await _firestore.PatchFieldsAsync("calls", _channelName,
                        new Dictionary<string, object> { ["receiverId"] = resolvedReceiverId });
                }

                await FcmService.SendCallNotificationAsync(fcmToken, myName, _channelName, IsUrgent);
                Console.WriteLine($"[CallVM] FCM call notification sent. resolvedReceiverId='{resolvedReceiverId}'");
            }
            else
            {
                Console.WriteLine("[CallVM] No FCM token found for receiver — admin not push-notified.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CallVM] FCM notify error: {ex.Message}");
        }
    }

    public async Task EndCallAsync()
    {
        await _signal.EndCallAsync(_channelName);
        _agora.LeaveChannel();
        _agora.RestoreAudio();
        _signal.StopListeningForStatus();
        ResetState();
    }

    public void ToggleMute()
    {
        _isMuted = !_isMuted;
        _agora.MuteLocalAudio(_isMuted);
        MuteIcon = _isMuted ? "🔇" : "🎙️";
    }

    public void SetSpeakerphone(bool on) => _agora.SetSpeakerphone(on);

    public void Cleanup()
    {
        _signal.OnCallStatusChanged -= HandleCallStatusChanged;
        _signal.StopListeningForStatus();

        if (_remoteJoinedHandler != null) _agora.OnRemoteUserJoined -= _remoteJoinedHandler;
        if (_remoteOfflineHandler != null) _agora.OnRemoteUserOffline -= _remoteOfflineHandler;
    }

    private void HandleCallStatusChanged(string status)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            switch (status)
            {
                case "accepted":
                    IsCalling = false;
                    IsCallActive = true;
                    StatusText = $"Connected to {ReceiverName}";
                    break;

                case "rejected":
                case "missed":
                    StatusText = status == "missed"
                        ? $"{ReceiverName} did not answer"
                        : $"{ReceiverName} declined";
                    _agora.LeaveChannel();
                    _agora.RestoreAudio();
                    _signal.StopListeningForStatus();
                    IsCalling = false;
                    break;

                case "ended":
                    _agora.LeaveChannel();
                    _agora.RestoreAudio();
                    _signal.StopListeningForStatus();
                    ResetState();
                    break;
            }
        });
    }

    private void OnRemoteJoined()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            IsCallActive = true;
            IsCalling = false;
            StatusText = $"Connected to {ReceiverName}";
        });
    }

    private void HandleRemoteLeft()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (IsCallActive) await EndCallAsync();
        });
    }

    private void ResetState()
    {
        IsCallActive = false;
        IsCalling = false;
        StatusText = "Call ended";
        _isMuted = false;
        MuteIcon = "🎙️";
        OnPropertyChanged(nameof(IsIdle));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

}