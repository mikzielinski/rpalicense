using Ops.Runtime.Seed;

namespace Ops.Runtime.Seed.Tests;

public sealed class GitHubPagesIntegrationTests : IDisposable
{
    private readonly FixtureManifest _manifest = TestFixture.LoadManifest();
    private readonly string _cacheDir = Path.Combine(
        Path.GetTempPath(),
        "ops-seed-pages-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        TestFixture.Reset(_cacheDir);
    }

    [Fact]
    public async Task GitHubPages_SeedJwt_IsReachable()
    {
        var url = _manifest.SourceUrl;
        Assert.False(string.IsNullOrWhiteSpace(url));

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var body = await WaitForPagesJwtAsync(http, url).ConfigureAwait(false);

        Assert.StartsWith("eyJ", body.Trim());
        Assert.Contains(".", body);
    }

    [Fact]
    public async Task GitHubPages_LiveLicense_ValidatesViaHttp_ReturnsBootOkRemote()
    {
        TestFixture.Reset(_cacheDir);
        BootstrapperSettings.ResetToDefaults();
        BootstrapperSettings.CachePathOverride = Path.Combine(_cacheDir, "seed.cache.json");
        BootstrapperSettings.CatalogLoaderOverride = null;

        var profile = await Bootstrapper.InitializeAsync(_manifest.TokenId).ConfigureAwait(false);

        Assert.Equal("boot-ok-remote", Bootstrapper.LastCheck.Code);
        Assert.True(Bootstrapper.LastCheck.Success);
        Assert.False(Bootstrapper.LastCheck.UsedCache);
        Assert.Equal(BootstrapperSettings.ProductionSourceUrl, Bootstrapper.LastCheck.SourceUrl);
        Assert.Equal(_manifest.TokenId, profile.TokenId);
        Assert.Equal("Test Report Client", profile.Owner);
        Assert.Equal("https://api.example.com/v1", profile.ApiEndpoint);
    }

    [Fact]
    public async Task GitHubPages_SeedJwt_ContainsRegisteredToken()
    {
        var url = _manifest.SourceUrl;

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var remote = (await WaitForPagesJwtAsync(http, url).ConfigureAwait(false)).Trim();

        var keygen = Path.GetFullPath(Path.Combine(FindFixturesRoot(), "..", "keygen", "SeedForge.csproj"));
        var remoteCatalog = RunKeygenUnwrap(keygen, remote);
        using var doc = JsonDocument.Parse(remoteCatalog);
        var tokenId = doc.RootElement.GetProperty("entries")[0].GetProperty("tokenId").GetString();

        Assert.Equal(_manifest.TokenId, tokenId);
        Assert.True(doc.RootElement.GetProperty("entries")[0].GetProperty("enabled").GetBoolean());
    }

    private static string RunKeygenUnwrap(string keygenProject, string jwt)
    {
        var jwtFile = Path.Combine(Path.GetTempPath(), $"unwrap-{Guid.NewGuid():N}.jwt");
        File.WriteAllText(jwtFile, jwt);
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments =
                    $"run --project \"{keygenProject}\" -c Release --no-build -- unwrapjwt \"{jwtFile}\" " +
                    "\"test-jwt-signing-key-ops-runtime-seed-2026\" " +
                    "\"test-envelope-pepper-ops-runtime-2026\" " +
                    "\"https://mikzielinski.github.io/rpalicense\" " +
                    "\"ops-runtime-seed\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            })!;
            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            if (proc.ExitCode != 0)
            {
                throw new InvalidOperationException($"unwrapjwt failed: {stderr}");
            }

            return stdout.Trim();
        }
        finally
        {
            try { File.Delete(jwtFile); } catch { /* ignore */ }
        }
    }

    private static async Task<string> WaitForPagesJwtAsync(HttpClient http, string url)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= 12; attempt++)
        {
            try
            {
                using var response = await http.GetAsync(url).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }

                last = new HttpRequestException($"HTTP {(int)response.StatusCode} from {url}");
            }
            catch (Exception ex)
            {
                last = ex;
            }

            if (attempt < 12)
            {
                await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException(
            $"GitHub Pages seed.jwt not available at {url} after retries. Last error: {last?.Message}");
    }

    private static string FindFixturesRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.GetFullPath(Path.Combine(dir, "test-fixtures"));
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.Combine(dir, "..");
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "test-fixtures"));
    }
}
