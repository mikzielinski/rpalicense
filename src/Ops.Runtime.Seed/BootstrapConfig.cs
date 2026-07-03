namespace Ops.Runtime.Seed;

internal static class BootstrapConfig
{
    private const string DefaultSourceUrl = "https://example.github.io/assets/seed.jwt";
    private const string DefaultPepper = "replace-with-long-random-pepper";
    private const string DefaultEnvelopePepper = "replace-with-long-random-envelope-pepper";
    private const string DefaultEnvelopeSigningKey = "replace-with-long-random-envelope-signing-key";
    private const string DefaultEnvelopeIssuer = "https://example.github.io";
    private const string DefaultEnvelopeAudience = "ops-runtime-seed";
    private const string DefaultPublicSealKeyPem = """
-----BEGIN PUBLIC KEY-----
REPLACE_WITH_RSA_PUBLIC_KEY
-----END PUBLIC KEY-----
""";

    internal static string SourceUrl => Read("FLOW_RUNTIME_SOURCE_URL", DefaultSourceUrl);
    internal static string SourceToken => Read("FLOW_RUNTIME_SOURCE_TOKEN", string.Empty);
    internal static string Pepper => Read("FLOW_RUNTIME_PEPPER", DefaultPepper);
    internal static string EnvelopePepper => Read("FLOW_RUNTIME_ENVELOPE_PEPPER", DefaultEnvelopePepper);
    internal static string EnvelopeSigningKey => Read("FLOW_RUNTIME_ENVELOPE_SIGNING_KEY", DefaultEnvelopeSigningKey);
    internal static string EnvelopeIssuer => Read("FLOW_RUNTIME_ENVELOPE_ISSUER", DefaultEnvelopeIssuer);
    internal static string EnvelopeAudience => Read("FLOW_RUNTIME_ENVELOPE_AUDIENCE", DefaultEnvelopeAudience);
    internal static bool SourceUsesJwtEnvelope => !string.Equals(
        Read("FLOW_RUNTIME_USE_JWT_ENVELOPE", "true"),
        "false",
        StringComparison.OrdinalIgnoreCase);

    internal static string PublicSealKeyPem => ReadPublicSealKeyPem();

    internal static string? CachePath => Environment.GetEnvironmentVariable("FLOW_RUNTIME_CACHE_PATH");

    private static string Read(string envVar, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(envVar);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string ReadPublicSealKeyPem()
    {
        var pemFile = Environment.GetEnvironmentVariable("FLOW_RUNTIME_PUBLIC_SEAL_KEY_PEM_FILE");
        if (!string.IsNullOrWhiteSpace(pemFile) && File.Exists(pemFile))
        {
            return File.ReadAllText(pemFile);
        }

        var inline = Environment.GetEnvironmentVariable("FLOW_RUNTIME_PUBLIC_SEAL_KEY_PEM");
        if (!string.IsNullOrWhiteSpace(inline))
        {
            return inline.Replace("\\n", "\n", StringComparison.Ordinal);
        }

        return DefaultPublicSealKeyPem;
    }
}
