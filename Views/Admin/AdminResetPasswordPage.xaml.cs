using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using SAFETY_STEPS.FireBase;

namespace SAFETY_STEPS;

/// <summary>
/// Allows an admin to reset any student's Firebase Auth password
/// using a service-account JWT (same approach as FcmService).
///
/// Firebase Admin endpoint used:
///   POST https://identitytoolkit.googleapis.com/v1/accounts:update
///   Authorization: Bearer {service_account_access_token}
///   Body: { "localId": "...", "password": "..." }
/// </summary>
public partial class AdminResetPasswordPage : ContentPage
{
    // ── Service-account credentials (same as FcmService) ─────────────────
    private static string ClientEmail => Env.ServiceAccountEmail;
    private static string PrivateKey => Env.ServiceAccountPrivateKey;
    private const string TokenUrl = "https://oauth2.googleapis.com/token";
    // cloud-platform scope is required for Identity Toolkit admin operations
    private const string Scope = "https://www.googleapis.com/auth/cloud-platform";
    private const string UpdateAccountUrl =
        "https://identitytoolkit.googleapis.com/v1/accounts:update";

    private static readonly HttpClient _http = new();

    private readonly FirestoreService _firestore;
    private readonly FirebaseAuthService _auth;

    // The Firebase UID resolved during search
    private string? _resolvedLocalId;
    private string? _resolvedStudentId;

    // ─────────────────────────────────────────────────────────────────────
    public AdminResetPasswordPage(FirestoreService firestore, FirebaseAuthService auth)
    {
        InitializeComponent();
        _firestore = firestore;
        _auth = auth;
    }

    // ── Step 1: Search ────────────────────────────────────────────────────
    private async void OnSearchClicked(object sender, EventArgs e)
    {
        var studentNumber = StudentNumberEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(studentNumber))
        {
            await DisplayAlert("Error", "Please enter a student number.", "OK");
            return;
        }

        SearchButton.IsEnabled = false;
        SearchResultLabel.IsVisible = true;
        SearchResultLabel.TextColor = Colors.Gray;
        SearchResultLabel.Text = "Searching…";
        ResetSection.IsVisible = false;
        _resolvedLocalId = null;

