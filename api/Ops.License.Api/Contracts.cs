using System.Text.Json.Serialization;
using UiPath.System.RoboticSecurity;

namespace Ops.License.Api;

public sealed class RuntimeChallengeRequest
{
    [JsonPropertyName("machine")]
    public string Machine { get; set; } = string.Empty;
}

public sealed class RuntimeAuthorizeRequest
{
    [JsonPropertyName("tokenId")]
    public string TokenId { get; set; } = string.Empty;

    [JsonPropertyName("machine")]
    public string Machine { get; set; } = string.Empty;

    [JsonPropertyName("challengeId")]
    public string ChallengeId { get; set; } = string.Empty;

    [JsonPropertyName("clientNonce")]
    public string ClientNonce { get; set; } = string.Empty;

    [JsonPropertyName("proof")]
    public string Proof { get; set; } = string.Empty;
}

public sealed class OperatorChallengeRequest
{
    [JsonPropertyName("operatorId")]
    public string OperatorId { get; set; } = string.Empty;
}

public sealed class OperatorSessionRequest
{
    [JsonPropertyName("operatorId")]
    public string OperatorId { get; set; } = string.Empty;

    [JsonPropertyName("challengeId")]
    public string ChallengeId { get; set; } = string.Empty;

    [JsonPropertyName("clientNonce")]
    public string ClientNonce { get; set; } = string.Empty;

    [JsonPropertyName("proof")]
    public string Proof { get; set; } = string.Empty;
}

public sealed class ChallengeResponse
{
    [JsonPropertyName("challengeId")]
    public string ChallengeId { get; set; } = string.Empty;

    [JsonPropertyName("serverNonce")]
    public string ServerNonce { get; set; } = string.Empty;

    [JsonPropertyName("expiresAt")]
    public string ExpiresAt { get; set; } = string.Empty;
}

public sealed class AuthorizeResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("sessionToken")]
    public string? SessionToken { get; set; }

    [JsonPropertyName("profile")]
    public RuntimeProfile? Profile { get; set; }
}

public sealed class SeedPublishRequest
{
    [JsonPropertyName("jwt")]
    public string Jwt { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

public sealed class AuditReplaceRequest
{
    [JsonPropertyName("entries")]
    public List<AuditEntryDto> Entries { get; set; } = new();
}

public sealed class AuditEntryDto
{
    [JsonPropertyName("atUtc")]
    public string AtUtc { get; set; } = string.Empty;

    [JsonPropertyName("who")]
    public string Who { get; set; } = string.Empty;

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("tokenId")]
    public string TokenId { get; set; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = string.Empty;
}

public sealed class TelemetryAppendRequest
{
    [JsonPropertyName("atUtc")]
    public string AtUtc { get; set; } = string.Empty;

    [JsonPropertyName("tokenId")]
    public string TokenId { get; set; } = string.Empty;

    [JsonPropertyName("machine")]
    public string Machine { get; set; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("usedCache")]
    public bool UsedCache { get; set; }

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = string.Empty;

    [JsonPropertyName("processName")]
    public string ProcessName { get; set; } = string.Empty;

    [JsonPropertyName("windowsIdentity")]
    public string WindowsIdentity { get; set; } = string.Empty;
}
