namespace Ops.Runtime.Seed;

internal static class BootstrapConfig
{
    // Domyślne wartości demo GitHub Pages (repo rpalicense). Nadpisz env lub przed produkcją.
    private const string DefaultSourceUrl = "https://mikzielinski.github.io/rpalicense/assets/seed.jwt";
    private const string DefaultPepper = "pages-demo-pepper-v1-7f3a9c2e1b0d";
    private const string DefaultEnvelopePepper = "pages-demo-envelope-pepper-v1-4e8b1a";
    private const string DefaultEnvelopeSigningKey = "pages-demo-jwt-signing-key-v1-9d2c7f";
    private const string DefaultEnvelopeIssuer = "https://mikzielinski.github.io/rpalicense";
    private const string DefaultEnvelopeAudience = "ops-runtime-seed";
    private const string DefaultPublicSealKeyPem = """
-----BEGIN RSA PUBLIC KEY-----
MIIBigKCAYEAth2XWrDXhL26271B54XM6rzQ85+FU7S0xuJ0zUnpiseBXGTTK1fN
Rxs+n5dWjbA9nzey64B64QHK8+4R2SspGnTXOqNCXJQ3zYbgYOQa8ZDIIAxbwW0V
g8zrBWnlGIF/GxwLKNr0652GFhjSCoUilIlv1Wdoql9g+72apMpZFtlTXsxWYj83
tHJEDHEcQi8LqyUKvHZSlJO0IhVXCboxp9cpcrAQ6ti8vdHZCK5Qqyt716vU6ufT
CY21Hhglqfcu3iaAu3XduDGWQm6lZ9JlBE9NKIBwFfSoRaSWDVNOppahUfvjd4Lv
PXpB8IYcyspCw1hNWqF73kuaFyOxBfvlX27Ivg59FvsnYqwpz6bsu0BX3leBzFpQ
XOY8yKi6Ue4domiCPJbUfvFEOCM+9efsJ4aDPQJ9Hy1EUUJ3aujZMxC/UihbYkFM
BK2OruqtHH9W/occvcIobwpeNsJc/tmo9NTX7pQtSUNzVlkt/2Ff+PnpvPhadgjz
IaXFbsOEE5prAgMBAAE=
-----END RSA PUBLIC KEY-----
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
