using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UiPath.System.RoboticSecurity;

namespace Ops.License.Api.Tests;

internal static class ApiTestClient
{
    internal static async Task<JsonDocument> PostJsonAsync(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsJsonAsync(path, body);
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"POST {path} failed ({(int)response.StatusCode}): {text}");
        }

        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }

    internal static async Task<(bool Success, string Code, string? SessionToken)> AuthorizeRuntimeAsync(
        HttpClient client,
        string runtimeToken,
        string machine,
        string pepper)
    {
        var normalizedMachine = machine.Trim().ToUpperInvariant();
        var challengeDoc = await PostJsonAsync(client, "/v1/runtime/challenge", new { machine = normalizedMachine });
        var challengeId = challengeDoc.RootElement.GetProperty("challengeId").GetString()!;
        var serverNonce = challengeDoc.RootElement.GetProperty("serverNonce").GetString()!;
        var clientNonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var proof = HandshakeProof.ComputeRuntimeProof(
            runtimeToken,
            pepper,
            challengeId,
            serverNonce,
            clientNonce,
            normalizedMachine,
            runtimeToken);

        using var response = await client.PostAsJsonAsync("/v1/runtime/authorize", new
        {
            tokenId = runtimeToken,
            machine = normalizedMachine,
            challengeId,
            clientNonce,
            proof
        });
        var text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        return (
            root.GetProperty("success").GetBoolean(),
            root.GetProperty("code").GetString() ?? "boot-0xFF",
            root.TryGetProperty("sessionToken", out var session) ? session.GetString() : null);
    }

    internal static async Task<string> ObtainOperatorSessionAsync(
        HttpClient client,
        string operatorSecret,
        string operatorId = "tester")
    {
        var challengeDoc = await PostJsonAsync(client, "/v1/operator/challenge", new { operatorId });
        var challengeId = challengeDoc.RootElement.GetProperty("challengeId").GetString()!;
        var serverNonce = challengeDoc.RootElement.GetProperty("serverNonce").GetString()!;
        var clientNonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var proof = HandshakeProof.ComputeOperatorProof(
            operatorSecret,
            challengeId,
            serverNonce,
            clientNonce,
            operatorId);

        using var response = await client.PostAsJsonAsync("/v1/operator/session", new
        {
            operatorId,
            challengeId,
            clientNonce,
            proof
        });
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"operator session failed: {text}");
        }

        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.GetProperty("sessionToken").GetString()
            ?? throw new InvalidOperationException("missing sessionToken");
    }

    internal static void ApplyBootstrapperForApi(LicenseApiFactory factory, FixtureManifest manifest)
    {
        Bootstrapper.ResetForTesting();
        BootstrapperSettings.ResetToDefaults();
        ApiLicenseClient.ClearRuntimeSession();
        ApiLicenseClient.UseHttpHandlerForTesting(factory.CreateTestHandler());

        var apiUrl = factory.CreateClient().BaseAddress!.ToString().TrimEnd('/');
        BootstrapperSettings.ApiUrl = apiUrl;
        BootstrapperSettings.Pepper = manifest.Pepper;
        BootstrapperSettings.EnvelopePepper = manifest.EnvelopePepper;
        BootstrapperSettings.EnvelopeSigningKey = manifest.EnvelopeSigningKey;
        BootstrapperSettings.EnvelopeIssuer = manifest.EnvelopeIssuer;
        BootstrapperSettings.EnvelopeAudience = manifest.EnvelopeAudience;
        BootstrapperSettings.PublicSealKeyPem = manifest.PublicSealKeyPem;
        BootstrapperSettings.SourceUsesJwtEnvelope = true;
        BootstrapperSettings.TelemetryEnabled = false;
        BootstrapperSettings.KillOnDeny = false;
        BootstrapperSettings.CatalogLoaderOverride = null;
    }
}
