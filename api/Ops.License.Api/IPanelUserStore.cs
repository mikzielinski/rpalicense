namespace Ops.License.Api;

public sealed class PanelUserDto
{
    public string Username { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string? GithubLogin { get; set; }
    public string? GoogleEmail { get; set; }
}

public sealed class PanelUserCreateOptions
{
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public string? GithubLogin { get; set; }
    public string? GoogleEmail { get; set; }
}

public interface IPanelUserStore
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);

    Task EnsureBootstrapAdminAsync(string username, string password, string? githubLogin = null, CancellationToken cancellationToken = default);

    Task<PanelUserRecord?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default);

    Task<PanelUserRecord?> FindByGithubLoginAsync(string githubLogin, CancellationToken cancellationToken = default);

    Task<PanelUserRecord?> FindByGoogleEmailAsync(string googleEmail, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PanelUserDto>> ListUsersAsync(CancellationToken cancellationToken = default);

    Task CreateUserAsync(PanelUserCreateOptions options, CancellationToken cancellationToken = default);

    Task<bool> UpdateUserLinksAsync(string username, string? githubLogin, string? googleEmail, CancellationToken cancellationToken = default);

    Task<bool> DeleteUserAsync(string username, CancellationToken cancellationToken = default);

    Task<bool> AnyUsersAsync(CancellationToken cancellationToken = default);
}

public sealed class PanelUserRecord
{
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string? GithubLogin { get; set; }
    public string? GoogleEmail { get; set; }
}
