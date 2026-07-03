namespace Ops.Runtime.Seed;

public sealed class RuntimeProfile
{
    public string ApiEndpoint { get; init; } = string.Empty;
    public string ConnectionString { get; init; } = string.Empty;
    public string AgentSystemPrompt { get; init; } = string.Empty;
    public DateTimeOffset ValidToUtc { get; init; }
    public string Owner { get; init; } = string.Empty;
    public string TokenId { get; init; } = string.Empty;
}
