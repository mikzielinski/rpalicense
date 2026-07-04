namespace Ops.Runtime.Seed;

internal static class BootstrapperSettings
{
    private const string DefaultPublicSealKeyPem =
        "-----BEGIN PUBLIC KEY-----\nREPLACE_WITH_RSA_PUBLIC_KEY\n-----END PUBLIC KEY-----\n";

    internal static string SourceUrl { get; set; } = "https://example.github.io/assets/seed.jwt";
    internal static string SourceToken { get; set; } = string.Empty;
    internal static string Pepper { get; set; } = "replace-with-long-random-pepper";
    internal static string EnvelopePepper { get; set; } = "replace-with-long-random-envelope-pepper";
    internal static string EnvelopeSigningKey { get; set; } = "replace-with-long-random-envelope-signing-key";
    internal static string EnvelopeIssuer { get; set; } = "https://example.github.io";
    internal static string EnvelopeAudience { get; set; } = "ops-runtime-seed";
    internal static bool SourceUsesJwtEnvelope { get; set; } = true;
    internal static string PublicSealKeyPem { get; set; } = DefaultPublicSealKeyPem;
    internal static int GraceDays { get; set; } = 7;
    internal static string? CachePathOverride { get; set; }
    internal static Func<string, Task<string>>? CatalogLoaderOverride { get; set; }

    internal static void ResetToDefaults()
    {
        SourceUrl = "https://example.github.io/assets/seed.jwt";
        SourceToken = string.Empty;
        Pepper = "replace-with-long-random-pepper";
        EnvelopePepper = "replace-with-long-random-envelope-pepper";
        EnvelopeSigningKey = "replace-with-long-random-envelope-signing-key";
        EnvelopeIssuer = "https://example.github.io";
        EnvelopeAudience = "ops-runtime-seed";
        SourceUsesJwtEnvelope = true;
        PublicSealKeyPem = DefaultPublicSealKeyPem;
        GraceDays = 7;
        CachePathOverride = null;
        CatalogLoaderOverride = null;
    }

    internal static void ApplyFromEnvironment()
    {
        SourceUrl = ReadEnv("OPS_SEED_SOURCE_URL") ?? SourceUrl;
        SourceToken = ReadEnv("OPS_SEED_SOURCE_TOKEN") ?? SourceToken;
        Pepper = ReadEnv("OPS_SEED_PEPPER") ?? Pepper;
        EnvelopePepper = ReadEnv("OPS_SEED_ENVELOPE_PEPPER") ?? EnvelopePepper;
        EnvelopeSigningKey = ReadEnv("OPS_SEED_ENVELOPE_SIGNING_KEY") ?? EnvelopeSigningKey;
        EnvelopeIssuer = ReadEnv("OPS_SEED_ENVELOPE_ISSUER") ?? EnvelopeIssuer;
        EnvelopeAudience = ReadEnv("OPS_SEED_ENVELOPE_AUDIENCE") ?? EnvelopeAudience;
        PublicSealKeyPem = ReadEnv("OPS_SEED_PUBLIC_SEAL_KEY_PEM") ?? PublicSealKeyPem;
        var publicKeyFile = ReadEnv("OPS_SEED_PUBLIC_SEAL_KEY_FILE");
        if (!string.IsNullOrWhiteSpace(publicKeyFile) && File.Exists(publicKeyFile))
        {
            PublicSealKeyPem = File.ReadAllText(publicKeyFile);
        }

        CachePathOverride = ReadEnv("OPS_SEED_CACHE_PATH") ?? CachePathOverride;

        var catalogFile = ReadEnv("OPS_SEED_CATALOG_FILE");
        if (CatalogLoaderOverride is null &&
            !string.IsNullOrWhiteSpace(catalogFile) &&
            File.Exists(catalogFile))
        {
            CatalogLoaderOverride = _ => Task.FromResult(File.ReadAllText(catalogFile));
        }
    }

    private static string? ReadEnv(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
