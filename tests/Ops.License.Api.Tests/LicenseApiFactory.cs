using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Ops.License.Api;

namespace Ops.License.Api.Tests;

internal sealed class LicenseApiFactory : WebApplicationFactory<Program>
{
    internal const string OperatorSecret = "test-operator-secret";
    internal const string SessionSigningKey = "test-session-signing-key-32-chars!";
    internal const string SeedPath = "docs/assets/seed.jwt";

    private readonly FakeGitHubHandler _github = new();
    private readonly FixtureManifest _manifest;

    internal LicenseApiFactory(FixtureManifest manifest, string seedJwt)
    {
        _manifest = manifest;
        _github.SetTextFile(SeedPath, $"{seedJwt.Trim()}\n");
    }

    internal FakeGitHubHandler GitHub => _github;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        Environment.SetEnvironmentVariable("GITHUB_TOKEN", "ghp_test_token");
        Environment.SetEnvironmentVariable("OPS_OPERATOR_SECRET", OperatorSecret);
        Environment.SetEnvironmentVariable("OPS_SESSION_SIGNING_KEY", SessionSigningKey);
        Environment.SetEnvironmentVariable("OPS_SEED_PEPPER", _manifest.Pepper);
        Environment.SetEnvironmentVariable("OPS_SEED_ENVELOPE_PEPPER", _manifest.EnvelopePepper);
        Environment.SetEnvironmentVariable("OPS_SEED_ENVELOPE_SIGNING_KEY", _manifest.EnvelopeSigningKey);
        Environment.SetEnvironmentVariable("OPS_SEED_ENVELOPE_ISSUER", _manifest.EnvelopeIssuer);
        Environment.SetEnvironmentVariable("OPS_SEED_ENVELOPE_AUDIENCE", _manifest.EnvelopeAudience);
        Environment.SetEnvironmentVariable("OPS_SEED_PUBLIC_SEAL_KEY_PEM", _manifest.PublicSealKeyPem);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<GitHubContentsClient>();
            services.AddSingleton(_github);
            services.AddSingleton<GitHubContentsClient>(sp =>
            {
                var http = new HttpClient(_github) { BaseAddress = new Uri("https://api.github.com/") };
                return new GitHubContentsClient(http, sp.GetRequiredService<GitHubOptions>());
            });
        });
    }

    internal HttpMessageHandler CreateTestHandler() => Server.CreateHandler();
}

internal sealed class FixtureManifest
{
    [JsonPropertyName("tokenId")]
    public string TokenId { get; init; } = string.Empty;

    [JsonPropertyName("pepper")]
    public string Pepper { get; init; } = string.Empty;

    [JsonPropertyName("envelopePepper")]
    public string EnvelopePepper { get; init; } = string.Empty;

    [JsonPropertyName("envelopeSigningKey")]
    public string EnvelopeSigningKey { get; init; } = string.Empty;

    [JsonPropertyName("envelopeIssuer")]
    public string EnvelopeIssuer { get; init; } = string.Empty;

    [JsonPropertyName("envelopeAudience")]
    public string EnvelopeAudience { get; init; } = string.Empty;

    [JsonPropertyName("publicSealKeyPem")]
    public string PublicSealKeyPem { get; init; } = string.Empty;

    [JsonPropertyName("liveJwt")]
    public string LiveJwt { get; init; } = string.Empty;

    [JsonPropertyName("disabledJwt")]
    public string DisabledJwt { get; init; } = string.Empty;

    [JsonPropertyName("expiredJwt")]
    public string ExpiredJwt { get; init; } = string.Empty;

    [JsonPropertyName("hostRestrictedJwt")]
    public string HostRestrictedJwt { get; init; } = string.Empty;

    internal static FixtureManifest Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "manifest.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<FixtureManifest>(json, JsonOptions)
            ?? throw new InvalidOperationException("Invalid manifest.json");
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
}
