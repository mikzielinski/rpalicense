using Ops.Runtime.Seed;

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
