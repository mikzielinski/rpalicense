namespace Ops.License.Api;

public sealed class InMemoryLicenseStore : ILicenseStore
{
    private readonly object _gate = new();
    private string? _seedJwt;
    private string _seedRevision = "test-revision";
    private readonly List<AuditEntryDto> _audit = new();
    private readonly List<TelemetryAppendRequest> _robotEvents = new();

    public void SeedJwt(string jwt) => _seedJwt = jwt.Trim();

    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<string?> GetSeedJwtAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_seedJwt);
        }
    }

    public Task<PublishResult> PublishSeedJwtAsync(string jwt, string message, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _seedJwt = jwt.Trim();
            _seedRevision = Guid.NewGuid().ToString("N")[..12];
            return Task.FromResult(new PublishResult(_seedRevision));
        }
    }

    public Task<EntriesDocument<AuditEntryDto>> GetAuditAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(new EntriesDocument<AuditEntryDto>
            {
                Entries = _audit.ToList()
            });
        }
    }

    public Task<PublishResult> ReplaceAuditAsync(IReadOnlyList<AuditEntryDto> entries, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _audit.Clear();
            _audit.AddRange(entries.Take(500));
            return Task.FromResult(new PublishResult(Guid.NewGuid().ToString("N")[..12]));
        }
    }

    public Task<EntriesDocument<TelemetryAppendRequest>> GetRobotEventsAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(new EntriesDocument<TelemetryAppendRequest>
            {
                Entries = _robotEvents.ToList()
            });
        }
    }

    public Task<PublishResult> AppendRobotEventAsync(TelemetryAppendRequest entry, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _robotEvents.Insert(0, entry);
            if (_robotEvents.Count > 500)
            {
                _robotEvents.RemoveRange(500, _robotEvents.Count - 500);
            }

            return Task.FromResult(new PublishResult(Guid.NewGuid().ToString("N")[..12]));
        }
    }
}
