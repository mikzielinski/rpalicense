using System.Net;
using System.Text;
using System.Text.Json;

namespace Ops.License.Api.Tests;

internal sealed class FakeGitHubHandler : HttpMessageHandler
{
    private readonly Dictionary<string, (string Sha, string Text)> _files = new(StringComparer.OrdinalIgnoreCase);

    internal void SetTextFile(string path, string text, string sha = "test-sha")
    {
        _files[path.Trim().TrimStart('/')] = (sha, text);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri?.ToString() ?? string.Empty;

        if (request.Method == HttpMethod.Get && url.Contains("/contents/", StringComparison.Ordinal))
        {
            var path = ExtractPath(url);
            if (_files.TryGetValue(path, out var file))
            {
                var payload = JsonSerializer.Serialize(new
                {
                    sha = file.Sha,
                    content = Convert.ToBase64String(Encoding.UTF8.GetBytes(file.Text))
                });
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, payload));
            }

            return Task.FromResult(JsonResponse(HttpStatusCode.NotFound, "{\"message\":\"Not Found\"}"));
        }

        if (request.Method == HttpMethod.Put && url.Contains("/contents/", StringComparison.Ordinal))
        {
            var path = ExtractPath(url);
            var body = request.Content is null
                ? string.Empty
                : request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(body);
            var contentB64 = doc.RootElement.GetProperty("content").GetString() ?? string.Empty;
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(contentB64.Replace("\n", "", StringComparison.Ordinal)));
            _files[path] = ("published-sha", text);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, "{\"commit\":{\"sha\":\"published-sha\"}}"));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static string ExtractPath(string url)
    {
        var marker = "/contents/";
        var idx = url.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0)
        {
            return string.Empty;
        }

        var tail = url[(idx + marker.Length)..];
        var q = tail.IndexOf('?', StringComparison.Ordinal);
        if (q >= 0)
        {
            tail = tail[..q];
        }

        return Uri.UnescapeDataString(tail);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
