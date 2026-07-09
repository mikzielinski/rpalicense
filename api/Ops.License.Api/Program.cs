using Ops.License.Api;

var builder = WebApplication.CreateBuilder(args);

var githubOptions = builder.Configuration.GetSection(GitHubOptions.SectionName).Get<GitHubOptions>() ?? new GitHubOptions();
githubOptions.Token = FirstNonEmpty(
    Environment.GetEnvironmentVariable("GITHUB_TOKEN"),
    Environment.GetEnvironmentVariable("OPS_GITHUB_TOKEN"),
    githubOptions.Token) ?? string.Empty;

var apiKeyOptions = builder.Configuration.GetSection(ApiKeyOptions.SectionName).Get<ApiKeyOptions>() ?? new ApiKeyOptions();
apiKeyOptions.Operator = FirstNonEmpty(
    Environment.GetEnvironmentVariable("OPS_API_OPERATOR_KEY"),
    apiKeyOptions.Operator) ?? string.Empty;
apiKeyOptions.Robot = FirstNonEmpty(
    Environment.GetEnvironmentVariable("OPS_API_ROBOT_KEY"),
    apiKeyOptions.Robot) ?? string.Empty;

var corsOptions = builder.Configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>() ?? new CorsOptions();
var allowedOrigins = corsOptions.AllowedOrigins.Length > 0
    ? corsOptions.AllowedOrigins
    : new[] { "https://mikzielinski.github.io" };

builder.Services.AddSingleton(githubOptions);
builder.Services.AddSingleton(apiKeyOptions);
builder.Services.AddHttpClient<GitHubContentsClient>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

if (string.IsNullOrWhiteSpace(githubOptions.Token))
{
    app.Logger.LogWarning("GITHUB_TOKEN is not configured — publish endpoints will fail.");
}

if (string.IsNullOrWhiteSpace(apiKeyOptions.Operator) && string.IsNullOrWhiteSpace(apiKeyOptions.Robot))
{
    app.Logger.LogWarning("OPS_API_OPERATOR_KEY / OPS_API_ROBOT_KEY are not configured — all API calls will be rejected.");
}

app.UseCors();
app.UseMiddleware<ApiKeyAuthMiddleware>();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/v1/seed", async (GitHubContentsClient github, GitHubOptions options, CancellationToken ct) =>
{
    var meta = await github.GetFileAsync(options.SeedPath, ct).ConfigureAwait(false);
    return Results.Ok(new { sha = meta.Sha, jwt = meta.Text.Trim() });
});

app.MapPost("/v1/seed/publish", async (SeedPublishRequest request, GitHubContentsClient github, GitHubOptions options, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Jwt))
    {
        return Results.BadRequest(new { error = "jwt_required" });
    }

    var jwt = request.Jwt.Trim();
    if (!jwt.StartsWith("eyJ", StringComparison.Ordinal))
    {
        return Results.BadRequest(new { error = "invalid_jwt" });
    }

    var message = string.IsNullOrWhiteSpace(request.Message) ? "Update seed.jwt (api)" : request.Message.Trim();
    var result = await github.PublishTextFileAsync(options.SeedPath, $"{jwt}\n", message, ct).ConfigureAwait(false);
    return Results.Ok(new { ok = true, sha = result.Sha });
});

app.MapPost("/v1/audit", async (AuditReplaceRequest request, GitHubContentsClient github, GitHubOptions options, CancellationToken ct) =>
{
    var body = new EntriesDocument<AuditEntryDto>
    {
        Entries = request.Entries.Take(500).ToList()
    };
    var json = System.Text.Json.JsonSerializer.Serialize(body, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    }) + "\n";

    var result = await github.PublishTextFileAsync(options.AuditPath, json, "Update audit-log.json (api)", ct).ConfigureAwait(false);
    return Results.Ok(new { ok = true, sha = result.Sha, count = body.Entries.Count });
});

app.MapPost("/v1/telemetry", async (TelemetryAppendRequest request, GitHubContentsClient github, GitHubOptions options, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.TokenId))
    {
        return Results.BadRequest(new { error = "tokenId_required" });
    }

    var entry = new TelemetryAppendRequest
    {
        AtUtc = string.IsNullOrWhiteSpace(request.AtUtc) ? DateTimeOffset.UtcNow.ToString("O") : request.AtUtc,
        TokenId = request.TokenId,
        Machine = request.Machine,
        Code = request.Code,
        Success = request.Success,
        UsedCache = request.UsedCache,
        Notes = request.Notes,
        ProcessName = request.ProcessName,
        WindowsIdentity = request.WindowsIdentity
    };

    var result = await github.AppendJsonEntryAsync<TelemetryAppendRequest, EntriesDocument<TelemetryAppendRequest>>(
        options.RobotEventsPath,
        entry,
        () => new EntriesDocument<TelemetryAppendRequest>(),
        doc => doc.Entries,
        (doc, entries) => doc.Entries = entries,
        $"robot-check {entry.TokenId} {entry.Code}",
        cancellationToken: ct).ConfigureAwait(false);

    return Results.Ok(new { ok = true, sha = result.Sha });
});

app.Run();

static string? FirstNonEmpty(params string?[] values)
{
    foreach (var value in values)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }
    }

    return null;
}
