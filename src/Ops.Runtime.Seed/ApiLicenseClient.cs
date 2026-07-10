using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UiPath.System.RoboticSecurity;

internal sealed class ApiLicenseClient
{
    private static HttpClient Http = CreateHttpClient();

    private static string? _runtimeSession;

    internal static string? RuntimeSession => _runtimeSession;

    internal static void ClearRuntimeSession() => _runtimeSession = null;

    internal static void UseHttpHandlerForTesting(HttpMessageHandler handler)
    {
        Http = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    internal static void ResetHttpClientForTesting() => Http = CreateHttpClient();

    private static HttpClient CreateHttpClient() =>
        new() { Timeout = TimeSpan.FromSeconds(15) };

    internal static async Task<RuntimeProfile> AuthorizeAsync(
        string runtimeToken,
        string machine,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = BootstrapperSettings.ApiUrl?.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("boot-0x60");
        }

        var clientNonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var challenge = await PostJsonAsync<ChallengeResponse>(
            $"{baseUrl}/v1/runtime/challenge",
            new { machine },
            null,
            cancellationToken).ConfigureAwait(false);

        var proof = HandshakeProof.ComputeRuntimeProof(
            runtimeToken,
            BootstrapperSettings.Pepper,
            challenge.ChallengeId,
            challenge.ServerNonce,
            clientNonce,
            machine,
            runtimeToken);

        var authorize = await PostJsonAsync<AuthorizeResponse>(
            $"{baseUrl}/v1/runtime/authorize",
            new
            {
                tokenId = runtimeToken,
                machine,
                challengeId = challenge.ChallengeId,
                clientNonce,
                proof
            },
            null,
            cancellationToken).ConfigureAwait(false);

        if (!authorize.Success || authorize.Profile is null)
        {
            throw new InvalidOperationException(authorize.Code ?? "boot-0xFF");
        }

        _runtimeSession = authorize.SessionToken;
        return authorize.Profile;
    }

    internal static async Task TryReportTelemetryAsync(ValidationSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var baseUrl = BootstrapperSettings.ApiUrl?.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return;
        }

        var headers = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(_runtimeSession))
        {
            headers["Authorization"] = $"Bearer {_runtimeSession}";
        }

        _ = await PostJsonAsync<object>(
            $"{baseUrl}/v1/runtime/telemetry",
            new
            {
                tokenId = snapshot.TokenId,
                machine = snapshot.Machine,
                code = snapshot.Code,
                success = snapshot.Success,
                usedCache = snapshot.UsedCache,
                notes = snapshot.Notes
            },
            headers,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T> PostJsonAsync<T>(
        string url,
        object body,
        Dictionary<string, string>? headers,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.UserAgent.ParseAdd("UiPath.System.RoboticSecurity/1.0");
        if (headers is not null)
        {
            foreach (var (key, value) in headers)
            {
                if (key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                {
                    request.Headers.Authorization = AuthenticationHeaderValue.Parse(value);
                }
                else
                {
                    request.Headers.TryAddWithoutValidation(key, value);
                }
            }
        }

        request.Content = new StringContent(JsonSerializer.Serialize(body, Json.Options), Encoding.UTF8, "application/json");
        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"boot-0x61:{(int)response.StatusCode}");
        }

        return JsonSerializer.Deserialize<T>(text, Json.Options)
            ?? throw new InvalidOperationException("boot-0x62");
    }

    private sealed class ChallengeResponse
    {
        [JsonPropertyName("challengeId")]
        public string ChallengeId { get; set; } = string.Empty;

        [JsonPropertyName("serverNonce")]
        public string ServerNonce { get; set; } = string.Empty;
    }

    private sealed class AuthorizeResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("sessionToken")]
        public string? SessionToken { get; set; }

        [JsonPropertyName("profile")]
        public RuntimeProfile? Profile { get; set; }
    }
}