        try
        {
            // Indexed lookup first (".indexOn": "studentID" in database.rules.json).
            // Only if that finds nothing (e.g. rules not deployed yet, or the account
            // uses "studentNumber") do we fall back to scanning every user.
            var results = await _firestore.QueryCollectionAsync("users", ("studentID", studentNumber));

            if (results.Count == 0)
            {
                var allUsers = await _firestore.GetCollectionAsync("users");

                results = allUsers.Where(d =>
                {
                    // Support both "studentID" and "studentNumber" field names
                    if (d.TryGetValue("studentID", out var v1) &&
                        string.Equals(v1?.ToString(), studentNumber, StringComparison.OrdinalIgnoreCase))
                        return true;
                    if (d.TryGetValue("studentNumber", out var v2) &&
                        string.Equals(v2?.ToString(), studentNumber, StringComparison.OrdinalIgnoreCase))
                        return true;
                    return false;
                }).ToList();
            }

            if (results.Count == 0)
            {
                SearchResultLabel.TextColor = Colors.Red;
                SearchResultLabel.Text = "❌  No account found for that student number.";
                return;
            }

            var doc = results[0];
            _resolvedLocalId = doc.TryGetValue("docId", out var id) ? id?.ToString() : null;
            _resolvedStudentId = studentNumber;

            var name = doc.TryGetValue("name", out var n) && !string.IsNullOrWhiteSpace(n?.ToString())
                ? n.ToString()
                : studentNumber;

            SearchResultLabel.TextColor = Color.FromArgb("#2E7D32");
            SearchResultLabel.Text = $"✅  Account found: {name}";

            // Show reset section
            FoundStudentLabel.Text = $"Student: {name}  (SN: {studentNumber})";
            ResetSection.IsVisible = true;
            ResetResultLabel.IsVisible = false;
        }
        catch (Exception ex)
        {
            SearchResultLabel.TextColor = Colors.Red;
            SearchResultLabel.Text = $"Error: {ex.Message}";
        }
        finally
        {
            SearchButton.IsEnabled = true;
        }
    }

    // ── Step 2: Reset password ────────────────────────────────────────────
    private async void OnResetClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_resolvedLocalId))
        {
            await DisplayAlert("Error", "Please search for the student first.", "OK");
            return;
        }

        var newPassword = NewPasswordEntry.Text;
        var confirmPassword = ConfirmPasswordEntry.Text;

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
        {
            await DisplayAlert("Error", "Password must be at least 6 characters.", "OK");
            return;
        }

        if (newPassword != confirmPassword)
        {
            await DisplayAlert("Error", "Passwords do not match.", "OK");
            return;
        }

        ResetButton.IsEnabled = false;
        ResetResultLabel.IsVisible = true;
        ResetResultLabel.TextColor = Colors.Gray;
        ResetResultLabel.Text = "Resetting password…";

        try
        {
            var accessToken = await GetAdminAccessTokenAsync();
            if (string.IsNullOrEmpty(accessToken))
            {
                ResetResultLabel.TextColor = Colors.Red;
                ResetResultLabel.Text = "❌  Could not obtain admin token. Check service account.";
                return;
            }

            // Call Firebase Identity Toolkit admin update endpoint
            var request = new HttpRequestMessage(HttpMethod.Post, UpdateAccountUrl);
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {accessToken}");
            request.Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    localId = _resolvedLocalId,
                    password = newPassword
                }),
                Encoding.UTF8,
                "application/json");

            var response = await _http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                ResetResultLabel.TextColor = Color.FromArgb("#2E7D32");
                ResetResultLabel.Text = "✅  Password reset successfully!";

                await DisplayAlert(
                    "Success",
                    $"Password for SN {_resolvedStudentId} has been reset.\n\n" +
                    "Tell the student to log in with the new temporary password and change it.",
                    "OK");

                // Clear fields
                NewPasswordEntry.Text = "";
                ConfirmPasswordEntry.Text = "";
            }
            else
            {
                Console.WriteLine($"[ResetPwd] Failed: {body}");
                ResetResultLabel.TextColor = Colors.Red;
                ResetResultLabel.Text = "❌  Reset failed. See console for details.";
            }
        }
        catch (Exception ex)
        {
            ResetResultLabel.TextColor = Colors.Red;
            ResetResultLabel.Text = $"Error: {ex.Message}";
        }
        finally
        {
            ResetButton.IsEnabled = true;
        }
    }

    // ── Service-account JWT → OAuth2 access token ─────────────────────────
    private static async Task<string?> GetAdminAccessTokenAsync()
    {
        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var header = Base64UrlEncode(
                JsonSerializer.Serialize(new { alg = "RS256", typ = "JWT" }));
            var claims = Base64UrlEncode(
                JsonSerializer.Serialize(new
                {
                    iss = ClientEmail,
                    scope = Scope,
                    aud = TokenUrl,
                    iat = now,
                    exp = now + 3600
                }));

            var unsigned = $"{header}.{claims}";
            var signature = SignWithRsa(unsigned);
            var jwt = $"{unsigned}.{signature}";

            var form = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>(
                    "grant_type", "urn:ietf:params:oauth:grant-type:jwt-bearer"),
                new KeyValuePair<string, string>("assertion", jwt)
            });

            var resp = await _http.PostAsync(TokenUrl, form);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
            return json.GetProperty("access_token").GetString();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AdminResetPwd] Token error: {ex.Message}");
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
        var sig = rsa.SignData(
            Encoding.UTF8.GetBytes(data),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return Base64UrlEncode(sig);
    }

    private static string Base64UrlEncode(string input) =>
        Base64UrlEncode(Encoding.UTF8.GetBytes(input));

    private static string Base64UrlEncode(byte[] input) =>
        Convert.ToBase64String(input).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}