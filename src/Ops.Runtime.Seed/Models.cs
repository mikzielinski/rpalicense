using System.Text.Json.Serialization;

namespace Ops.Runtime.Seed;

internal sealed class CatalogDocument
{
    [JsonPropertyName("entries")]
    public List<CatalogEntry> Entries { get; init; } = new();
}

internal sealed class CatalogEntry
{
    [JsonPropertyName("tokenId")]
    public string TokenId { get; init; } = string.Empty;

    [JsonPropertyName("owner")]
    public string Owner { get; init; } = string.Empty;

    [JsonPropertyName("validToUtc")]
    public string ValidToUtc { get; init; } = string.Empty;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("hosts")]
    public List<string> Hosts { get; init; } = new();

    [JsonPropertyName("blob")]
    public string Blob { get; init; } = string.Empty;

    [JsonPropertyName("nonce")]
    public string Nonce { get; init; } = string.Empty;

    [JsonPropertyName("tag")]
    public string Tag { get; init; } = string.Empty;

    [JsonPropertyName("seal")]
    public string Seal { get; init; } = string.Empty;
}

internal sealed class SecretPayload
{
    [JsonPropertyName("apiEndpoint")]
    public string ApiEndpoint { get; init; } = string.Empty;

    [JsonPropertyName("connectionString")]
    public string ConnectionString { get; init; } = string.Empty;

    [JsonPropertyName("agentSystemPrompt")]
    public string AgentSystemPrompt { get; init; } = string.Empty;
}

internal sealed class CachedRecord
{
    [JsonPropertyName("tokenHash")]
    public string TokenHash { get; init; } = string.Empty;

    [JsonPropertyName("machine")]
    public string Machine { get; init; } = string.Empty;

    [JsonPropertyName("owner")]
    public string Owner { get; init; } = string.Empty;

    [JsonPropertyName("tokenId")]
    public string TokenId { get; init; } = string.Empty;

    [JsonPropertyName("validToUtc")]
    public DateTimeOffset ValidToUtc { get; init; }

    [JsonPropertyName("validatedAtUtc")]
    public DateTimeOffset ValidatedAtUtc { get; init; }

    [JsonPropertyName("blob")]
    public string Blob { get; init; } = string.Empty;

    [JsonPropertyName("nonce")]
    public string Nonce { get; init; } = string.Empty;

    [JsonPropertyName("tag")]
    public string Tag { get; init; } = string.Empty;
}

internal sealed class JwtEnvelopeClaims
{
    [JsonPropertyName("iss")]
    public string Issuer { get; init; } = string.Empty;

    [JsonPropertyName("aud")]
    public string Audience { get; init; } = string.Empty;

    [JsonPropertyName("exp")]
    public long Exp { get; init; }

    [JsonPropertyName("nbf")]
    public long NotBefore { get; init; }

    [JsonPropertyName("blob")]
    public string Blob { get; init; } = string.Empty;

    [JsonPropertyName("nonce")]
    public string Nonce { get; init; } = string.Empty;

    [JsonPropertyName("tag")]
    public string Tag { get; init; } = string.Empty;
}
