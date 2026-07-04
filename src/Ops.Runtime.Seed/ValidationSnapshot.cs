namespace Ops.Runtime.Seed;

internal sealed class ValidationSnapshot
{
    public bool Success { get; init; }
    public bool UsedCache { get; init; }
    public string SourceUrl { get; init; } = string.Empty;
    public string TokenId { get; init; } = string.Empty;
    public string Machine { get; init; } = string.Empty;
    public DateTimeOffset CheckedAtUtc { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
}
