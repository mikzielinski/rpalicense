namespace Ops.License.Api;

public sealed class GitHubOptions
{
    public const string SectionName = "GitHub";

    public string Token { get; set; } = string.Empty;
    public string Owner { get; set; } = "mikzielinski";
    public string Repo { get; set; } = "rpalicense";
    public string Branch { get; set; } = "main";
    public string SeedPath { get; set; } = "docs/assets/seed.jwt";
    public string AuditPath { get; set; } = "docs/assets/audit-log.json";
    public string RobotEventsPath { get; set; } = "docs/assets/robot-events.json";
}

public sealed class ApiKeyOptions
{
    public const string SectionName = "ApiKeys";

    public string Operator { get; set; } = string.Empty;
    public string Robot { get; set; } = string.Empty;
}

public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();
}
