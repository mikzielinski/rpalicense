namespace Ops.License.Api;

public sealed class InMemoryPanelUserStore : IPanelUserStore
{
    private readonly Dictionary<string, PanelUserRecord> _users = new(StringComparer.OrdinalIgnoreCase);

    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task EnsureBootstrapAdminAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        if (_users.Count > 0)
        {
            return Task.CompletedTask;
        }

        _users[username] = new PanelUserRecord
        {
            Username = username,
            PasswordHash = PasswordHasher.Hash(password),
            IsAdmin = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        return Task.CompletedTask;
    }

    public Task<PanelUserRecord?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        _users.TryGetValue(username.Trim(), out var user);
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

    public Task CreateUserAsync(string username, string passwordHash, bool isAdmin, CancellationToken cancellationToken = default)
    {
        _users[username.Trim()] = new PanelUserRecord
        {
            Username = username.Trim(),
            PasswordHash = passwordHash,
            IsAdmin = isAdmin,
            CreatedAtUtc = DateTime.UtcNow
        };
        return Task.CompletedTask;
    }

    public Task<bool> DeleteUserAsync(string username, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_users.Remove(username.Trim()));
    }

    public Task<bool> AnyUsersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_users.Count > 0);

    private static PanelUserDto ToDto(PanelUserRecord user) => new()
    {
        Username = user.Username,
        IsAdmin = user.IsAdmin,
        CreatedAt = new DateTimeOffset(DateTime.SpecifyKind(user.CreatedAtUtc, DateTimeKind.Utc)).ToString("O")
    };
}
