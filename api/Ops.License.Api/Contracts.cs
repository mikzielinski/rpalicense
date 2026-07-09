using System.Text.Json.Serialization;

namespace Ops.License.Api;

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
