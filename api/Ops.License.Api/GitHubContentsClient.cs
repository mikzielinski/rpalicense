using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ops.License.Api;

public sealed class GitHubContentsClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly GitHubOptions _options;

    public GitHubContentsClient(HttpClient http, GitHubOptions options)
    {
        _http = http;
        _options = options;
    }

    public async Task<FileMeta> GetFileAsync(string path, CancellationToken cancellationToken = default)
    {
        var apiUrl = BuildContentsUrl(path);
        using var request = CreateRequest(HttpMethod.Get, $"{apiUrl}?ref={Uri.EscapeDataString(_options.Branch)}");
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new FileNotFoundException($"GitHub file not found: {path}");
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var sha = doc.RootElement.TryGetProperty("sha", out var shaEl) ? shaEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(sha))
        {
            throw new InvalidOperationException($"Missing sha for {path}");
        }

        string text;
        if (doc.RootElement.TryGetProperty("content", out var contentEl))
        {
            var b64 = contentEl.GetString()?.Replace("\n", "", StringComparison.Ordinal);
            text = string.IsNullOrWhiteSpace(b64)
                ? string.Empty
                : Encoding.UTF8.GetString(Convert.FromBase64String(b64));
        }
        else if (doc.RootElement.TryGetProperty("download_url", out var downloadEl))
        {
            var downloadUrl = downloadEl.GetString();
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                throw new InvalidOperationException($"Missing content for {path}");
            }

            text = await _http.GetStringAsync(downloadUrl, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            throw new InvalidOperationException($"Missing content for {path}");
        }

        return new FileMeta(sha, text);
    }

    public async Task<PublishResult> PublishTextFileAsync(
        string path,
        string content,
        string message,
        CancellationToken cancellationToken = default)
    {
        const int maxAttempts = 8;
        Exception? lastError = null;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                string? sha = null;
                try
                {
                    var existing = await GetFileAsync(path, cancellationToken).ConfigureAwait(false);
                    sha = existing.Sha;
                }
                catch (FileNotFoundException)
                {
                    sha = null;
                }

                var apiUrl = BuildContentsUrl(path);
                var payload = new
                {
                    message,
                    content = Convert.ToBase64String(Encoding.UTF8.GetBytes(content)),
                    branch = _options.Branch,
                    sha
                };

                using var request = CreateRequest(HttpMethod.Put, apiUrl);
                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
                {
                    lastError = new InvalidOperationException("HTTP 409 conflict");
                    await Task.Delay(600 * (attempt + 1), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException($"GitHub PUT failed ({(int)response.StatusCode}): {body}");
                }

                using var doc = JsonDocument.Parse(body);
                var commitSha = doc.RootElement.TryGetProperty("commit", out var commitEl) &&
                                commitEl.TryGetProperty("sha", out var commitShaEl)
                    ? commitShaEl.GetString()
                    : doc.RootElement.TryGetProperty("content", out var contentNode) &&
                      contentNode.TryGetProperty("sha", out var contentShaEl)
                        ? contentShaEl.GetString()
                        : sha;

                return new PublishResult(commitSha ?? sha ?? "ok");
            }
            catch (Exception ex) when (ex is InvalidOperationException && ex.Message.Contains("409", StringComparison.Ordinal))
            {
                lastError = ex;
                await Task.Delay(600 * (attempt + 1), cancellationToken).ConfigureAwait(false);
            }
        }

        throw lastError ?? new InvalidOperationException("Publish failed after retries");
    }

    public async Task<PublishResult> AppendJsonEntryAsync<TEntry, TDocument>(
        string path,
        TEntry entry,
        Func<TDocument> createDocument,
        Func<TDocument, List<TEntry>> getEntries,
        Action<TDocument, List<TEntry>> setEntries,
        string commitMessage,
        int maxEntries = 500,
        CancellationToken cancellationToken = default)
        where TDocument : class
    {
        const int maxAttempts = 8;
        Exception? lastError = null;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                TDocument document;
                string? sha = null;

                try
                {
                    var existing = await GetFileAsync(path, cancellationToken).ConfigureAwait(false);
                    sha = existing.Sha;
                    document = JsonSerializer.Deserialize<TDocument>(existing.Text, JsonOptions)
                               ?? createDocument();
                }
                catch (FileNotFoundException)
                {
                    document = createDocument();
                }

                var entries = getEntries(document);
                entries.Insert(0, entry);
                if (entries.Count > maxEntries)
                {
                    entries.RemoveRange(maxEntries, entries.Count - maxEntries);
                }

                setEntries(document, entries);
                var json = JsonSerializer.Serialize(document, JsonOptions) + "\n";

                var apiUrl = BuildContentsUrl(path);
                var payload = new
                {
                    message = commitMessage,
                    content = Convert.ToBase64String(Encoding.UTF8.GetBytes(json)),
                    branch = _options.Branch,
                    sha
                };

                using var request = CreateRequest(HttpMethod.Put, apiUrl);
                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
                {
                    lastError = new InvalidOperationException("HTTP 409 conflict");
                    await Task.Delay(600 * (attempt + 1), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException($"GitHub PUT failed ({(int)response.StatusCode}): {body}");
                }

                using var doc = JsonDocument.Parse(body);
                var commitSha = doc.RootElement.TryGetProperty("commit", out var commitEl) &&
                                commitEl.TryGetProperty("sha", out var commitShaEl)
                    ? commitShaEl.GetString()
                    : sha;

                return new PublishResult(commitSha ?? sha ?? "ok");
            }
            catch (Exception ex) when (ex is InvalidOperationException && ex.Message.Contains("409", StringComparison.Ordinal))
            {
                lastError = ex;
                await Task.Delay(600 * (attempt + 1), cancellationToken).ConfigureAwait(false);
            }
        }

        throw lastError ?? new InvalidOperationException("Append failed after retries");
    }

    private string BuildContentsUrl(string path)
    {
        var encodedPath = string.Join("/",
            path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Uri.EscapeDataString));
        return $"https://api.github.com/repos/{Uri.EscapeDataString(_options.Owner)}/{Uri.EscapeDataString(_options.Repo)}/contents/{encodedPath}";
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.Token);
        request.Headers.UserAgent.ParseAdd("Ops.License.Api/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        return request;
    }
}

public sealed record FileMeta(string Sha, string Text);

public sealed record PublishResult(string Sha);

public sealed class EntriesDocument<T>
{
    [JsonPropertyName("entries")]
    public List<T> Entries { get; set; } = new();
}
