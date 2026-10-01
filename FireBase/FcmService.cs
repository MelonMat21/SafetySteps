using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SAFETY_STEPS.FireBase;

public static class FcmService
{
    private static string ClientEmail => Env.ServiceAccountEmail;
    private static string PrivateKey => Env.ServiceAccountPrivateKey;
    private const string ProjectId = "safetysteps-f4f77";
    private const string FcmV1Url = $"https://fcm.googleapis.com/v1/projects/{ProjectId}/messages:send";
    private const string Scope = "https://www.googleapis.com/auth/firebase.messaging";
    private const string TokenUrl = "https://oauth2.googleapis.com/token";

    private static readonly HttpClient _http = new();

    // ─────────────────────────────────────────────────────────────────────
    // PUBLIC API
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Red banner — plays sound + vibration.</summary>
    public static Task SendHighPriorityAlertAsync(
        string deviceToken, string title, string body, string reportDocId = "")
        => SendAsync(deviceToken, title, body,
            androidPriority: "high",
            channelId: "high_priority_channel",
            extraData: new Dictionary<string, string>
            {
                ["type"] = "HIGH_PRIORITY_ALERT",
                ["reportDocId"] = reportDocId
            });

    /// <summary>Normal alert — also notifies admin, just lower urgency.</summary>
    public static Task SendLowPriorityAlertAsync(
        string deviceToken, string title, string body, string reportDocId = "")
        => SendAsync(deviceToken, title, body,
            androidPriority: "normal",
            channelId: "default_channel",
            extraData: new Dictionary<string, string>
            {
                ["type"] = "LOW_PRIORITY_ALERT",
                ["reportDocId"] = reportDocId
            });

    /// <summary>Rings admin's phone like an incoming call.</summary>
    public static Task SendCallNotificationAsync(
        string deviceToken, string callerName, string channelName, bool isUrgent)
        => SendAsync(deviceToken,
            title: isUrgent ? "Urgent Incoming Call" : "Incoming Call",
            body: isUrgent ? $"{callerName} is calling (URGENT)" : $"{callerName} is calling you",
            androidPriority: isUrgent ? "high" : "normal",
            channelId: isUrgent ? "call_channel" : "default_channel",
            extraData: new Dictionary<string, string>
            {
                ["type"] = "INCOMING_CALL",
                ["callerName"] = callerName,
                ["channelName"] = channelName,
                ["isUrgent"] = isUrgent ? "true" : "false"
            });

    // ─────────────────────────────────────────────────────────────────────
    // PRIVATE
    // ─────────────────────────────────────────────────────────────────────

    private static async Task SendAsync(
        string deviceToken, string title, string body,
        string androidPriority, string channelId,
        Dictionary<string, string> extraData)
    {
        try
        {
            var accessToken = await GetAccessTokenAsync();
            if (string.IsNullOrEmpty(accessToken))
            {
                Console.WriteLine("[FcmService] Failed to get access token.");
                return;
            }

            // Build data dictionary (string→string required by FCM data payload)
            var data = new Dictionary<string, string>
            {
                ["title"] = title,
                ["body"] = body
            };
            foreach (var kv in extraData) data[kv.Key] = kv.Value;

            var payload = new
            {
                message = new
                {
                    token = deviceToken,
                    // notification block → shown automatically when app is BACKGROUND
                    notification = new { title, body },
                    // android overrides
                    android = new
                    {
                        priority = androidPriority,
                        notification = new
                        {
                            channelId,
                            sound = "default",
                            defaultVibrateTimings = true
                        }
                    },
                    // data block → always delivered to OnMessageReceived (foreground + background)
                    data
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, FcmV1Url);
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {accessToken}");
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var response = await _http.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                Console.WriteLine($"[FcmService] Failed ({channelId}): {responseBody}");
            else
                Console.WriteLine($"[FcmService] Sent OK ({channelId}): {responseBody}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FcmService] Error: {ex.Message}");
        }
    }

    private static async Task<string?> GetAccessTokenAsync()
    {
        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var header = Base64UrlEncode(JsonSerializer.Serialize(new { alg = "RS256", typ = "JWT" }));
            var claims = Base64UrlEncode(JsonSerializer.Serialize(new
            {
                iss = ClientEmail,
                scope = Scope,
                aud = TokenUrl,
                iat = now,
                exp = now + 3600
            }));

            var unsignedJwt = $"{header}.{claims}";
            var signature = SignWithRsa(unsignedJwt);
            var jwt = $"{unsignedJwt}.{signature}";

            var formData = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type",
                    "urn:ietf:params:oauth:grant-type:jwt-bearer"),
                new KeyValuePair<string, string>("assertion", jwt)
            });

            var response = await _http.PostAsync(TokenUrl, formData);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            return json.GetProperty("access_token").GetString();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FcmService] Token error: {ex.Message}");
            return null;
        }
    }

    private static string SignWithRsa(string data)
    {
        var keyPem = PrivateKey
            .Replace("-----BEGIN PRIVATE KEY-----", "")
            .Replace("-----END PRIVATE KEY-----", "")
            .Replace("\n", "")
            .Trim();

        using var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(keyPem), out _);
        var signature = rsa.SignData(
            Encoding.UTF8.GetBytes(data),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return Base64UrlEncode(signature);
    }

    private static string Base64UrlEncode(string input) =>
        Base64UrlEncode(Encoding.UTF8.GetBytes(input));

    private static string Base64UrlEncode(byte[] input) =>
        Convert.ToBase64String(input).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}