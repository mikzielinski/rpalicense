namespace Ops.License.Api;

public static class SessionAuth
{
    public static bool TryGetBearer(HttpRequest request, out string token)
    {
        token = string.Empty;
        var authorization = request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        token = authorization["Bearer ".Length..].Trim();
        return !string.IsNullOrWhiteSpace(token);
    }

    public static bool RequireOperator(HttpRequest request, SessionTokenService sessions, out SessionClaims claims)
    {
        claims = new SessionClaims();
        if (!TryGetBearer(request, out var token) ||
            !sessions.TryValidate(token, "operator", out claims))
        {
            return false;
        }

        return true;
    }

    public static bool RequireAdmin(HttpRequest request, SessionTokenService sessions, out SessionClaims claims)
    {
        if (!RequireOperator(request, sessions, out claims) || !claims.IsAdmin)
        {
            claims = new SessionClaims();
            return false;
        }

        return true;
    }

    public static bool RequireRuntime(HttpRequest request, SessionTokenService sessions, out SessionClaims claims)
    {
        claims = new SessionClaims();
        if (!TryGetBearer(request, out var token) ||
            !sessions.TryValidate(token, "runtime", out claims))
        {
            return false;
        }

        return true;
    }
}
