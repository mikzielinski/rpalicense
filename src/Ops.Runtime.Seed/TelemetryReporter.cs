using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UiPath.System.RoboticSecurity;

internal static class TelemetryReporter
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(12)
    };

    private static readonly object Gate = new();
    private static string? _lastFingerprint;
    private static DateTimeOffset _lastReportedAt = DateTimeOffset.MinValue;

    internal static void TryReport(ValidationSnapshot snapshot)
    {
        if (!BootstrapperSettings.TelemetryEnabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(snapshot.TokenId))
        {
            return;
        }

        var fingerprint = $"{snapshot.TokenId}|{snapshot.Machine}|{snapshot.Code}|{snapshot.Success}|{snapshot.UsedCache}";
        lock (Gate)
        {
            var now = DateTimeOffset.UtcNow;
            if (fingerprint == _lastFingerprint && now - _lastReportedAt < TimeSpan.FromSeconds(30))
            {
                return;
            }

            _lastFingerprint = fingerprint;
            _lastReportedAt = now;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(BootstrapperSettings.TelemetryApiUrl))
                {
                    await ReportToLicenseApiAsync(snapshot).ConfigureAwait(false);
                }
                else if (BootstrapperSettings.TelemetryUseDispatch)
                {
                    await ReportToActionsDispatchAsync(snapshot).ConfigureAwait(false);
                }
                else
                {
                    await ReportToGitHubAsync(snapshot).ConfigureAwait(false);
                }
            }
            catch
            {
                // Best-effort telemetry.
            }
        });
    }

    private static async Task ReportToLicenseApiAsync(ValidationSnapshot snapshot)
    {
        var baseUrl = BootstrapperSettings.TelemetryApiUrl?.Trim().TrimEnd('/');
        var apiKey = BootstrapperSettings.TelemetryApiKey;
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey))
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new RobotEventRecord
        {
            AtUtc = DateTimeOffset.UtcNow.ToString("O"),
            TokenId = snapshot.TokenId,
            Machine = snapshot.Machine,
            Code = snapshot.Code,
            Success = snapshot.Success,
            UsedCache = snapshot.UsedCache,
            Notes = snapshot.Notes,
            ProcessName = Process.GetCurrentProcess().ProcessName,
            WindowsIdentity = Environment.UserName
        }, Json.Options);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/telemetry");
        request.Headers.Add("X-Api-Key", apiKey);
        request.Headers.UserAgent.ParseAdd("UiPath.System.RoboticSecurity/1.0");
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await Http.SendAsync(request).ConfigureAwait(false);
        _ = response.IsSuccessStatusCode;
    }

    private static async Task ReportToActionsDispatchAsync(ValidationSnapshot snapshot)
    {
        var token = BootstrapperSettings.DispatchGitHubToken;
        var owner = BootstrapperSettings.DispatchGitHubOwner;
        var repo = BootstrapperSettings.DispatchGitHubRepo;
        var apiKey = BootstrapperSettings.TelemetryApiKey;
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(apiKey))
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            event_type = "robot-telemetry",
            client_payload = new
            {
                apiKey,
                @event = new RobotEventRecord
                {
                    AtUtc = DateTimeOffset.UtcNow.ToString("O"),
                    TokenId = snapshot.TokenId,
                    Machine = snapshot.Machine,
                    Code = snapshot.Code,
                    Success = snapshot.Success,
                    UsedCache = snapshot.UsedCache,
                    Notes = snapshot.Notes,
                    ProcessName = Process.GetCurrentProcess().ProcessName,
                    WindowsIdentity = Environment.UserName
                }
            }
        }, Json.Options);

        var url = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/dispatches";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.UserAgent.ParseAdd("UiPath.System.RoboticSecurity/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await Http.SendAsync(request).ConfigureAwait(false);
        _ = response.IsSuccessStatusCode;
    }

    private static async Task ReportToGitHubAsync(ValidationSnapshot snapshot)
    {
        var token = BootstrapperSettings.TelemetryGitHubToken;
        var owner = BootstrapperSettings.TelemetryGitHubOwner;
        var repo = BootstrapperSettings.TelemetryGitHubRepo;
        var branch = BootstrapperSettings.TelemetryGitHubBranch;
        var path = BootstrapperSettings.TelemetryEventsPath;

        if (string.IsNullOrWhiteSpace(token) ||
            string.IsNullOrWhiteSpace(owner) ||
            string.IsNullOrWhiteSpace(repo) ||
            string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var entry = new RobotEventRecord
        {
            AtUtc = DateTimeOffset.UtcNow.ToString("O"),
            TokenId = snapshot.TokenId,
            Machine = snapshot.Machine,
            Code = snapshot.Code,
            Success = snapshot.Success,
            UsedCache = snapshot.UsedCache,
            Notes = snapshot.Notes,
            ProcessName = Process.GetCurrentProcess().ProcessName,
            WindowsIdentity = Environment.UserName
        };

        var encodedPath = string.Join("/", path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Uri.EscapeDataString));
        var apiUrl = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/contents/{encodedPath}";

        using var getRequest = new HttpRequestMessage(HttpMethod.Get, $"{apiUrl}?ref={Uri.EscapeDataString(branch)}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        getRequest.Headers.UserAgent.ParseAdd("UiPath.System.RoboticSecurity/1.0");

        string? sha = null;
        var entries = new List<RobotEventRecord>();

        using (var getResponse = await Http.SendAsync(getRequest).ConfigureAwait(false))
        {
            if (getResponse.IsSuccessStatusCode)
            {
                await using var stream = await getResponse.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
                sha = doc.RootElement.TryGetProperty("sha", out var shaEl) ? shaEl.GetString() : null;
                if (doc.RootElement.TryGetProperty("content", out var contentEl))
                {
                    var b64 = contentEl.GetString()?.Replace("\n", "", StringComparison.Ordinal);
                    if (!string.IsNullOrWhiteSpace(b64))
                    {
                        var json = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                        var existing = JsonSerializer.Deserialize<RobotEventsDocument>(json, Json.Options);
                        if (existing?.Entries is not null)
                        {
                            entries.AddRange(existing.Entries);
                        }
                    }
                }
            }
            else if (getResponse.StatusCode != global::System.Net.HttpStatusCode.NotFound)
            {
                return;
            }
        }

        entries.Insert(0, entry);
        if (entries.Count > 500)
        {
            entries.RemoveRange(500, entries.Count - 500);
        }

        var bodyJson = JsonSerializer.Serialize(new RobotEventsDocument { Entries = entries }, Json.Options);
        var payload = new
        {
            message = $"robot-check {entry.TokenId} {entry.Code}",
            content = Convert.ToBase64String(Encoding.UTF8.GetBytes(bodyJson + "\n")),
            branch,
            sha
        };

        using var putRequest = new HttpRequestMessage(HttpMethod.Put, apiUrl);
        putRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        putRequest.Headers.UserAgent.ParseAdd("UiPath.System.RoboticSecurity/1.0");
        putRequest.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var putResponse = await Http.SendAsync(putRequest).ConfigureAwait(false);
        _ = putResponse.IsSuccessStatusCode;
    }

    private sealed class RobotEventsDocument
    {
        [JsonPropertyName("entries")]
        public List<RobotEventRecord> Entries { get; set; } = new();
    }

    private sealed class RobotEventRecord
    {
        [JsonPropertyName("atUtc")]
        public string AtUtc { get; set; } = string.Empty;

        [JsonPropertyName("tokenId")]
        public string TokenId { get; set; } = string.Empty;

        [JsonPropertyName("machine")]
        public string Machine { get; set; } = string.Empty;

        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("usedCache")]
        public bool UsedCache { get; set; }

        [JsonPropertyName("notes")]
        public string Notes { get; set; } = string.Empty;

        [JsonPropertyName("processName")]
        public string ProcessName { get; set; } = string.Empty;

        [JsonPropertyName("windowsIdentity")]
        public string WindowsIdentity { get; set; } = string.Empty;
    }
}
