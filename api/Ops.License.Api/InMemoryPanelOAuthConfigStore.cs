namespace Ops.License.Api;

public sealed class InMemoryPanelOAuthConfigStore : IPanelOAuthConfigStore
{
    private readonly SecretProtector _protector;
    private readonly Dictionary<string, string> _settings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PanelOAuthProviderConfig> _providers = new(StringComparer.Ordinal)
    {
        ["github"] = new PanelOAuthProviderConfig { Provider = "github" },
        ["google"] = new PanelOAuthProviderConfig { Provider = "google" }
    };

    public InMemoryPanelOAuthConfigStore(ServerSettings settings)
    {
        _protector = new SecretProtector(settings);
    }

    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async Task ImportFromEnvironmentIfEmptyAsync(PanelOAuthEnvBootstrap bootstrap, CancellationToken cancellationToken = default)
    {
        var current = await GetRuntimeConfigAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(current.PanelPublicUrl) ||
            !string.IsNullOrWhiteSpace(current.ApiPublicUrl) ||
            ProviderConfigured(current.Github) ||
            ProviderConfigured(current.Google))
        {
            return;
        }

        await SaveSetupAsync(new PanelOAuthSetupDto
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
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task<PanelOAuthRuntimeConfig> GetRuntimeConfigAsync(CancellationToken cancellationToken = default)
    {
        _settings.TryGetValue("panel_public_url", out var panelUrl);
        _settings.TryGetValue("api_public_url", out var apiUrl);
        return Task.FromResult(new PanelOAuthRuntimeConfig
        {
            PanelPublicUrl = panelUrl ?? string.Empty,
            ApiPublicUrl = apiUrl ?? string.Empty,
            Github = CloneProvider(_providers["github"]),
            Google = CloneProvider(_providers["google"])
        });
    }

    public async Task<PanelOAuthSetupDto> GetSetupAsync(CancellationToken cancellationToken = default)
    {
        var runtime = await GetRuntimeConfigAsync(cancellationToken).ConfigureAwait(false);
        return PanelOAuthConfigMapper.ToSetupDto(runtime);
    }

    public Task SaveSetupAsync(PanelOAuthSetupDto setup, CancellationToken cancellationToken = default)
    {
        return SaveSetupInternalAsync(setup, cancellationToken);
    }

    private async Task SaveSetupInternalAsync(PanelOAuthSetupDto setup, CancellationToken cancellationToken)
    {
        var current = await GetRuntimeConfigAsync(cancellationToken).ConfigureAwait(false);
        var merged = PanelOAuthConfigMapper.ApplySetup(setup, current, _protector);
        _settings["panel_public_url"] = merged.PanelPublicUrl;
        _settings["api_public_url"] = merged.ApiPublicUrl;
        _providers["github"] = CloneProvider(merged.Github);
        _providers["google"] = CloneProvider(merged.Google);
    }

    private static PanelOAuthProviderConfig CloneProvider(PanelOAuthProviderConfig source) =>
        new()
        {
            Provider = source.Provider,
            ClientId = source.ClientId,
            ClientSecret = source.ClientSecret,
            Enabled = source.Enabled
        };

    private static bool ProviderConfigured(PanelOAuthProviderConfig provider) =>
        provider.Enabled &&
        !string.IsNullOrWhiteSpace(provider.ClientId) &&
        !string.IsNullOrWhiteSpace(provider.ClientSecret);
}
