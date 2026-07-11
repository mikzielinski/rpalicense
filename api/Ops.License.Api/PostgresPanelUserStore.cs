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

    public async Task EnsureBootstrapAdminAsync(string username, string password, string? githubLogin = null, CancellationToken cancellationToken = default)
    {
        if (await AnyUsersAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await CreateUserAsync(new PanelUserCreateOptions
        {
            Username = username.Trim(),
            PasswordHash = PasswordHasher.Hash(password),
            IsAdmin = true,
            GithubLogin = githubLogin
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PanelUserRecord?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT username, password_hash, is_admin, created_at, github_login, google_email
            FROM panel_user WHERE username = @username
            """,
            connection);
        command.Parameters.AddWithValue("username", username.Trim());
        return await ReadUserAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PanelUserRecord?> FindByGithubLoginAsync(string githubLogin, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeGithubLogin(githubLogin);
        if (normalized is null)
        {
            return null;
        }

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT username, password_hash, is_admin, created_at, github_login, google_email
            FROM panel_user WHERE lower(github_login) = lower(@github_login)
            """,
            connection);
        command.Parameters.AddWithValue("github_login", normalized);
        return await ReadUserAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PanelUserRecord?> FindByGoogleEmailAsync(string googleEmail, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeGoogleEmail(googleEmail);
        if (normalized is null)
        {
            return null;
        }

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT username, password_hash, is_admin, created_at, github_login, google_email
            FROM panel_user WHERE google_email = @google_email
            """,
            connection);
        command.Parameters.AddWithValue("google_email", normalized);
        return await ReadUserAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PanelUserDto>> ListUsersAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT username, is_admin, created_at, github_login, google_email
            FROM panel_user ORDER BY username
            """,
            connection);

        var users = new List<PanelUserDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            users.Add(new PanelUserDto
            {
                Username = reader.GetString(0),
                IsAdmin = reader.GetBoolean(1),
                CreatedAt = FormatTimestamp(reader.GetDateTime(2)),
                GithubLogin = reader.IsDBNull(3) ? null : reader.GetString(3),
                GoogleEmail = reader.IsDBNull(4) ? null : reader.GetString(4)
            });
        }

        return users;
    }

    public async Task CreateUserAsync(PanelUserCreateOptions options, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO panel_user (username, password_hash, is_admin, created_at, github_login, google_email)
            VALUES (@username, @password_hash, @is_admin, NOW(), @github_login, @google_email)
            """,
            connection);
        command.Parameters.AddWithValue("username", options.Username.Trim());
        command.Parameters.AddWithValue("password_hash", options.PasswordHash);
        command.Parameters.AddWithValue("is_admin", options.IsAdmin);
        command.Parameters.AddWithValue("github_login", (object?)NormalizeGithubLogin(options.GithubLogin) ?? DBNull.Value);
        command.Parameters.AddWithValue("google_email", (object?)NormalizeGoogleEmail(options.GoogleEmail) ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> UpdateUserLinksAsync(string username, string? githubLogin, string? googleEmail, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            UPDATE panel_user
            SET github_login = @github_login,
                google_email = @google_email
            WHERE username = @username
            """,
            connection);
        command.Parameters.AddWithValue("username", username.Trim());
        command.Parameters.AddWithValue("github_login", (object?)NormalizeGithubLogin(githubLogin) ?? DBNull.Value);
        command.Parameters.AddWithValue("google_email", (object?)NormalizeGoogleEmail(googleEmail) ?? DBNull.Value);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
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

    private static async Task<PanelUserRecord?> ReadUserAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
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
            CreatedAtUtc = reader.GetDateTime(3),
            GithubLogin = reader.IsDBNull(4) ? null : reader.GetString(4),
            GoogleEmail = reader.IsDBNull(5) ? null : reader.GetString(5)
        };
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static string FormatTimestamp(DateTime value) =>
        new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)).ToString("O", CultureInfo.InvariantCulture);

    private static string? NormalizeGithubLogin(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeGoogleEmail(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private const string SchemaSql =
        """
        CREATE TABLE IF NOT EXISTS panel_user (
            username TEXT PRIMARY KEY,
            password_hash TEXT NOT NULL,
            is_admin BOOLEAN NOT NULL DEFAULT FALSE,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            github_login TEXT,
            google_email TEXT
        );

        ALTER TABLE panel_user ADD COLUMN IF NOT EXISTS github_login TEXT;
        ALTER TABLE panel_user ADD COLUMN IF NOT EXISTS google_email TEXT;

        CREATE UNIQUE INDEX IF NOT EXISTS idx_panel_user_github_login
            ON panel_user (lower(github_login)) WHERE github_login IS NOT NULL;
        CREATE UNIQUE INDEX IF NOT EXISTS idx_panel_user_google_email
            ON panel_user (google_email) WHERE google_email IS NOT NULL;
        """;
}
