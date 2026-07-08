using System.Net.Http.Headers;
namespace UiPath.System.RoboticSecurity;

internal sealed class CatalogClient
{
    private readonly HttpClient _http;

    internal CatalogClient(HttpClient http)
    {
        _http = http;
    }

    internal async Task<string> LoadRawAsync(string sourceUrl, string? sourceToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, sourceUrl);
        request.Headers.UserAgent.ParseAdd("Ops-Runtime-Seed/1.0");
        if (!string.IsNullOrWhiteSpace(sourceToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sourceToken);
        }

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }
}
