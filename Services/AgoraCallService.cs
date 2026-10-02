using Android.Content;
using Android.Media;
using DT.Xamarin.Agora;
using Microsoft.Maui.Storage;

namespace SAFETY_STEPS.Services;

public class AgoraCallService
{
    private RtcEngine? _engine;
    private readonly AgoraRtcHandler _handler;
    private AudioManager? _audioManager;
    private Context? _context;
    private readonly AgoraTokenService _tokenService;
    private string? _currentChannel;

    // ── Events ──────────────────────────────────────────────────────────
    public event Action<uint>? OnRemoteUserJoined;
    public event Action<uint>? OnRemoteUserOffline;
    public event Action<string, uint>? OnJoinChannelSuccess;
    public event Action<uint>? OnFirstRemoteAudioReceived;

    public AgoraCallService(AgoraTokenService tokenService)
    {
        _tokenService = tokenService;
        _handler = new AgoraRtcHandler();
        // Agora tokens expire after 1 hour — fetch a fresh one before the call drops.
        _handler.TokenExpiring += () => _ = RenewTokenAsync();
        _handler.RemoteUserJoined += uid => OnRemoteUserJoined?.Invoke(uid);
        _handler.RemoteUserOffline += uid => OnRemoteUserOffline?.Invoke(uid);
        _handler.JoinSuccess += (ch, uid) => OnJoinChannelSuccess?.Invoke(ch, uid);
        _handler.FirstRemoteAudioReceived += uid => OnFirstRemoteAudioReceived?.Invoke(uid);
        // Re-apply speakerphone once the audio session is confirmed open.
        _handler.SpeakerphoneReady += () =>
        {
            _engine?.SetEnableSpeakerphone(true);
            if (_audioManager != null) _audioManager.SpeakerphoneOn = true;
            Console.WriteLine("[Agora] Speakerphone re-applied after OnJoinChannelSuccess");
        };
    }

    // ── Init ─────────────────────────────────────────────────────────────
    public void Initialize(Context context)
    {
        if (_engine != null) return;

        try
        {
            _context = context;
            _audioManager = context.GetSystemService(Context.AudioService) as AudioManager;


            _engine = RtcEngine.Create(context, AppConstants.AgoraAppId, _handler);

            _engine.SetChannelProfile(Constants.ChannelProfileCommunication);
            // AudioProfileSpeechStandard (1) + AudioScenarioChatRoomEntertainment (4) is the
            // correct pairing for ChannelProfileCommunication.
            // AudioProfileMusicHighQuality uses stereo/high-bitrate encoding that
            // is incompatible with the mono voice codec forced by Communication
            // mode — it connects but audio negotiation silently fails.
            _engine.SetAudioProfile(Constants.AudioProfileSpeechStandard, 0); // 4 = AudioScenarioChatRoomEntertainment
            _engine.DisableVideo();                      // voice-only call — no video pipeline
            _engine.EnableAudio();
            _engine.MuteAllRemoteAudioStreams(false);    // ensure remote audio is unmuted on init
            _engine.AdjustPlaybackSignalVolume(400);
            _engine.AdjustRecordingSignalVolume(400);

            Console.WriteLine("[Agora] Initialized OK");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Agora] Init FAILED: {ex.Message}");
        }
    }

    // ── Channel ──────────────────────────────────────────────────────────
    public void JoinChannel(string channelName, string token = "")
    {
        if (_engine == null)
        {
            Console.WriteLine("[Agora] ERROR: JoinChannel called before Initialize()");
            return;
        }

        if (_audioManager != null)
        {
            _audioManager.Mode = Mode.InCommunication;
            _audioManager.SpeakerphoneOn = true;
            Console.WriteLine("[Agora] AudioManager  mode=InCommunication  speaker=ON");
        }

        _engine.MuteLocalAudioStream(false);
        _engine.SetEnableSpeakerphone(true);


        _currentChannel = channelName;
        int result = _engine.JoinChannel(token, channelName, "", 0);

        Console.WriteLine(result == 0
            ? $"[Agora] JoinChannel OK  channel={channelName}"
            : $"[Agora] JoinChannel FAILED  result={result}  channel={channelName}");
    }

    private async Task RenewTokenAsync()
    {
        var channel = _currentChannel;
        if (_engine == null || channel == null) return;

        var token = await _tokenService.GetTokenAsync(channel, 0);
        if (string.IsNullOrEmpty(token) || channel != _currentChannel)
        {
            Console.WriteLine("[Agora] Token renewal failed or call ended");
            return;
        }

        int result = _engine.RenewToken(token);
        Console.WriteLine($"[Agora] RenewToken result={result}");
    }

    public void LeaveChannel()
    {
        _currentChannel = null;
        _engine?.LeaveChannel();
        Console.WriteLine("[Agora] LeaveChannel");
    }

    public void RestoreAudio()
    {
        if (_audioManager != null)
        {
            _audioManager.Mode = Mode.Normal;
            _audioManager.SpeakerphoneOn = false;
            Console.WriteLine("[Agora] AudioManager restored to Normal");
        }
    }

    // ── Controls ─────────────────────────────────────────────────────────
    public void MuteLocalAudio(bool mute)
    {
        _engine?.MuteLocalAudioStream(mute);
        Console.WriteLine($"[Agora] MuteLocal={mute}");
    }

    public void SetSpeakerphone(bool on)
    {
        _engine?.SetEnableSpeakerphone(on);
        Console.WriteLine($"[Agora] Speakerphone={on}");
    }

    // ── Cleanup ──────────────────────────────────────────────────────────
    public void Destroy()
    {
        LeaveChannel();
        RtcEngine.Destroy();
        _engine = null;
    }
}

