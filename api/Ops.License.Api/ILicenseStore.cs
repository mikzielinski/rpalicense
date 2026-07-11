namespace Ops.License.Api;

public interface ILicenseStore
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);

    Task<string?> GetSeedJwtAsync(CancellationToken cancellationToken = default);

    Task<PublishResult> PublishSeedJwtAsync(string jwt, string message, CancellationToken cancellationToken = default);

    Task<EntriesDocument<AuditEntryDto>> GetAuditAsync(CancellationToken cancellationToken = default);

    Task<PublishResult> ReplaceAuditAsync(IReadOnlyList<AuditEntryDto> entries, CancellationToken cancellationToken = default);

    Task<EntriesDocument<TelemetryAppendRequest>> GetRobotEventsAsync(CancellationToken cancellationToken = default);

    Task<PublishResult> AppendRobotEventAsync(TelemetryAppendRequest entry, CancellationToken cancellationToken = default);
}

public sealed record PublishResult(string Revision);

public sealed class EntriesDocument<T>
{
    public List<T> Entries { get; set; } = new();
}
