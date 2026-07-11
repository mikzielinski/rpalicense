using System.Globalization;
using Npgsql;

namespace Ops.License.Api;

public sealed class PostgresLicenseStore : ILicenseStore
{
    private const int MaxEntries = 500;
    private readonly string _connectionString;

    public PostgresLicenseStore(DatabaseOptions options)
    {
        _connectionString = NormalizeConnectionString(options.ConnectionString);
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(SchemaSql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> GetSeedJwtAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT jwt FROM catalog_seed WHERE id = 1",
            connection);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is string jwt ? jwt.Trim() : null;
    }

    public async Task<PublishResult> PublishSeedJwtAsync(string jwt, string message, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO catalog_seed (id, jwt, updated_at, revision)
            VALUES (1, @jwt, NOW(), gen_random_uuid()::text)
            ON CONFLICT (id) DO UPDATE
            SET jwt = EXCLUDED.jwt,
                updated_at = NOW(),
                revision = gen_random_uuid()::text
            RETURNING revision
            """,
            connection);
        command.Parameters.AddWithValue("jwt", jwt.Trim());
        var revision = (string?)await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
                       ?? "ok";
        return new PublishResult(revision);
    }

    public async Task<EntriesDocument<AuditEntryDto>> GetAuditAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT at_utc, who, action, token_id, code, success, notes
            FROM audit_entry
            ORDER BY at_utc DESC
            LIMIT @limit
            """,
            connection);
        command.Parameters.AddWithValue("limit", MaxEntries);

