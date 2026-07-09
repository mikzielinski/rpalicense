namespace Ops.License.Api;

public enum ApiKeyRole
{
    None,
    Operator,
    Robot
}

public sealed class ApiKeyAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ApiKeyOptions _keys;

    public ApiKeyAuthMiddleware(RequestDelegate next, ApiKeyOptions keys)
    {
        _next = next;
        _keys = keys;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsOptions(context.Request.Method) ||
            context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var role = ResolveRole(ExtractApiKey(context.Request));
        if (role == ApiKeyRole.None)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "invalid_api_key" }).ConfigureAwait(false);
            return;
        }

        if (!IsAllowed(context.Request.Path, context.Request.Method, role))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "forbidden" }).ConfigureAwait(false);
            return;
        }

        context.Items["apiRole"] = role;
        await _next(context).ConfigureAwait(false);
    }

    private ApiKeyRole ResolveRole(string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return ApiKeyRole.None;
        }

        if (!string.IsNullOrWhiteSpace(_keys.Operator) &&
            string.Equals(apiKey, _keys.Operator, StringComparison.Ordinal))
        {
            return ApiKeyRole.Operator;
        }

        if (!string.IsNullOrWhiteSpace(_keys.Robot) &&
            string.Equals(apiKey, _keys.Robot, StringComparison.Ordinal))
        {
            return ApiKeyRole.Robot;
        }

        return ApiKeyRole.None;
    }

    private static bool IsAllowed(PathString path, string method, ApiKeyRole role)
    {
        var p = path.Value ?? string.Empty;

        if (p.StartsWith("/v1/telemetry", StringComparison.OrdinalIgnoreCase))
        {
            return role is ApiKeyRole.Robot or ApiKeyRole.Operator;
        }

        if (!HttpMethods.IsPost(method) && !HttpMethods.IsGet(method))
        {
            return false;
        }

        if (p.StartsWith("/v1/seed", StringComparison.OrdinalIgnoreCase) ||
            p.StartsWith("/v1/audit", StringComparison.OrdinalIgnoreCase))
        {
            return role == ApiKeyRole.Operator;
        }

        return false;
    }

    private static string? ExtractApiKey(HttpRequest request)
    {
        if (request.Headers.TryGetValue("X-Api-Key", out var header) &&
            !string.IsNullOrWhiteSpace(header))
        {
            return header.ToString();
        }

        var authorization = request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return authorization["Bearer ".Length..].Trim();
        }

        return null;
    }
}
