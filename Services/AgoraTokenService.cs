using System.Net.Http.Json;

namespace SAFETY_STEPS.Services;

public class AgoraTokenService
{
    private readonly HttpClient _httpClient;

    // ONLY the base URL here
    private const string ServerUrl =
        "https://agoratokenserver-42h3.onrender.com";

    public AgoraTokenService()
    {
#if ANDROID
        var handler = new Xamarin.Android.Net.AndroidMessageHandler();
#else
        var handler = new HttpClientHandler();
#endif

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public async Task<string?> GetTokenAsync(string channelName, uint uid = 0)
    {
        // Build the full endpoint correctly
        var url = $"{ServerUrl}/Token?channelName={channelName}&uid={uid}";

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            Console.WriteLine($"[Token] Request URL: {url}");

            var response = await _httpClient.GetFromJsonAsync<TokenResponse>(url, cts.Token);

            Console.WriteLine($"[Token] Success: {response?.Token}");

            return response?.Token;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[Token] Request timed out");
            return null;
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"[Token] HTTP error: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Token] Unexpected error: {ex.Message}");
            return null;
        }
    }
}

public class TokenResponse
{
    public string? Token { get; set; }
}