        var entries = new List<AuditEntryDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            entries.Add(new AuditEntryDto
            {
                AtUtc = FormatTimestamp(reader.GetDateTime(0)),
                Who = reader.GetString(1),
                Action = reader.GetString(2),
                TokenId = reader.GetString(3),
                Code = reader.GetString(4),
                Success = reader.GetBoolean(5),
                Notes = reader.GetString(6)
            });
        }

        return new EntriesDocument<AuditEntryDto> { Entries = entries };
    }

    public async Task<PublishResult> ReplaceAuditAsync(IReadOnlyList<AuditEntryDto> entries, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var delete = new NpgsqlCommand("DELETE FROM audit_entry", connection, transaction))
        {
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var limited = entries.Take(MaxEntries).ToList();
        foreach (var entry in limited)
        {
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO audit_entry (at_utc, who, action, token_id, code, success, notes)
                VALUES (@at_utc, @who, @action, @token_id, @code, @success, @notes)
                """,
                connection,
                transaction);
            insert.Parameters.AddWithValue("at_utc", ParseTimestamp(entry.AtUtc));
            insert.Parameters.AddWithValue("who", entry.Who);
            insert.Parameters.AddWithValue("action", entry.Action);
            insert.Parameters.AddWithValue("token_id", entry.TokenId);
            insert.Parameters.AddWithValue("code", entry.Code);
            insert.Parameters.AddWithValue("success", entry.Success);
            insert.Parameters.AddWithValue("notes", entry.Notes);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new PublishResult(Guid.NewGuid().ToString("N")[..12]);
    }

    public async Task<EntriesDocument<TelemetryAppendRequest>> GetRobotEventsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT at_utc, token_id, machine, code, success, used_cache, notes, process_name, windows_identity
            FROM robot_event
            ORDER BY at_utc DESC
            LIMIT @limit
            """,
            connection);
        command.Parameters.AddWithValue("limit", MaxEntries);

        var entries = new List<TelemetryAppendRequest>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            entries.Add(new TelemetryAppendRequest
            {
                AtUtc = FormatTimestamp(reader.GetDateTime(0)),
                TokenId = reader.GetString(1),
                Machine = reader.GetString(2),
                Code = reader.GetString(3),
                Success = reader.GetBoolean(4),
                UsedCache = reader.GetBoolean(5),
                Notes = reader.GetString(6),
                ProcessName = reader.GetString(7),
                WindowsIdentity = reader.GetString(8)
            });
        }

        return new EntriesDocument<TelemetryAppendRequest> { Entries = entries };
    }

    public async Task<PublishResult> AppendRobotEventAsync(TelemetryAppendRequest entry, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var insert = new NpgsqlCommand(
                         """
                         INSERT INTO robot_event
                             (at_utc, token_id, machine, code, success, used_cache, notes, process_name, windows_identity)
                         VALUES
                             (@at_utc, @token_id, @machine, @code, @success, @used_cache, @notes, @process_name, @windows_identity)
                         """,
                         connection,
                         transaction))
        {
            insert.Parameters.AddWithValue("at_utc", ParseTimestamp(entry.AtUtc));
            insert.Parameters.AddWithValue("token_id", entry.TokenId);
            insert.Parameters.AddWithValue("machine", entry.Machine);
            insert.Parameters.AddWithValue("code", entry.Code);
            insert.Parameters.AddWithValue("success", entry.Success);
            insert.Parameters.AddWithValue("used_cache", entry.UsedCache);
            insert.Parameters.AddWithValue("notes", entry.Notes);
            insert.Parameters.AddWithValue("process_name", entry.ProcessName);
            insert.Parameters.AddWithValue("windows_identity", entry.WindowsIdentity);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var trim = new NpgsqlCommand(
                         """
                         DELETE FROM robot_event
                         WHERE id NOT IN (
                             SELECT id FROM robot_event ORDER BY at_utc DESC LIMIT @limit
                         )
                         """,
                         connection,
                         transaction))
        {
            trim.Parameters.AddWithValue("limit", MaxEntries);
            await trim.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new PublishResult(Guid.NewGuid().ToString("N")[..12]);
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static string NormalizeConnectionString(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        if (raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return ConvertUriToNpgsql(raw);
        }

        return raw;
    }

    private static string ConvertUriToNpgsql(string uri)
    {
        var parsed = new Uri(uri);
        var userInfo = parsed.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = parsed.Host,
            Port = parsed.Port > 0 ? parsed.Port : 5432,
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty,
            Database = parsed.AbsolutePath.TrimStart('/'),
            SslMode = SslMode.Require
        };

        if (!string.IsNullOrWhiteSpace(parsed.Query))
        {
            foreach (var pair in parsed.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                if (parts.Length != 2)
                {
                    continue;
                }

                var key = parts[0];
                var value = Uri.UnescapeDataString(parts[1]);
                if (key.Equals("sslmode", StringComparison.OrdinalIgnoreCase))
                {
                    builder.SslMode = value.ToLowerInvariant() switch
                    {
                        "disable" => SslMode.Disable,
                        "prefer" => SslMode.Prefer,
                        "require" => SslMode.Require,
                        "verify-ca" => SslMode.VerifyCA,
                        "verify-full" => SslMode.VerifyFull,
                        _ => SslMode.Require
                    };
                }
            }
        }

        return builder.ConnectionString;
    }

    private static DateTime ParseTimestamp(string value)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed.UtcDateTime;
        }

        return DateTime.UtcNow;
    }

    private static string FormatTimestamp(DateTime value) =>
        new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)).ToString("O");

    private const string SchemaSql =
        """
        CREATE TABLE IF NOT EXISTS catalog_seed (
            id INT PRIMARY KEY DEFAULT 1 CHECK (id = 1),
            jwt TEXT NOT NULL,
            updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            revision TEXT NOT NULL DEFAULT gen_random_uuid()::text
        );

        CREATE TABLE IF NOT EXISTS audit_entry (
            id BIGSERIAL PRIMARY KEY,
            at_utc TIMESTAMPTZ NOT NULL,
            who TEXT NOT NULL,
            action TEXT NOT NULL,
            token_id TEXT NOT NULL DEFAULT '',
            code TEXT NOT NULL DEFAULT '',
            success BOOLEAN NOT NULL DEFAULT FALSE,
            notes TEXT NOT NULL DEFAULT ''
        );

        CREATE INDEX IF NOT EXISTS idx_audit_entry_at_utc ON audit_entry (at_utc DESC);

        CREATE TABLE IF NOT EXISTS robot_event (
            id BIGSERIAL PRIMARY KEY,
            at_utc TIMESTAMPTZ NOT NULL,
            token_id TEXT NOT NULL,
            machine TEXT NOT NULL,
            code TEXT NOT NULL,
            success BOOLEAN NOT NULL,
            used_cache BOOLEAN NOT NULL DEFAULT FALSE,
            notes TEXT NOT NULL DEFAULT '',
            process_name TEXT NOT NULL DEFAULT '',
            windows_identity TEXT NOT NULL DEFAULT ''
        );

        CREATE INDEX IF NOT EXISTS idx_robot_event_at_utc ON robot_event (at_utc DESC);
        """;
}