// ── Event handler ────────────────────────────────────────────────────────
internal class AgoraRtcHandler : IRtcEngineEventHandler
{
    public event Action<uint>? RemoteUserJoined;
    public event Action<uint>? RemoteUserOffline;
    public event Action<string, uint>? JoinSuccess;
    public event Action<uint>? FirstRemoteAudioReceived;
    public event Action? SpeakerphoneReady;   // fired after OnJoinChannelSuccess
    public event Action? TokenExpiring;       // token about to expire, or already expired

    public override void OnTokenPrivilegeWillExpire(string token)
    {
        Console.WriteLine("[Agora] Token will expire in 30s — renewing");
        TokenExpiring?.Invoke();
    }

    public override void OnRequestToken()
    {
        Console.WriteLine("[Agora] Token expired — requesting a new one");
        TokenExpiring?.Invoke();
    }

    public override void OnJoinChannelSuccess(string channel, int uid, int elapsed)
    {
        Console.WriteLine($"[Agora] ✅ OnJoinChannelSuccess  channel={channel}  myUid={uid}");
        // Re-apply speakerphone here — the audio session is fully open at this
        // point, so the call to SetEnableSpeakerphone in JoinChannel() may have
        // fired before the engine was ready on some devices.
        SpeakerphoneReady?.Invoke();
        JoinSuccess?.Invoke(channel, (uint)uid);
    }

    public override void OnUserJoined(int uid, int elapsed)
    {
        Console.WriteLine($"[Agora] ✅ OnUserJoined  remoteUid={uid}  → remote user is in the channel");
        RemoteUserJoined?.Invoke((uint)uid);
    }

    public override void OnFirstRemoteAudioDecoded(int uid, int elapsed)
    {
        Console.WriteLine($"[Agora] ✅ OnFirstRemoteAudioDecoded  remoteUid={uid}  → AUDIO IS FLOWING");
        FirstRemoteAudioReceived?.Invoke((uint)uid);
    }

    public override void OnUserOffline(int uid, int reason)
    {
        Console.WriteLine($"[Agora] OnUserOffline  uid={uid}  reason={reason}");
        RemoteUserOffline?.Invoke((uint)uid);
    }

    public override void OnLeaveChannel(RtcStats stats)
        => Console.WriteLine("[Agora] OnLeaveChannel");

    public override void OnError(int err)
    {
        var meaning = err switch
        {
            1 => "General error",
            2 => "Invalid argument",
            7 => "SDK not initialized — call Initialize() first",
            17 => "Already in a channel",
            101 => "Invalid App ID — also check RTC token: Agora returns 101 if token was built with a different App ID than RtcEngine.Create (align the token server's Agora__AppId on Render with AGORA_APP_ID in .env.local)",
            110 => "Invalid token — check the token server's Agora__AppCertificate matches the Agora Console",
            111 => "Token expired",
            112 => "Token invalid — uid mismatch",
            113 => "Not in channel",
            119 => "Switch channel failed",
            _ => "Unknown error"
        };
        Console.WriteLine($"[Agora] ❌ ERROR {err}: {meaning}");
    }

    public override void OnWarning(int warn)
        => Console.WriteLine($"[Agora] ⚠️ Warning {warn}");

    public override void OnConnectionStateChanged(int state, int reason)
        => Console.WriteLine($"[Agora] Connection state={state}  reason={reason}");
}