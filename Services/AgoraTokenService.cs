using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SAFETY_STEPS.FireBase;

namespace SAFETY_STEPS.Services;

public class AgoraTokenService
{
    private readonly HttpClient _httpClient;
    private readonly FirebaseAuthService _auth;

    // ONLY the base URL here
    private const string ServerUrl =
        "https://agoratokenserver-42h3.onrender.com";

    // Render's free tier sleeps when idle and can take ~60s to wake up.
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

    public AgoraTokenService(FirebaseAuthService auth)
    {
        _auth = auth;

#if ANDROID
        var handler = new Xamarin.Android.Net.AndroidMessageHandler();
#else
        var handler = new HttpClientHandler();
#endif

        _httpClient = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan // per-request timeouts below
        };
    }

    /// <summary>
    /// Fire-and-forget ping that wakes the token server so the first call
    /// after a quiet period doesn't wait on a cold start.
    /// </summary>
    public async Task WarmUpAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(RequestTimeout);
            using var response = await _httpClient.GetAsync($"{ServerUrl}/health", cts.Token);
            Console.WriteLine($"[Token] Warm-up: {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Token] Warm-up failed: {ex.Message}");
        }
    }

    public async Task<string?> GetTokenAsync(string channelName, uint uid = 0)
    {
        var url = $"{ServerUrl}/Token?channelName={Uri.EscapeDataString(channelName)}&uid={uid}";
        Console.WriteLine($"[Token] Request URL: {url}");

        // Firebase ID tokens last 1 hour — refresh if this one may be stale.
        if ((DateTime.UtcNow - _auth.LastTokenRefresh).TotalMinutes >= 50)
            await _auth.RefreshTokenAsync();

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                using var cts = new CancellationTokenSource(RequestTimeout);
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _auth.IdToken);

                using var response = await _httpClient.SendAsync(request, cts.Token);

                if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 1)
                {
                    Console.WriteLine("[Token] 401 — refreshing Firebase ID token and retrying");
                    await _auth.RefreshTokenAsync();
                    continue;
                }

                response.EnsureSuccessStatusCode();
                var body = await response.Content.ReadFromJsonAsync<TokenResponse>(cts.Token);

                Console.WriteLine("[Token] Success");
                return body?.Token;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"[Token] Request timed out (attempt {attempt})");
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"[Token] HTTP error (attempt {attempt}): {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Token] Unexpected error: {ex.Message}");
                return null;
            }
        }

        return null;
    }
}

public class TokenResponse
{
    public string? Token { get; set; }
}
