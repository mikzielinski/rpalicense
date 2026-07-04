using Ops.Runtime.Seed;

namespace Ops.Runtime.Seed.Tests;

/// <summary>
/// Verifies remote license revocation against GitHub Pages (enabled=false → boot-0x12).
/// Run after scripts/revoke-license-on-pages.sh has published the disabled JWT.
/// </summary>
public sealed class GitHubPagesRevocationTests : IDisposable
{
    private readonly FixtureManifest _manifest = TestFixture.LoadManifest();
    private readonly string _cacheDir = Path.Combine(
        Path.GetTempPath(),
        "ops-seed-revoke-" + Guid.NewGuid().ToString("N"));

    public void Dispose() => TestFixture.Reset(_cacheDir);

    [Fact]
    public async Task GitHubPages_RevokedCatalog_HasEnabledFalse()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var jwt = (await WaitForPagesJwtAsync(http, _manifest.SourceUrl)).Trim();
        var catalog = UnwrapJwt(jwt);
        using var doc = JsonDocument.Parse(catalog);
        Assert.False(doc.RootElement.GetProperty("entries")[0].GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task GitHubPages_RevokedLicense_BlocksInit_ReturnsBoot0x12()
    {
        TestFixture.Reset(_cacheDir);
        BootstrapperSettings.ResetToDefaults();
        BootstrapperSettings.CachePathOverride = Path.Combine(_cacheDir, "seed.cache.json");
        BootstrapperSettings.CatalogLoaderOverride = null;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Bootstrapper.InitializeAsync(_manifest.TokenId)).ConfigureAwait(true);

        Assert.Equal("boot-0x12", ex.Message);
        Assert.Equal("boot-0x12", Bootstrapper.LastCheck.Code);
        Assert.False(Bootstrapper.LastCheck.Success);
    }

    [Fact]
    public async Task GitHubPages_RevokedLicense_TryInitialize_ReturnsFalse()
    {
        TestFixture.Reset(_cacheDir);
        BootstrapperSettings.ResetToDefaults();
        BootstrapperSettings.CachePathOverride = Path.Combine(_cacheDir, "seed.cache.json");
        BootstrapperSettings.CatalogLoaderOverride = null;

        var ok = Bootstrapper.TryInitialize(_manifest.TokenId, out var profile);

        Assert.False(ok);
        Assert.Null(profile);
        Assert.Equal("boot-0x12", Bootstrapper.LastCheck.Code);
        await Task.CompletedTask;
    }

    private static string UnwrapJwt(string jwt)
    {
        var keygen = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "keygen", "SeedForge.csproj"));
        var jwtFile = Path.Combine(Path.GetTempPath(), $"unwrap-{Guid.NewGuid():N}.jwt");
        File.WriteAllText(jwtFile, jwt);
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments =
                    $"run --project \"{keygen}\" -c Release -- unwrapjwt \"{jwtFile}\" " +
                    "\"test-jwt-signing-key-ops-runtime-seed-2026\" " +
                    "\"test-envelope-pepper-ops-runtime-2026\" " +
                    "\"https://mikzielinski.github.io/rpalicense\" " +
                    "\"ops-runtime-seed\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            })!;
            var stdout = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            if (proc.ExitCode != 0)
            {
                throw new InvalidOperationException(proc.StandardError.ReadToEnd());
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
                using var response = await http.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }

                last = new HttpRequestException($"HTTP {(int)response.StatusCode}");
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

        throw new InvalidOperationException($"Pages JWT unavailable: {last?.Message}");
    }
}
