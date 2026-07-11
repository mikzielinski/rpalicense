namespace Ops.License.Api;

public sealed class PanelOAuthProviderConfig
{
    public string Provider { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}

public sealed class PanelOAuthRuntimeConfig
{
    public string PanelPublicUrl { get; set; } = string.Empty;
    public string ApiPublicUrl { get; set; } = string.Empty;
    public PanelOAuthProviderConfig Github { get; set; } = new() { Provider = "github" };
    public PanelOAuthProviderConfig Google { get; set; } = new() { Provider = "google" };

    public bool GithubEnabled =>
        Github.Enabled &&
        !string.IsNullOrWhiteSpace(Github.ClientId) &&
        !string.IsNullOrWhiteSpace(Github.ClientSecret);

    public bool GoogleEnabled =>
        Google.Enabled &&
        !string.IsNullOrWhiteSpace(Google.ClientId) &&
        !string.IsNullOrWhiteSpace(Google.ClientSecret);
}

public sealed class PanelOAuthSetupDto
{
    public string PanelPublicUrl { get; set; } = string.Empty;
    public string ApiPublicUrl { get; set; } = string.Empty;
    public PanelOAuthProviderSetupDto Github { get; set; } = new();
    public PanelOAuthProviderSetupDto Google { get; set; } = new();
}

public sealed class PanelOAuthProviderSetupDto
{
    public bool Enabled { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public bool SecretConfigured { get; set; }
    public string SecretHint { get; set; } = string.Empty;
    public string CallbackUrl { get; set; } = string.Empty;
}

public interface IPanelOAuthConfigStore
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);

    Task ImportFromEnvironmentIfEmptyAsync(PanelOAuthEnvBootstrap bootstrap, CancellationToken cancellationToken = default);

    Task<PanelOAuthRuntimeConfig> GetRuntimeConfigAsync(CancellationToken cancellationToken = default);

    Task<PanelOAuthSetupDto> GetSetupAsync(CancellationToken cancellationToken = default);

    Task SaveSetupAsync(PanelOAuthSetupDto setup, CancellationToken cancellationToken = default);
}

public sealed class PanelOAuthEnvBootstrap
{
    public string PanelPublicUrl { get; set; } = string.Empty;
    public string ApiPublicUrl { get; set; } = string.Empty;
    public string GithubClientId { get; set; } = string.Empty;
    public string GithubClientSecret { get; set; } = string.Empty;
    public string GoogleClientId { get; set; } = string.Empty;
    public string GoogleClientSecret { get; set; } = string.Empty;
}
