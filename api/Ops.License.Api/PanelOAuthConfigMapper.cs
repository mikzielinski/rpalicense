namespace Ops.License.Api;

internal static class PanelOAuthConfigMapper
{
    internal static PanelOAuthSetupDto ToSetupDto(PanelOAuthRuntimeConfig runtime)
    {
        var api = runtime.ApiPublicUrl.TrimEnd('/');
        return new PanelOAuthSetupDto
        {
            PanelPublicUrl = runtime.PanelPublicUrl,
            ApiPublicUrl = runtime.ApiPublicUrl,
            Github = ToProviderSetup(runtime.Github, $"{api}/v1/panel/oauth/github/callback"),
            Google = ToProviderSetup(runtime.Google, $"{api}/v1/panel/oauth/google/callback")
        };
    }

    internal static PanelOAuthProviderSetupDto ToProviderSetup(PanelOAuthProviderConfig provider, string callbackUrl) =>
        new()
        {
            Enabled = provider.Enabled,
            ClientId = provider.ClientId,
            SecretConfigured = !string.IsNullOrWhiteSpace(provider.ClientSecret),
            SecretHint = SecretProtector.Mask(provider.ClientSecret),
            CallbackUrl = callbackUrl
        };

    internal static PanelOAuthRuntimeConfig ApplySetup(
        PanelOAuthSetupDto setup,
        PanelOAuthRuntimeConfig current,
        SecretProtector protector)
    {
        return new PanelOAuthRuntimeConfig
        {
            PanelPublicUrl = NormalizeUrl(setup.PanelPublicUrl) ?? current.PanelPublicUrl,
            ApiPublicUrl = NormalizeUrl(setup.ApiPublicUrl) ?? current.ApiPublicUrl,
            Github = MergeProvider(setup.Github, current.Github, protector),
            Google = MergeProvider(setup.Google, current.Google, protector)
        };
    }

    private static PanelOAuthProviderConfig MergeProvider(
        PanelOAuthProviderSetupDto incoming,
        PanelOAuthProviderConfig current,
        SecretProtector protector)
    {
        var secret = incoming.ClientSecret?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(secret))
        {
            secret = current.ClientSecret;
        }

        return new PanelOAuthProviderConfig
        {
            Provider = current.Provider,
            Enabled = incoming.Enabled,
            ClientId = incoming.ClientId?.Trim() ?? string.Empty,
            ClientSecret = secret
        };
    }

    internal static string? NormalizeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().TrimEnd('/');
    }
}
