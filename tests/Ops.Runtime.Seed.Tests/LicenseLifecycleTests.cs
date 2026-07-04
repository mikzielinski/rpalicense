using System.Diagnostics;
using System.Text.Json;
using Ops.Runtime.Seed;

namespace Ops.Runtime.Seed.Tests;

/// <summary>
/// Uruchamia sample/Ops.Runtime.Seed.TestApp (bot z biblioteka) w trzech fazach:
/// bez licencji → po nadaniu → po deaktywacji.
/// </summary>
public sealed class LicenseLifecycleTests
{
    private readonly FixtureManifest _manifest = TestFixture.LoadManifest();

    [Fact]
    public void Lifecycle_BezLicencji_BrakTokenu()
    {
        var result = RunTestApp(token: null, jwtOverride: _manifest.LiveJwt);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("boot-0x01", result.Code);
        Assert.False(result.Success);
        Assert.Equal("bez-licencji", result.Scenario);
    }

    [Fact]
    public void Lifecycle_BezLicencji_NiezarejestrowanyToken()
    {
        var result = RunTestApp("RT-NIE-MA-W-KATALOGU", _manifest.LiveJwt);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("boot-0x11", result.Code);
        Assert.False(result.Success);
    }

    [Fact]
    public void Lifecycle_PoNadaniu_InitSucceeds()
    {
        var result = RunTestApp(_manifest.TokenId, _manifest.LiveJwt);

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Success);
        Assert.Equal("boot-ok-remote", result.Code);
        Assert.Equal("po-nadaniu", result.Scenario);
        Assert.Equal("https://api.example.com/v1", result.ApiEndpoint);
    }

    [Fact]
    public void Lifecycle_PoDeaktywacji_InitBlocked()
    {
        var result = RunTestApp(_manifest.TokenId, _manifest.DisabledJwt);

        Assert.Equal(1, result.ExitCode);
        Assert.False(result.Success);
        Assert.Equal("boot-0x12", result.Code);
        Assert.Equal("po-deaktywacji", result.Scenario);
    }

    [Fact]
    public void Lifecycle_PelnyPrzebieg_NadaniePotemDeaktywacja()
    {
        // 1) bez licencji
        var noToken = RunTestApp(null, _manifest.LiveJwt);
        Assert.Equal(2, noToken.ExitCode);

        // 2) po nadaniu (live JWT)
        var granted = RunTestApp(_manifest.TokenId, _manifest.LiveJwt);
        Assert.Equal(0, granted.ExitCode);
        Assert.Equal("boot-ok-remote", granted.Code);

        // 3) po deaktywacji (disabled JWT) — bot od razu blokuje re-init
        var revoked = RunTestApp(_manifest.TokenId, _manifest.DisabledJwt);
        Assert.Equal(1, revoked.ExitCode);
        Assert.Equal("boot-0x12", revoked.Code);
    }

    private TestAppResult RunTestApp(string? token, string jwtOverride)
    {
        var appProject = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "sample", "Ops.Runtime.Seed.TestApp", "Ops.Runtime.Seed.TestApp.csproj"));

        var appDir = Path.GetDirectoryName(appProject)!;
        var dll = Path.Combine(appDir, "bin", "Release", "net6.0", "Ops.Runtime.Seed.TestApp.dll");

        if (!File.Exists(dll))
        {
            using var build = Process.Start(new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"build \"{appProject}\" -c Release",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            })!;
            build.WaitForExit();
            if (build.ExitCode != 0)
            {
                throw new InvalidOperationException("Failed to build TestApp");
            }
        }

        var jwtFile = Path.Combine(Path.GetTempPath(), $"lifecycle-jwt-{Guid.NewGuid():N}.jwt");
        var pemFile = Path.Combine(Path.GetTempPath(), $"lifecycle-pem-{Guid.NewGuid():N}.pem");
        var cacheDir = Path.Combine(Path.GetTempPath(), $"lifecycle-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cacheDir);
        File.WriteAllText(jwtFile, jwtOverride);
        File.WriteAllText(pemFile, _manifest.PublicSealKeyPem);

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{dll}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        if (!string.IsNullOrWhiteSpace(token))
        {
            psi.Environment["FLOW_RUNTIME_TOKEN"] = token;
        }

        psi.Environment["OPS_SEED_CATALOG_FILE"] = jwtFile;
        psi.Environment["OPS_SEED_PUBLIC_SEAL_KEY_FILE"] = pemFile;
        psi.Environment["OPS_SEED_CACHE_PATH"] = Path.Combine(cacheDir, "seed.cache.json");
        psi.Environment["OPS_SEED_PEPPER"] = _manifest.Pepper;
        psi.Environment["OPS_SEED_ENVELOPE_PEPPER"] = _manifest.EnvelopePepper;
        psi.Environment["OPS_SEED_ENVELOPE_SIGNING_KEY"] = _manifest.EnvelopeSigningKey;
        psi.Environment["OPS_SEED_ENVELOPE_ISSUER"] = _manifest.EnvelopeIssuer;
        psi.Environment["OPS_SEED_ENVELOPE_AUDIENCE"] = _manifest.EnvelopeAudience;
        psi.Environment["OPS_SEED_SOURCE_URL"] = _manifest.SourceUrl;

        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();

        try { File.Delete(jwtFile); File.Delete(pemFile); Directory.Delete(cacheDir, true); } catch { /* ignore */ }

        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;

        return new TestAppResult
        {
            ExitCode = proc.ExitCode,
            Success = root.TryGetProperty("success", out var s) && s.GetBoolean(),
            Code = root.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "",
            Scenario = root.TryGetProperty("scenario", out var sc) ? sc.GetString() ?? "" : "",
            ApiEndpoint = root.TryGetProperty("profile", out var p) && p.TryGetProperty("apiEndpoint", out var api)
                ? api.GetString() ?? ""
                : "",
            RawJson = stdout
        };
    }

    private sealed class TestAppResult
    {
        public int ExitCode { get; init; }
        public bool Success { get; init; }
        public string Code { get; init; } = string.Empty;
        public string Scenario { get; init; } = string.Empty;
        public string ApiEndpoint { get; init; } = string.Empty;
        public string RawJson { get; init; } = string.Empty;
    }
}
