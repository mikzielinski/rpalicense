using Ops.Runtime.Seed;

namespace Ops.Runtime.Seed.Tests;

[Collection("GitHubPages")]
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
        var body = await WaitForPagesJwtAsync(http, url);

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

        var profile = await Bootstrapper.InitializeAsync(_manifest.TokenId);

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
        var remote = (await WaitForPagesJwtAsync(http, url)).Trim();

        var remoteCatalog = GitHubPagesTestHelpers.UnwrapJwt(remote);
        using var doc = JsonDocument.Parse(remoteCatalog);
        var tokenId = doc.RootElement.GetProperty("entries")[0].GetProperty("tokenId").GetString();

        Assert.Equal(_manifest.TokenId, tokenId);
        Assert.True(doc.RootElement.GetProperty("entries")[0].GetProperty("enabled").GetBoolean());
    }

    private static async Task<string> WaitForPagesJwtAsync(HttpClient http, string url)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= 12; attempt++)
        {
            try
            {
                using var response = await http.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }

                last = new HttpRequestException($"HTTP {(int)response.StatusCode} from {url}");
            }
            catch (Exception ex)
            {
                last = ex;
            }

            if (attempt < 12)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }

        throw new InvalidOperationException(
            $"GitHub Pages seed.jwt not available at {url} after retries. Last error: {last?.Message}");
    }
}
