using System.Text.Json;
using Ops.License.Api;

var connectionString = FirstNonEmpty(
    Environment.GetEnvironmentVariable("DATABASE_URL"),
    Environment.GetEnvironmentVariable("NEON_DATABASE_URL"),
    args.FirstOrDefault(a => a.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)));

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("DATABASE_URL or NEON_DATABASE_URL is required.");
    return 1;
}

var store = new PostgresLicenseStore(new DatabaseOptions { ConnectionString = connectionString });
await store.EnsureSchemaAsync();

var seedPath = ResolvePath("docs/assets/seed.jwt");
if (File.Exists(seedPath))
{
    var jwt = (await File.ReadAllTextAsync(seedPath)).Trim();
    if (!string.IsNullOrWhiteSpace(jwt))
    {
        var result = await store.PublishSeedJwtAsync(jwt, "Bootstrap seed from docs/assets/seed.jwt");
        Console.WriteLine($"Seeded catalog_seed (revision={result.Revision})");
    }
}

var auditPath = ResolvePath("docs/assets/audit-log.json");
if (File.Exists(auditPath))
{
    var auditJson = await File.ReadAllTextAsync(auditPath);
    var auditDoc = JsonSerializer.Deserialize<EntriesDocument<AuditEntryDto>>(auditJson, JsonOptions.Web)
                   ?? new EntriesDocument<AuditEntryDto>();
    if (auditDoc.Entries.Count > 0)
    {
        var result = await store.ReplaceAuditAsync(auditDoc.Entries);
        Console.WriteLine($"Seeded {auditDoc.Entries.Count} audit entries (revision={result.Revision})");
    }
}

var eventsPath = ResolvePath("docs/assets/robot-events.json");
if (File.Exists(eventsPath))
{
    var eventsJson = await File.ReadAllTextAsync(eventsPath);
    var eventsDoc = JsonSerializer.Deserialize<EntriesDocument<TelemetryAppendRequest>>(eventsJson, JsonOptions.Web)
                    ?? new EntriesDocument<TelemetryAppendRequest>();
    foreach (var entry in eventsDoc.Entries.AsEnumerable().Reverse())
    {
        await store.AppendRobotEventAsync(entry);
    }

    Console.WriteLine($"Seeded {eventsDoc.Entries.Count} robot events");
}

Console.WriteLine("Neon bootstrap complete.");
return 0;

static string? FirstNonEmpty(params string?[] values)
{
    foreach (var value in values)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }
    }

    return null;
}

static string ResolvePath(string relativePath)
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        var candidate = Path.Combine(dir.FullName, relativePath);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        dir = dir.Parent;
    }

    return Path.GetFullPath(relativePath);
}

internal static class JsonOptions
{
    internal static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
}
