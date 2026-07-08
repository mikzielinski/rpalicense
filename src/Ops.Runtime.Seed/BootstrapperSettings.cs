namespace UiPath.System.RoboticSecurity;

internal static class BootstrapperSettings
{
    internal const string ProductionSourceUrl =
        "https://mikzielinski.github.io/rpalicense/assets/seed.jwt";

    private const string ProductionPublicSealKeyPem =
        "-----BEGIN RSA PUBLIC KEY-----\n" +
        "MIIBigKCAYEA0fjsptTsCqauH26cK2zToyfaGxB8i6F8nDBfhLN9SlYeAfrSfDep\n" +
        "IMqql4x8SbMXLTktQF435XZ07fS0s9R/xLANOlT/L+8p/j4sgoYdd8kF2AdS62wD\n" +
        "6g/I/UUO0KZuOqZU6C7WsKDJEGXbCBgQCAdl2jpFGijrDiirlrGyUTaGiE6mbfNM\n" +
        "6MnkW99SHRUelzY155WN8bco0o23jG4Y1wbplbW19BWcmE4+QrNfIIuU1/4XZhgE\n" +
        "fUFfwzkr8jRlGmWhXUlnPJAJHtYPZdzfZtHlcdJmVclhg6kmNGvE80KfT54cWVW8\n" +
        "EWFNbkZn1mFY47/4+eyaiTVPQHXZAgqNkWAAWqYZB8uzJwB7zJxirWUcl1WJLkge\n" +
        "UATyh/RtiaOMzhE51y+RJHTd4dwuVuthtUWUMXF8UP7ZhdN1J70iAO7pZWjJSL7X\n" +
        "WnJfjU2U1Q3fG1J6i1WBU8M0rMqvd0dHdCQP72TPwNnQyMM44Rl7qxGNbc+xadG0\n" +
        "qi3B75/Z1OgZAgMBAAE=\n" +
        "-----END RSA PUBLIC KEY-----\n";

    internal static string SourceUrl { get; set; } = ProductionSourceUrl;
    internal static string SourceToken { get; set; } = string.Empty;
    internal static string Pepper { get; set; } = "test-pepper-ops-runtime-seed-2026";
    internal static string EnvelopePepper { get; set; } = "test-envelope-pepper-ops-runtime-2026";
    internal static string EnvelopeSigningKey { get; set; } = "test-jwt-signing-key-ops-runtime-seed-2026";
    internal static string EnvelopeIssuer { get; set; } = "https://mikzielinski.github.io/rpalicense";
    internal static string EnvelopeAudience { get; set; } = "ops-runtime-seed";
    internal static bool SourceUsesJwtEnvelope { get; set; } = true;
    internal static string PublicSealKeyPem { get; set; } = ProductionPublicSealKeyPem;
    internal static int GraceDays { get; set; } = 7;
    internal static bool KillOnDeny { get; set; }
    internal static string? CachePathOverride { get; set; }
    internal static Func<string, Task<string>>? CatalogLoaderOverride { get; set; }

    internal static void ResetToDefaults()
    {
        SourceUrl = ProductionSourceUrl;
        SourceToken = string.Empty;
        Pepper = "test-pepper-ops-runtime-seed-2026";
        EnvelopePepper = "test-envelope-pepper-ops-runtime-2026";
        EnvelopeSigningKey = "test-jwt-signing-key-ops-runtime-seed-2026";
        EnvelopeIssuer = "https://mikzielinski.github.io/rpalicense";
        EnvelopeAudience = "ops-runtime-seed";
        SourceUsesJwtEnvelope = true;
        PublicSealKeyPem = ProductionPublicSealKeyPem;
        GraceDays = 7;
        KillOnDeny = false;
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

        var killEnv = ReadEnv("OPS_SEED_KILL_ON_DENY");
        if (killEnv is not null)
        {
            KillOnDeny = killEnv is "1" or "true" or "TRUE" or "yes" or "YES";
        }
        else if (OperatingSystem.IsWindows())
        {
            // Production robots on Windows: terminate UiPath when license is cut/expired.
            KillOnDeny = true;
        }

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
