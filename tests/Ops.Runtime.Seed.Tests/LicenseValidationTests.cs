using UiPath.System.RoboticSecurity;

namespace Ops.Runtime.Seed.Tests;

public sealed class LicenseValidationTests : IDisposable
{
    private readonly FixtureManifest _manifest = TestFixture.LoadManifest();
    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), "ops-seed-test-" + Guid.NewGuid().ToString("N"));
    private string _activeJwt = string.Empty;

    public void Dispose()
    {
        TestFixture.Reset(_cacheDir);
    }

    private void UseJwt(string jwt)
    {
        _activeJwt = jwt;
        TestFixture.Reset(_cacheDir);
        TestFixture.ApplyManifest(_manifest, jwt, _cacheDir);
    }

    [Fact]
    public void License_IsGeneratedAndRegistered_InCatalog()
    {
        Assert.Equal("RT-TEST-REPORT-001", _manifest.TokenId);
        Assert.Contains("entries", File.ReadAllText(Path.Combine(
            FindFixturesRoot(), "catalog", "live.json")));
        Assert.False(string.IsNullOrWhiteSpace(_manifest.LiveJwt));
        Assert.Contains(".", _manifest.LiveJwt);
    }

    [Fact]
    public void LiveLicense_ValidatesRemotely_ReturnsBootOkRemote()
    {
        UseJwt(_manifest.LiveJwt);

        var profile = Bootstrapper.Initialize(_manifest.TokenId);

        Assert.Equal("boot-ok-remote", Bootstrapper.LastCheck.Code);
        Assert.True(Bootstrapper.LastCheck.Success);
        Assert.False(Bootstrapper.LastCheck.UsedCache);
        Assert.Equal(_manifest.TokenId, profile.TokenId);
        Assert.Equal("Test Report Client", profile.Owner);
        Assert.Equal("https://api.example.com/v1", profile.ApiEndpoint);
    }

    [Fact]
    public void DisabledLicense_BlocksFurtherInit_ReturnsBoot0x12()
    {
        Assert.NotEqual(_manifest.LiveJwt, _manifest.DisabledJwt);

        UseJwt(_manifest.DisabledJwt);
        var ok = Bootstrapper.TryInitialize(_manifest.TokenId, out _);

        Assert.False(ok);
        Assert.Equal("boot-0x12", Bootstrapper.LastCheck.Code);
        Assert.False(Bootstrapper.LastCheck.Success);
    }

    [Fact]
    public void DisabledLicense_ClearsCurrentAfterPriorLiveValidation()
    {
        UseJwt(_manifest.LiveJwt);
        Bootstrapper.Initialize(_manifest.TokenId);
        _ = Bootstrapper.Current;

        UseJwt(_manifest.DisabledJwt);
        var ok = Bootstrapper.TryInitialize(_manifest.TokenId, out _);

        Assert.False(ok);
        Assert.Equal("boot-0x12", Bootstrapper.LastCheck.Code);
        var ex = Assert.Throws<InvalidOperationException>(() => _ = Bootstrapper.Current);
        Assert.Equal("boot-0x00", ex.Message);
    }

    [Fact]
    public void EnsureAuthorized_OnDisabled_ThrowsBoot0x12()
    {
        UseJwt(_manifest.DisabledJwt);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            Bootstrapper.EnsureAuthorized(_manifest.TokenId));

        Assert.Equal("boot-0x12", ex.Message);
        Assert.Equal("boot-0x12", Bootstrapper.LastCheck.Code);
    }

    [Fact]
    public void DisabledLicense_BlocksReInitAfterLiveValidation()
    {
        UseJwt(_manifest.LiveJwt);
        Bootstrapper.Initialize(_manifest.TokenId);
        Assert.Equal("boot-ok-remote", Bootstrapper.LastCheck.Code);

        UseJwt(_manifest.DisabledJwt);
        var ok = Bootstrapper.TryInitialize(_manifest.TokenId, out _);

        Assert.False(ok);
        Assert.Equal("boot-0x12", Bootstrapper.LastCheck.Code);
    }

    [Fact]
    public void UnknownToken_ReturnsBoot0x11()
    {
        UseJwt(_manifest.LiveJwt);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            Bootstrapper.Initialize("RT-NOT-REGISTERED"));

        Assert.Equal("boot-0x11", ex.Message);
        Assert.Equal("boot-0x11", Bootstrapper.LastCheck.Code);
    }

    [Fact]
    public void EmptyToken_ReturnsBoot0x01()
    {
        UseJwt(_manifest.LiveJwt);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            Bootstrapper.Initialize("  "));

        Assert.Equal("boot-0x01", ex.Message);
    }

    [Fact]
    public void HostNotAllowed_ReturnsBoot0x15()
    {
        Assert.NotEqual(_manifest.LiveJwt, _manifest.HostRestrictedJwt);
        UseJwt(_manifest.HostRestrictedJwt);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            Bootstrapper.Initialize(_manifest.TokenId, "ROBOT99"));

        Assert.Equal("boot-0x15", ex.Message);
    }

    [Fact]
    public void TamperedJwt_ReturnsBoot0x53()
    {
        var tampered = _manifest.LiveJwt[..^4] + "XXXX";
        UseJwt(tampered);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            Bootstrapper.Initialize(_manifest.TokenId));

        Assert.Equal("boot-0x53", ex.Message);
    }

    [Fact]
    public void DependencyHost_AutoInitsViaModuleInitializer_IsLiveWithoutManualInit()
    {
        var json = TestFixture.RunDependencyHost(_manifest.TokenId, _manifest.LiveJwt, _manifest);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("moduleInitAttempted").GetBoolean());
        Assert.True(root.GetProperty("moduleInitSucceeded").GetBoolean());

        var lastCheck = root.GetProperty("lastCheck");
        Assert.Equal("boot-ok-remote", lastCheck.GetProperty("code").GetString());
        Assert.True(lastCheck.GetProperty("success").GetBoolean());

        var profile = root.GetProperty("profile");
        Assert.Equal(_manifest.TokenId, profile.GetProperty("tokenId").GetString());
    }

    [Fact]
    public void DependencyHost_BlockedWhenLicenseDisabled()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            TestFixture.RunDependencyHost(_manifest.TokenId, _manifest.DisabledJwt, _manifest));

        Assert.Contains("DependencyHost failed", ex.Message);
    }

    [Fact]
    public void Current_BeforeInit_ThrowsBoot0x00()
    {
        UseJwt(_manifest.LiveJwt);
        Bootstrapper.ResetForTesting();

        var ex = Assert.Throws<InvalidOperationException>(() => _ = Bootstrapper.Current);
        Assert.Equal("boot-0x00", ex.Message);
    }

    [Fact]
    public void TryInitialize_OnFailure_ReturnsFalseWithoutThrowing()
    {
        UseJwt(_manifest.DisabledJwt);

        var ok = Bootstrapper.TryInitialize(_manifest.TokenId, out var profile);

        Assert.False(ok);
        Assert.Null(profile);
        Assert.Equal("boot-0x12", Bootstrapper.LastCheck.Code);
    }

    [Fact]
    public void HostAllowed_OnRestrictedList_ReturnsBootOkRemote()
    {
        UseJwt(_manifest.HostRestrictedJwt);

        var profile = Bootstrapper.Initialize(_manifest.TokenId, "ROBOT01");

        Assert.Equal("boot-ok-remote", Bootstrapper.LastCheck.Code);
        Assert.True(Bootstrapper.LastCheck.Success);
        Assert.Equal(_manifest.TokenId, profile.TokenId);
    }

    [Fact]
    public void ExpiredLicense_ReturnsBoot0x14()
    {
        Assert.NotEqual(_manifest.LiveJwt, _manifest.ExpiredJwt);
        UseJwt(_manifest.ExpiredJwt);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            Bootstrapper.Initialize(_manifest.TokenId));

        Assert.Equal("boot-0x14", ex.Message);
        Assert.Equal("boot-0x14", Bootstrapper.LastCheck.Code);
    }

    [Fact]
    public void ReactivatedAfterDisable_ValidatesWhenCatalogRestored()
    {
        UseJwt(_manifest.DisabledJwt);
        Assert.False(Bootstrapper.TryInitialize(_manifest.TokenId, out _));
        Assert.Equal("boot-0x12", Bootstrapper.LastCheck.Code);

        UseJwt(_manifest.LiveJwt);
        var profile = Bootstrapper.Initialize(_manifest.TokenId);

        Assert.Equal("boot-ok-remote", Bootstrapper.LastCheck.Code);
        Assert.True(Bootstrapper.LastCheck.Success);
        Assert.Equal(_manifest.TokenId, profile.TokenId);
    }

    [Fact]
    public void DeletedToken_ReturnsBoot0x11()
    {
        UseJwt(_manifest.LiveJwt);
        var emptyCatalogJwt = RunKeygenWrapEmptyCatalog();
        UseJwt(emptyCatalogJwt);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            Bootstrapper.Initialize(_manifest.TokenId));

        Assert.Equal("boot-0x11", ex.Message);
    }

    private string RunKeygenWrapEmptyCatalog()
    {
        var keygen = Path.GetFullPath(Path.Combine(FindFixturesRoot(), "..", "keygen", "SeedForge.csproj"));
        var catalogFile = Path.Combine(Path.GetTempPath(), $"empty-catalog-{Guid.NewGuid():N}.json");
        File.WriteAllText(catalogFile, "{\"entries\":[]}");
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments =
                    $"run --project \"{keygen}\" -c Release --no-build -- wrapjwt \"{catalogFile}\" " +
                    "\"test-jwt-signing-key-ops-runtime-seed-2026\" " +
                    "\"test-envelope-pepper-ops-runtime-2026\" " +
                    "\"https://mikzielinski.github.io/rpalicense\" " +
                    "\"ops-runtime-seed\" " +
                    "\"2027-12-31T23:59:59Z\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            })!;
            var stdout = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            if (proc.ExitCode != 0)
            {
                throw new InvalidOperationException("wrapjwt empty catalog failed");
            }

            return stdout.Trim();
        }
        finally
        {
            try { File.Delete(catalogFile); } catch { /* ignore */ }
        }
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
