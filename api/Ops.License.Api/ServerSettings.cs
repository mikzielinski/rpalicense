namespace Ops.License.Api;

public sealed class ServerSettings
{
    public const string SectionName = "Server";

    public string Pepper { get; set; } = "test-pepper-ops-runtime-seed-2026";
    public string EnvelopePepper { get; set; } = "test-envelope-pepper-ops-runtime-2026";
    public string EnvelopeSigningKey { get; set; } = "test-jwt-signing-key-ops-runtime-seed-2026";
    public string EnvelopeIssuer { get; set; } = "https://mikzielinski.github.io/rpalicense";
    public string EnvelopeAudience { get; set; } = "ops-runtime-seed";
    public string PublicSealKeyPem { get; set; } = string.Empty;
    public string OperatorSecret { get; set; } = string.Empty;
    public string SessionSigningKey { get; set; } = string.Empty;
    public int ChallengeTtlSeconds { get; set; } = 120;
    public int SessionTtlMinutes { get; set; } = 30;
}
