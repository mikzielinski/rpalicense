using System.Net.Http.Headers;
using System.Net.Http.Json;
using UiPath.System.RoboticSecurity;

namespace Ops.License.Api.Tests;

public sealed class LicenseApiLifecycleTests
{
    private readonly FixtureManifest _manifest = FixtureManifest.Load();

    [Fact]
    public async Task Runtime_LiveLicense_AuthorizeReturnsBootOkRemote()
    {
        await using var factory = new LicenseApiFactory(_manifest, _manifest.LiveJwt);
        var client = factory.CreateClient();

        var (success, code, session) = await ApiTestClient.AuthorizeRuntimeAsync(
            client,
            _manifest.TokenId,
            Environment.MachineName,
            _manifest.Pepper);

        Assert.True(success);
        Assert.Equal("boot-ok-remote", code);
        Assert.False(string.IsNullOrWhiteSpace(session));
    }

    [Fact]
    public async Task Runtime_DisabledLicense_ReturnsBoot0x12()
    {
        await using var factory = new LicenseApiFactory(_manifest, _manifest.DisabledJwt);
        var client = factory.CreateClient();

        var (success, code, _) = await ApiTestClient.AuthorizeRuntimeAsync(
            client,
            _manifest.TokenId,
            Environment.MachineName,
            _manifest.Pepper);

        Assert.False(success);
        Assert.Equal("boot-0x12", code);
    }

    [Fact]
    public async Task Runtime_ExpiredLicense_ReturnsBoot0x14()
    {
        await using var factory = new LicenseApiFactory(_manifest, _manifest.ExpiredJwt);
        var client = factory.CreateClient();

        var (success, code, _) = await ApiTestClient.AuthorizeRuntimeAsync(
            client,
            _manifest.TokenId,
            Environment.MachineName,
            _manifest.Pepper);

        Assert.False(success);
        Assert.Equal("boot-0x14", code);
    }

    [Fact]
    public async Task Runtime_HostRestricted_WrongMachine_ReturnsBoot0x15()
    {
        await using var factory = new LicenseApiFactory(_manifest, _manifest.HostRestrictedJwt);
        var client = factory.CreateClient();

        var (success, code, _) = await ApiTestClient.AuthorizeRuntimeAsync(
            client,
            _manifest.TokenId,
            "ROBOT99",
            _manifest.Pepper);

        Assert.False(success);
        Assert.Equal("boot-0x15", code);
    }

    [Fact]
    public async Task Runtime_HostRestricted_AllowedMachine_ReturnsBootOkRemote()
    {
        await using var factory = new LicenseApiFactory(_manifest, _manifest.HostRestrictedJwt);
        var client = factory.CreateClient();

        var (success, code, _) = await ApiTestClient.AuthorizeRuntimeAsync(
            client,
            _manifest.TokenId,
            "ROBOT01",
            _manifest.Pepper);

        Assert.True(success);
        Assert.Equal("boot-ok-remote", code);
    }

    [Fact]
    public async Task Runtime_InvalidProof_ReturnsBoot0x65()
    {
        await using var factory = new LicenseApiFactory(_manifest, _manifest.LiveJwt);
        var client = factory.CreateClient();

        var challengeDoc = await ApiTestClient.PostJsonAsync(client, "/v1/runtime/challenge", new { machine = "HOST" });
        using var response = await client.PostAsJsonAsync("/v1/runtime/authorize", new
        {
            tokenId = _manifest.TokenId,
            machine = "HOST",
            challengeId = challengeDoc.RootElement.GetProperty("challengeId").GetString(),
            clientNonce = "bad",
            proof = "bad-proof"
        });
        var text = await response.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(text);

        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("boot-0x65", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Operator_Handshake_CanReadCatalog()
    {
        await using var factory = new LicenseApiFactory(_manifest, _manifest.LiveJwt);
        var client = factory.CreateClient();
        var session = await ApiTestClient.ObtainOperatorSessionAsync(client, LicenseApiFactory.OperatorSecret);

        var request = new HttpRequestMessage(HttpMethod.Get, "/v1/catalog");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var doc = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonDocument>();
        Assert.NotNull(doc);
        var jwt = doc.RootElement.GetProperty("jwt").GetString();
        Assert.StartsWith("eyJ", jwt);
    }

    [Fact]
    public async Task Operator_InvalidProof_Returns401()
    {
        await using var factory = new LicenseApiFactory(_manifest, _manifest.LiveJwt);
        var client = factory.CreateClient();

        var challengeDoc = await ApiTestClient.PostJsonAsync(client, "/v1/operator/challenge", new { operatorId = "tester" });
        using var response = await client.PostAsJsonAsync("/v1/operator/session", new
        {
            operatorId = "tester",
            challengeId = challengeDoc.RootElement.GetProperty("challengeId").GetString(),
            clientNonce = "x",
            proof = "y"
        });

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Bootstrapper_ViaApi_LiveLicense_ReturnsBootOkRemote()
    {
        await using var factory = new LicenseApiFactory(_manifest, _manifest.LiveJwt);
        ApiTestClient.ApplyBootstrapperForApi(factory, _manifest);

        var profile = await Bootstrapper.InitializeAsync(_manifest.TokenId);

        Assert.Equal("boot-ok-remote", Bootstrapper.LastCheck.Code);
        Assert.True(Bootstrapper.LastCheck.Success);
        Assert.Equal(_manifest.TokenId, profile.TokenId);
    }

    [Fact]
    public async Task Bootstrapper_ViaApi_DisabledLicense_ReturnsBoot0x12()
    {
        await using var factory = new LicenseApiFactory(_manifest, _manifest.DisabledJwt);
        ApiTestClient.ApplyBootstrapperForApi(factory, _manifest);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Bootstrapper.InitializeAsync(_manifest.TokenId));

        Assert.Equal("boot-0x12", ex.Message);
        Assert.Equal("boot-0x12", Bootstrapper.LastCheck.Code);
    }

    [Fact]
    public async Task Bootstrapper_ViaApi_ReactivatedCatalog_WorksAfterDisable()
    {
        await using (var disabledFactory = new LicenseApiFactory(_manifest, _manifest.DisabledJwt))
        {
            ApiTestClient.ApplyBootstrapperForApi(disabledFactory, _manifest);
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Bootstrapper.InitializeAsync(_manifest.TokenId));
        }

        await using var liveFactory = new LicenseApiFactory(_manifest, _manifest.LiveJwt);
        ApiTestClient.ApplyBootstrapperForApi(liveFactory, _manifest);

        var profile = await Bootstrapper.InitializeAsync(_manifest.TokenId);

        Assert.Equal("boot-ok-remote", Bootstrapper.LastCheck.Code);
        Assert.Equal(_manifest.TokenId, profile.TokenId);
    }
}
