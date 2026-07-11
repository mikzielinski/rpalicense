namespace Ops.License.Api;

public sealed class InMemoryPanelUserStore : IPanelUserStore
{
    private readonly Dictionary<string, PanelUserRecord> _users = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _githubIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _googleIndex = new(StringComparer.OrdinalIgnoreCase);

    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task EnsureBootstrapAdminAsync(string username, string password, string? githubLogin = null, CancellationToken cancellationToken = default)
    {
        var normalized = username.Trim();
        if (_users.TryGetValue(normalized, out var existing))
        {
            existing.PasswordHash = PasswordHasher.Hash(password);
            if (!string.IsNullOrWhiteSpace(githubLogin))
            {
                existing.GithubLogin = NormalizeGithubLogin(githubLogin);
                IndexOAuth(existing);
            }
            return Task.CompletedTask;
        }

        if (_users.Count > 0)
        {
            return Task.CompletedTask;
        }

        var record = new PanelUserRecord
        {
            Username = normalized,
            PasswordHash = PasswordHasher.Hash(password),
            IsAdmin = true,
            CreatedAtUtc = DateTime.UtcNow,
            GithubLogin = NormalizeGithubLogin(githubLogin),
            GoogleEmail = null
        };
        _users[record.Username] = record;
        IndexOAuth(record);
        return Task.CompletedTask;
    }

    public Task<PanelUserRecord?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        _users.TryGetValue(username.Trim(), out var user);
        return Task.FromResult(user);
    }

    public Task<PanelUserRecord?> FindByGithubLoginAsync(string githubLogin, CancellationToken cancellationToken = default)
    {
        var key = NormalizeGithubLogin(githubLogin);
        if (key is null || !_githubIndex.TryGetValue(key, out var username))
        {
            return Task.FromResult<PanelUserRecord?>(null);
        }

        _users.TryGetValue(username, out var user);
        return Task.FromResult(user);
    }

    public Task<PanelUserRecord?> FindByGoogleEmailAsync(string googleEmail, CancellationToken cancellationToken = default)
    {
        var key = NormalizeGoogleEmail(googleEmail);
        if (key is null || !_googleIndex.TryGetValue(key, out var username))
        {
            return Task.FromResult<PanelUserRecord?>(null);
        }

        _users.TryGetValue(username, out var user);
        return Task.FromResult(user);
    }

    public Task<IReadOnlyList<PanelUserDto>> ListUsersAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PanelUserDto> users = _users.Values
            .OrderBy(u => u.Username, StringComparer.OrdinalIgnoreCase)
            .Select(ToDto)
            .ToList();
        return Task.FromResult(users);
    }

    public Task CreateUserAsync(PanelUserCreateOptions options, CancellationToken cancellationToken = default)
    {
        var record = new PanelUserRecord
        {
            Username = options.Username.Trim(),
            PasswordHash = options.PasswordHash,
            IsAdmin = options.IsAdmin,
            CreatedAtUtc = DateTime.UtcNow,
            GithubLogin = NormalizeGithubLogin(options.GithubLogin),
            GoogleEmail = NormalizeGoogleEmail(options.GoogleEmail)
        };
        _users[record.Username] = record;
        IndexOAuth(record);
        return Task.CompletedTask;
    }

    public Task<bool> UpdateUserLinksAsync(string username, string? githubLogin, string? googleEmail, CancellationToken cancellationToken = default)
    {
        if (!_users.TryGetValue(username.Trim(), out var record))
        {
            return Task.FromResult(false);
        }

        if (record.GithubLogin is not null)
        {
            _githubIndex.Remove(record.GithubLogin);
        }

        if (record.GoogleEmail is not null)
        {
            _googleIndex.Remove(record.GoogleEmail);
        }

        record.GithubLogin = NormalizeGithubLogin(githubLogin);
        record.GoogleEmail = NormalizeGoogleEmail(googleEmail);
        IndexOAuth(record);
        return Task.FromResult(true);
    }

    public Task<bool> DeleteUserAsync(string username, CancellationToken cancellationToken = default)
    {
        if (!_users.TryGetValue(username.Trim(), out var record))
        {
            return Task.FromResult(false);
        }

        _users.Remove(record.Username);
        if (record.GithubLogin is not null)
        {
            _githubIndex.Remove(record.GithubLogin);
        }

        if (record.GoogleEmail is not null)
        {
            _googleIndex.Remove(record.GoogleEmail);
        }

        return Task.FromResult(true);
    }

    public Task<bool> AnyUsersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_users.Count > 0);

    private void IndexOAuth(PanelUserRecord record)
    {
        if (record.GithubLogin is not null)
        {
            _githubIndex[record.GithubLogin] = record.Username;
        }

        if (record.GoogleEmail is not null)
        {
            _googleIndex[record.GoogleEmail] = record.Username;
        }
    }

    private static PanelUserDto ToDto(PanelUserRecord user) => new()
    {
        Username = user.Username,
        IsAdmin = user.IsAdmin,
        CreatedAt = new DateTimeOffset(DateTime.SpecifyKind(user.CreatedAtUtc, DateTimeKind.Utc)).ToString("O"),
        GithubLogin = user.GithubLogin,
        GoogleEmail = user.GoogleEmail
    };

    private static string? NormalizeGithubLogin(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeGoogleEmail(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}
