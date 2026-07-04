using Ops.Runtime.Seed;

namespace Ops.Runtime.Seed.Tests;

/// <summary>
/// boot-0x12 when catalog has enabled=false (symuluje odcięcie licencji na Pages).
/// Pełny E2E z GitHub API: scripts/test-license-lifecycle.sh
/// </summary>
[Collection("GitHubPages")]
public sealed class GitHubPagesRevocationTests : IDisposable
{
    private readonly FixtureManifest _manifest = TestFixture.LoadManifest();
    private readonly string _cacheDir = Path.Combine(
        Path.GetTempPath(),
        "ops-seed-revoke-" + Guid.NewGuid().ToString("N"));

    public void Dispose() => TestFixture.Reset(_cacheDir);

    [Fact]
    public void DisabledCatalog_HasEnabledFalse()
    {
        var catalog = GitHubPagesTestHelpers.UnwrapJwt(_manifest.DisabledJwt);
        using var doc = JsonDocument.Parse(catalog);
        Assert.False(doc.RootElement.GetProperty("entries")[0].GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task DisabledCatalog_BlocksInit_ReturnsBoot0x12()
    {
        TestFixture.Reset(_cacheDir);
        BootstrapperSettings.ResetToDefaults();
        BootstrapperSettings.SourceUrl = _manifest.SourceUrl;
        BootstrapperSettings.CachePathOverride = Path.Combine(_cacheDir, "seed.cache.json");
        BootstrapperSettings.CatalogLoaderOverride = _ => Task.FromResult(_manifest.DisabledJwt);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Bootstrapper.InitializeAsync(_manifest.TokenId));

        Assert.Equal("boot-0x12", ex.Message);
        Assert.Equal("boot-0x12", Bootstrapper.LastCheck.Code);
        Assert.False(Bootstrapper.LastCheck.Success);
    }

    [Fact]
    public void DisabledCatalog_TryInitialize_ReturnsFalse()
    {
        TestFixture.Reset(_cacheDir);
        BootstrapperSettings.ResetToDefaults();
        BootstrapperSettings.SourceUrl = _manifest.SourceUrl;
        BootstrapperSettings.CachePathOverride = Path.Combine(_cacheDir, "seed.cache.json");
        BootstrapperSettings.CatalogLoaderOverride = _ => Task.FromResult(_manifest.DisabledJwt);

        var ok = Bootstrapper.TryInitialize(_manifest.TokenId, out var profile);

        Assert.False(ok);
        Assert.Null(profile);
        Assert.Equal("boot-0x12", Bootstrapper.LastCheck.Code);
    }
}
