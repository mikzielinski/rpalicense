using System.Text.Json.Serialization;
using Ops.Runtime.Seed;

namespace Ops.Runtime.Seed.Tests;

public sealed class FixtureManifest
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

    [JsonPropertyName("validToUtc")]
    public string ValidToUtc { get; init; } = string.Empty;

    [JsonPropertyName("publicSealKeyPem")]
    public string PublicSealKeyPem { get; init; } = string.Empty;

    [JsonPropertyName("liveJwt")]
    public string LiveJwt { get; init; } = string.Empty;

    [JsonPropertyName("disabledJwt")]
    public string DisabledJwt { get; init; } = string.Empty;

    [JsonPropertyName("hostRestrictedJwt")]
    public string HostRestrictedJwt { get; init; } = string.Empty;
}

public static class TestFixture
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static FixtureManifest LoadManifest()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "manifest.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "Run scripts/generate-test-fixtures.sh before tests.",
                path);
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<FixtureManifest>(json, JsonOptions)
            ?? throw new InvalidOperationException("Invalid manifest.json");
    }

    public static void ApplyManifest(FixtureManifest manifest, string jwtBody, string? cacheDir = null)
    {
        BootstrapperSettings.SourceUrl = "https://test.local/seed.jwt";
        BootstrapperSettings.SourceToken = string.Empty;
        BootstrapperSettings.Pepper = manifest.Pepper;
        BootstrapperSettings.EnvelopePepper = manifest.EnvelopePepper;
        BootstrapperSettings.EnvelopeSigningKey = manifest.EnvelopeSigningKey;
        BootstrapperSettings.EnvelopeIssuer = manifest.EnvelopeIssuer;
        BootstrapperSettings.EnvelopeAudience = manifest.EnvelopeAudience;
        BootstrapperSettings.PublicSealKeyPem = manifest.PublicSealKeyPem;
        BootstrapperSettings.SourceUsesJwtEnvelope = true;
        BootstrapperSettings.GraceDays = 7;
        BootstrapperSettings.CachePathOverride = cacheDir is null
            ? null
            : Path.Combine(cacheDir, "seed.cache.json");
        BootstrapperSettings.CatalogLoaderOverride = _ => Task.FromResult(jwtBody);
    }

    public static void Reset(string? cacheDir = null)
    {
        Bootstrapper.ResetForTesting();
        BootstrapperSettings.ResetToDefaults();
        ClearSeedEnvironmentVariables();
        if (cacheDir is not null && Directory.Exists(cacheDir))
        {
            Directory.Delete(cacheDir, recursive: true);
        }

        var defaultCache = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "OpsRuntime",
            "seed.cache.json");
        if (File.Exists(defaultCache))
        {
            File.Delete(defaultCache);
        }
    }

    public static string RunDependencyHost(string token, string jwtBody, FixtureManifest manifest)
    {
        var hostProject = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "Ops.Runtime.Seed.DependencyHost",
            "Ops.Runtime.Seed.DependencyHost.csproj"));

        var hostDir = Path.GetDirectoryName(hostProject)!;
        var dll = Path.Combine(hostDir, "bin", "Release", "net6.0", "Ops.Runtime.Seed.DependencyHost.dll");

        if (!File.Exists(dll))
        {
            var build = Process.Start(new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"build \"{hostProject}\" -c Release",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });
            build!.WaitForExit();
            if (build.ExitCode != 0)
            {
                throw new InvalidOperationException("Failed to build DependencyHost");
            }
        }

        var jwtFile = Path.Combine(Path.GetTempPath(), $"ops-seed-jwt-{Guid.NewGuid():N}.jwt");
        var pemFile = Path.Combine(Path.GetTempPath(), $"ops-seed-pem-{Guid.NewGuid():N}.pem");
        var cacheDir = Path.Combine(Path.GetTempPath(), $"ops-seed-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cacheDir);
        File.WriteAllText(jwtFile, jwtBody);
        File.WriteAllText(pemFile, manifest.PublicSealKeyPem);

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{dll}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        psi.Environment["FLOW_RUNTIME_TOKEN"] = token;
        psi.Environment["OPS_SEED_SOURCE_URL"] = $"file://{jwtFile}";
        psi.Environment["OPS_SEED_CATALOG_FILE"] = jwtFile;
        psi.Environment["OPS_SEED_PEPPER"] = manifest.Pepper;
        psi.Environment["OPS_SEED_ENVELOPE_PEPPER"] = manifest.EnvelopePepper;
        psi.Environment["OPS_SEED_ENVELOPE_SIGNING_KEY"] = manifest.EnvelopeSigningKey;
        psi.Environment["OPS_SEED_ENVELOPE_ISSUER"] = manifest.EnvelopeIssuer;
        psi.Environment["OPS_SEED_ENVELOPE_AUDIENCE"] = manifest.EnvelopeAudience;
        psi.Environment["OPS_SEED_PUBLIC_SEAL_KEY_FILE"] = pemFile;
        psi.Environment["OPS_SEED_CACHE_PATH"] = Path.Combine(cacheDir, "seed.cache.json");

        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        try { File.Delete(jwtFile); File.Delete(pemFile); Directory.Delete(cacheDir, true); } catch { /* ignore */ }

        if (proc.ExitCode != 0)
        {
            throw new InvalidOperationException($"DependencyHost failed: {stderr}\n{stdout}");
        }

        return stdout.Trim();
    }

    private static void ClearSeedEnvironmentVariables()
    {
        foreach (var name in new[]
        {
            "FLOW_RUNTIME_TOKEN",
            "APP_BOOT_TOKEN",
            "OPS_SEED_SOURCE_URL",
            "OPS_SEED_SOURCE_TOKEN",
            "OPS_SEED_PEPPER",
            "OPS_SEED_ENVELOPE_PEPPER",
            "OPS_SEED_ENVELOPE_SIGNING_KEY",
            "OPS_SEED_ENVELOPE_ISSUER",
            "OPS_SEED_ENVELOPE_AUDIENCE",
            "OPS_SEED_PUBLIC_SEAL_KEY_PEM",
            "OPS_SEED_PUBLIC_SEAL_KEY_FILE",
            "OPS_SEED_CATALOG_FILE",
            "OPS_SEED_CACHE_PATH"
        })
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }
}
