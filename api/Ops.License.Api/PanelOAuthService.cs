using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ops.License.Api;

public sealed class PanelOAuthService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IPanelOAuthConfigStore _configStore;
    private readonly ServerSettings _settings;
    private readonly HttpClient _http;

    public PanelOAuthService(IPanelOAuthConfigStore configStore, ServerSettings settings, HttpClient http)
    {
        _configStore = configStore;
        _settings = settings;
        _http = http;
    }

    public async Task<bool> IsGithubEnabledAsync(CancellationToken cancellationToken = default)
    {
        var config = await _configStore.GetRuntimeConfigAsync(cancellationToken).ConfigureAwait(false);
        return config.GithubEnabled;
    }

    public async Task<bool> IsGoogleEnabledAsync(CancellationToken cancellationToken = default)
    {
        var config = await _configStore.GetRuntimeConfigAsync(cancellationToken).ConfigureAwait(false);
        return config.GoogleEnabled;
    }

    public async Task<string> BuildGithubAuthorizeUrlAsync(CancellationToken cancellationToken = default)
    {
        var config = await _configStore.GetRuntimeConfigAsync(cancellationToken).ConfigureAwait(false);
        var state = IssueState("github");
        var redirectUri = GithubCallbackUrl(config);
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = config.Github.ClientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = "read:user user:email",
            ["state"] = state
        };
        return $"https://github.com/login/oauth/authorize?{BuildQuery(query)}";
    }

    public async Task<string> BuildGoogleAuthorizeUrlAsync(CancellationToken cancellationToken = default)
    {
        var config = await _configStore.GetRuntimeConfigAsync(cancellationToken).ConfigureAwait(false);
        var state = IssueState("google");
        var redirectUri = GoogleCallbackUrl(config);
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = config.Google.ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = "openid email profile",
            ["state"] = state,
            ["access_type"] = "online",
            ["prompt"] = "select_account"
        };
        return $"https://accounts.google.com/o/oauth2/v2/auth?{BuildQuery(query)}";
    }

    public bool TryValidateState(string? state, string provider, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(state))
        {
            error = "missing_state";
            return false;
        }

        var parts = state.Split('.', 2);
        if (parts.Length != 2)
        {
            error = "invalid_state";
            return false;
        }

        try
        {
            var expectedSig = Sign(parts[0]);
            var actualSig = FromBase64Url(parts[1]);
            if (!CryptographicOperations.FixedTimeEquals(expectedSig, actualSig))
            {
                error = "invalid_state";
                return false;
            }

            var json = Encoding.UTF8.GetString(FromBase64Url(parts[0]));
            var payload = JsonSerializer.Deserialize<OAuthStatePayload>(json, JsonOptions);
            if (payload is null ||
                !string.Equals(payload.Provider, provider, StringComparison.Ordinal) ||
                payload.Exp <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            {
                error = "expired_state";
                return false;
            }

            return true;
        }
        catch
        {
            error = "invalid_state";
            return false;
        }
    }

    public async Task<OAuthIdentity?> ExchangeGithubAsync(string code, CancellationToken cancellationToken = default)
    {
        var config = await _configStore.GetRuntimeConfigAsync(cancellationToken).ConfigureAwait(false);
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token");
        tokenRequest.Headers.Accept.ParseAdd("application/json");
        tokenRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = config.Github.ClientId,
            ["client_secret"] = config.Github.ClientSecret,
            ["code"] = code,
            ["redirect_uri"] = GithubCallbackUrl(config)
        });

        using var tokenResponse = await _http.SendAsync(tokenRequest, cancellationToken).ConfigureAwait(false);
        var tokenBody = await tokenResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!tokenResponse.IsSuccessStatusCode)
        {
            return null;
        }

        using var tokenDoc = JsonDocument.Parse(tokenBody);
        var accessToken = tokenDoc.RootElement.TryGetProperty("access_token", out var tokenEl)
            ? tokenEl.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        using var userRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
        userRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        userRequest.Headers.UserAgent.ParseAdd("Ops.License.Api/1.0");
        userRequest.Headers.Accept.ParseAdd("application/json");

        using var userResponse = await _http.SendAsync(userRequest, cancellationToken).ConfigureAwait(false);
        var userBody = await userResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!userResponse.IsSuccessStatusCode)
        {
            return null;
        }

        using var userDoc = JsonDocument.Parse(userBody);
        var login = userDoc.RootElement.TryGetProperty("login", out var loginEl) ? loginEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(login))
        {
            return null;
        }

        return new OAuthIdentity("github", login.Trim(), null);
    }

    public async Task<OAuthIdentity?> ExchangeGoogleAsync(string code, CancellationToken cancellationToken = default)
    {
        var config = await _configStore.GetRuntimeConfigAsync(cancellationToken).ConfigureAwait(false);
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token");
        tokenRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = config.Google.ClientId,
            ["client_secret"] = config.Google.ClientSecret,
            ["code"] = code,
            ["redirect_uri"] = GoogleCallbackUrl(config),
            ["grant_type"] = "authorization_code"
        });

        using var tokenResponse = await _http.SendAsync(tokenRequest, cancellationToken).ConfigureAwait(false);
        var tokenBody = await tokenResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!tokenResponse.IsSuccessStatusCode)
        {
            return null;
        }

        using var tokenDoc = JsonDocument.Parse(tokenBody);
        var accessToken = tokenDoc.RootElement.TryGetProperty("access_token", out var tokenEl)
            ? tokenEl.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        using var userRequest = new HttpRequestMessage(HttpMethod.Get, "https://openidconnect.googleapis.com/v1/userinfo");
        userRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var userResponse = await _http.SendAsync(userRequest, cancellationToken).ConfigureAwait(false);
        var userBody = await userResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!userResponse.IsSuccessStatusCode)
        {
            return null;
        }

        using var userDoc = JsonDocument.Parse(userBody);
        var email = userDoc.RootElement.TryGetProperty("email", out var emailEl) ? emailEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        return new OAuthIdentity("google", email.Trim().ToLowerInvariant(), email.Trim().ToLowerInvariant());
    }

    public async Task<string> BuildPanelSuccessRedirectAsync(PanelLoginResponse login, CancellationToken cancellationToken = default)
    {
        var config = await _configStore.GetRuntimeConfigAsync(cancellationToken).ConfigureAwait(false);
        return $"{NormalizePanelUrl(config)}#oauth=success&sessionToken={Uri.EscapeDataString(login.SessionToken)}" +
               $"&username={Uri.EscapeDataString(login.Username)}&isAdmin={(login.IsAdmin ? "1" : "0")}" +
               $"&expiresAt={Uri.EscapeDataString(login.ExpiresAt)}";
    }

    public async Task<string> BuildPanelErrorRedirectAsync(string error, CancellationToken cancellationToken = default)
    {
        var config = await _configStore.GetRuntimeConfigAsync(cancellationToken).ConfigureAwait(false);
        return $"{NormalizePanelUrl(config)}#oauth=error&reason={Uri.EscapeDataString(error)}";
    }

    private static string GithubCallbackUrl(PanelOAuthRuntimeConfig config) =>
        $"{NormalizeApiUrl(config)}/v1/panel/oauth/github/callback";

    private static string GoogleCallbackUrl(PanelOAuthRuntimeConfig config) =>
        $"{NormalizeApiUrl(config)}/v1/panel/oauth/google/callback";

    private static string NormalizePanelUrl(PanelOAuthRuntimeConfig config) => config.PanelPublicUrl.TrimEnd('/');

    private static string NormalizeApiUrl(PanelOAuthRuntimeConfig config) => config.ApiPublicUrl.TrimEnd('/');

    private string IssueState(string provider)
    {
        var payload = new OAuthStatePayload
        {
            Provider = provider,
            Nonce = Guid.NewGuid().ToString("N"),
            Exp = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds()
        };
        var payloadPart = ToBase64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOptions)));
        var sigPart = ToBase64Url(Sign(payloadPart));
        return $"{payloadPart}.{sigPart}";
    }

    private byte[] Sign(string payloadPart)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_settings.SessionSigningKey));
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadPart));
    }

    private static string BuildQuery(Dictionary<string, string?> values) =>
        string.Join("&", values
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}"));

    private static string ToBase64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        var pad = (4 - (s.Length % 4)) % 4;
        return Convert.FromBase64String(s + new string('=', pad));
    }

    private sealed class OAuthStatePayload
    {
        [JsonPropertyName("provider")]
        public string Provider { get; set; } = string.Empty;

        [JsonPropertyName("nonce")]
        public string Nonce { get; set; } = string.Empty;

        [JsonPropertyName("exp")]
        public long Exp { get; set; }
    }
}

public sealed record OAuthIdentity(string Provider, string Key, string? Email);
