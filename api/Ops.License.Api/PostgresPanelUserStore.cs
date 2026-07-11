using System.Globalization;
using Npgsql;

namespace Ops.License.Api;

public sealed class PostgresPanelUserStore : IPanelUserStore
{
    private readonly string _connectionString;

    public PostgresPanelUserStore(DatabaseOptions options)
    {
        _connectionString = DatabaseConnection.Normalize(options.ConnectionString);
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(SchemaSql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task EnsureBootstrapAdminAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        if (await AnyUsersAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await CreateUserAsync(username.Trim(), PasswordHasher.Hash(password), isAdmin: true, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<PanelUserRecord?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT username, password_hash, is_admin, created_at FROM panel_user WHERE username = @username",
            connection);
        command.Parameters.AddWithValue("username", username.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new PanelUserRecord
        {
            Username = reader.GetString(0),
            PasswordHash = reader.GetString(1),
            IsAdmin = reader.GetBoolean(2),
            CreatedAtUtc = reader.GetDateTime(3)
        };
    }

    public async Task<IReadOnlyList<PanelUserDto>> ListUsersAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT username, is_admin, created_at FROM panel_user ORDER BY username",
            connection);

        var users = new List<PanelUserDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            users.Add(new PanelUserDto
            {
                Username = reader.GetString(0),
                IsAdmin = reader.GetBoolean(1),
                CreatedAt = FormatTimestamp(reader.GetDateTime(2))
            });
        }

        return users;
    }

    public async Task CreateUserAsync(string username, string passwordHash, bool isAdmin, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO panel_user (username, password_hash, is_admin, created_at)
            VALUES (@username, @password_hash, @is_admin, NOW())
            """,
            connection);
        command.Parameters.AddWithValue("username", username.Trim());
        command.Parameters.AddWithValue("password_hash", passwordHash);
        command.Parameters.AddWithValue("is_admin", isAdmin);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteUserAsync(string username, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "DELETE FROM panel_user WHERE username = @username",
            connection);
        command.Parameters.AddWithValue("username", username.Trim());
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    public async Task<bool> AnyUsersAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM panel_user)", connection);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? false);
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static string FormatTimestamp(DateTime value) =>
        new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)).ToString("O", CultureInfo.InvariantCulture);

    private const string SchemaSql =
        """
        CREATE TABLE IF NOT EXISTS panel_user (
            username TEXT PRIMARY KEY,
            password_hash TEXT NOT NULL,
            is_admin BOOLEAN NOT NULL DEFAULT FALSE,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        """;
}
