namespace Ops.License.Api;

public sealed class PanelOAuthOptions
{
    public const string SectionName = "PanelOAuth";

    public string PanelUrl { get; set; } = "https://mikzielinski.github.io/rpalicense";
    public string ApiPublicUrl { get; set; } = "https://rpalicense.fly.dev";
    public string GithubClientId { get; set; } = string.Empty;
    public string GithubClientSecret { get; set; } = string.Empty;
    public string GoogleClientId { get; set; } = string.Empty;
    public string GoogleClientSecret { get; set; } = string.Empty;
}
