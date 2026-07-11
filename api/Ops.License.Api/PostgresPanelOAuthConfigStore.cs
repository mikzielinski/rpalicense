using System.Globalization;
using Npgsql;

namespace Ops.License.Api;

public sealed class PostgresPanelOAuthConfigStore : IPanelOAuthConfigStore
{
    private const string PanelUrlKey = "panel_public_url";
    private const string ApiUrlKey = "api_public_url";

    private readonly string _connectionString;
    private readonly SecretProtector _protector;

    public PostgresPanelOAuthConfigStore(DatabaseOptions options, ServerSettings settings)
    {
        _connectionString = DatabaseConnection.Normalize(options.ConnectionString);
        _protector = new SecretProtector(settings);
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(SchemaSql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ImportFromEnvironmentIfEmptyAsync(PanelOAuthEnvBootstrap bootstrap, CancellationToken cancellationToken = default)
    {
        var current = await GetRuntimeConfigAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(current.PanelPublicUrl) || !string.IsNullOrWhiteSpace(current.ApiPublicUrl) ||
            ProviderConfigured(current.Github) || ProviderConfigured(current.Google))
        {
            return;
        }

        var setup = new PanelOAuthSetupDto
        {
            PanelPublicUrl = bootstrap.PanelPublicUrl,
            ApiPublicUrl = bootstrap.ApiPublicUrl,
            Github = new PanelOAuthProviderSetupDto
            {
                Enabled = !string.IsNullOrWhiteSpace(bootstrap.GithubClientId) &&
                          !string.IsNullOrWhiteSpace(bootstrap.GithubClientSecret),
                ClientId = bootstrap.GithubClientId,
                ClientSecret = bootstrap.GithubClientSecret
            },
            Google = new PanelOAuthProviderSetupDto
            {
                Enabled = !string.IsNullOrWhiteSpace(bootstrap.GoogleClientId) &&
                          !string.IsNullOrWhiteSpace(bootstrap.GoogleClientSecret),
                ClientId = bootstrap.GoogleClientId,
                ClientSecret = bootstrap.GoogleClientSecret
            }
        };

        if (string.IsNullOrWhiteSpace(setup.PanelPublicUrl) &&
            string.IsNullOrWhiteSpace(setup.ApiPublicUrl) &&
            !setup.Github.Enabled &&
            !setup.Google.Enabled)
        {
            return;
        }

        await SaveSetupAsync(setup, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PanelOAuthRuntimeConfig> GetRuntimeConfigAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var panelUrl = await ReadSettingAsync(connection, PanelUrlKey, cancellationToken).ConfigureAwait(false) ?? string.Empty;
        var apiUrl = await ReadSettingAsync(connection, ApiUrlKey, cancellationToken).ConfigureAwait(false) ?? string.Empty;
        var github = await ReadProviderAsync(connection, "github", cancellationToken).ConfigureAwait(false);
        var google = await ReadProviderAsync(connection, "google", cancellationToken).ConfigureAwait(false);

        return new PanelOAuthRuntimeConfig
        {
            PanelPublicUrl = panelUrl,
            ApiPublicUrl = apiUrl,
            Github = github,
            Google = google
        };
    }

    public async Task<PanelOAuthSetupDto> GetSetupAsync(CancellationToken cancellationToken = default)
    {
        var runtime = await GetRuntimeConfigAsync(cancellationToken).ConfigureAwait(false);
        return PanelOAuthConfigMapper.ToSetupDto(runtime);
    }

    public async Task SaveSetupAsync(PanelOAuthSetupDto setup, CancellationToken cancellationToken = default)
    {
        var current = await GetRuntimeConfigAsync(cancellationToken).ConfigureAwait(false);
        var merged = PanelOAuthConfigMapper.ApplySetup(setup, current, _protector);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await UpsertSettingAsync(connection, PanelUrlKey, merged.PanelPublicUrl, cancellationToken).ConfigureAwait(false);
        await UpsertSettingAsync(connection, ApiUrlKey, merged.ApiPublicUrl, cancellationToken).ConfigureAwait(false);
        await UpsertProviderAsync(connection, merged.Github, cancellationToken).ConfigureAwait(false);
        await UpsertProviderAsync(connection, merged.Google, cancellationToken).ConfigureAwait(false);

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task UpsertSettingAsync(
        NpgsqlConnection connection,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO panel_setting (key, value, updated_at)
            VALUES (@key, @value, NOW())
            ON CONFLICT (key) DO UPDATE
            SET value = EXCLUDED.value,
                updated_at = NOW()
            """,
            connection);
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("value", value ?? string.Empty);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task UpsertProviderAsync(
        NpgsqlConnection connection,
        PanelOAuthProviderConfig provider,
        CancellationToken cancellationToken)
    {
        string? cipher = null;
        if (!string.IsNullOrWhiteSpace(provider.ClientSecret))
        {
            cipher = _protector.Encrypt(provider.ClientSecret);
        }

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO panel_oauth_provider (provider, client_id, client_secret_cipher, enabled, updated_at)
            VALUES (@provider, @client_id, @client_secret_cipher, @enabled, NOW())
            ON CONFLICT (provider) DO UPDATE
            SET client_id = EXCLUDED.client_id,
                client_secret_cipher = COALESCE(EXCLUDED.client_secret_cipher, panel_oauth_provider.client_secret_cipher),
                enabled = EXCLUDED.enabled,
                updated_at = NOW()
            """,
            connection);
        command.Parameters.AddWithValue("provider", provider.Provider);
        command.Parameters.AddWithValue("client_id", provider.ClientId ?? string.Empty);
        command.Parameters.AddWithValue("client_secret_cipher", (object?)cipher ?? DBNull.Value);
        command.Parameters.AddWithValue("enabled", provider.Enabled);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> ReadSettingAsync(
        NpgsqlConnection connection,
        string key,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT value FROM panel_setting WHERE key = @key",
            connection);
        command.Parameters.AddWithValue("key", key);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is string text ? text : null;
    }

    private async Task<PanelOAuthProviderConfig> ReadProviderAsync(
        NpgsqlConnection connection,
        string provider,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT client_id, client_secret_cipher, enabled
            FROM panel_oauth_provider
            WHERE provider = @provider
            """,
            connection);
        command.Parameters.AddWithValue("provider", provider);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return new PanelOAuthProviderConfig { Provider = provider };
        }

        var clientId = reader.GetString(0);
        string secret = string.Empty;
        if (!reader.IsDBNull(1))
        {
            secret = _protector.Decrypt(reader.GetString(1));
        }

        return new PanelOAuthProviderConfig
        {
            Provider = provider,
            ClientId = clientId,
            ClientSecret = secret,
            Enabled = reader.GetBoolean(2)
        };
    }

    private static bool ProviderConfigured(PanelOAuthProviderConfig provider) =>
        provider.Enabled &&
        !string.IsNullOrWhiteSpace(provider.ClientId) &&
        !string.IsNullOrWhiteSpace(provider.ClientSecret);

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private const string SchemaSql =
        """
        CREATE TABLE IF NOT EXISTS panel_setting (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL,
            updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE TABLE IF NOT EXISTS panel_oauth_provider (
            provider TEXT PRIMARY KEY,
            client_id TEXT NOT NULL DEFAULT '',
            client_secret_cipher TEXT,
            enabled BOOLEAN NOT NULL DEFAULT FALSE,
            updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        """;
}
