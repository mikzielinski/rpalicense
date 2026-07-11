using System.Text.Json;
using System.Text.Json.Serialization;
using Ops.License.Api;
using UiPath.System.RoboticSecurity;

var builder = WebApplication.CreateBuilder(args);

var databaseOptions = builder.Configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
databaseOptions.ConnectionString = FirstNonEmpty(
    Environment.GetEnvironmentVariable("DATABASE_URL"),
    Environment.GetEnvironmentVariable("NEON_DATABASE_URL"),
    builder.Configuration.GetConnectionString("Default"),
    databaseOptions.ConnectionString) ?? string.Empty;

var serverSettings = builder.Configuration.GetSection(ServerSettings.SectionName).Get<ServerSettings>() ?? new ServerSettings();
serverSettings.Pepper = FirstNonEmpty(Environment.GetEnvironmentVariable("OPS_SEED_PEPPER"), serverSettings.Pepper) ?? serverSettings.Pepper;
serverSettings.EnvelopePepper = FirstNonEmpty(Environment.GetEnvironmentVariable("OPS_SEED_ENVELOPE_PEPPER"), serverSettings.EnvelopePepper) ?? serverSettings.EnvelopePepper;
serverSettings.EnvelopeSigningKey = FirstNonEmpty(Environment.GetEnvironmentVariable("OPS_SEED_ENVELOPE_SIGNING_KEY"), serverSettings.EnvelopeSigningKey) ?? serverSettings.EnvelopeSigningKey;
serverSettings.EnvelopeIssuer = FirstNonEmpty(Environment.GetEnvironmentVariable("OPS_SEED_ENVELOPE_ISSUER"), serverSettings.EnvelopeIssuer) ?? serverSettings.EnvelopeIssuer;
serverSettings.EnvelopeAudience = FirstNonEmpty(Environment.GetEnvironmentVariable("OPS_SEED_ENVELOPE_AUDIENCE"), serverSettings.EnvelopeAudience) ?? serverSettings.EnvelopeAudience;
serverSettings.PublicSealKeyPem = FirstNonEmpty(Environment.GetEnvironmentVariable("OPS_SEED_PUBLIC_SEAL_KEY_PEM"), serverSettings.PublicSealKeyPem) ?? serverSettings.PublicSealKeyPem;
serverSettings.OperatorSecret = FirstNonEmpty(Environment.GetEnvironmentVariable("OPS_OPERATOR_SECRET"), serverSettings.OperatorSecret) ?? string.Empty;
serverSettings.SessionSigningKey = FirstNonEmpty(Environment.GetEnvironmentVariable("OPS_SESSION_SIGNING_KEY"), serverSettings.SessionSigningKey) ?? string.Empty;

var panelOAuthOptions = builder.Configuration.GetSection(PanelOAuthOptions.SectionName).Get<PanelOAuthOptions>() ?? new PanelOAuthOptions();
panelOAuthOptions.PanelUrl = FirstNonEmpty(Environment.GetEnvironmentVariable("OPS_PANEL_PUBLIC_URL"), panelOAuthOptions.PanelUrl) ?? panelOAuthOptions.PanelUrl;
panelOAuthOptions.ApiPublicUrl = FirstNonEmpty(Environment.GetEnvironmentVariable("OPS_API_PUBLIC_URL"), panelOAuthOptions.ApiPublicUrl) ?? panelOAuthOptions.ApiPublicUrl;
panelOAuthOptions.GithubClientId = FirstNonEmpty(Environment.GetEnvironmentVariable("OPS_OAUTH_GITHUB_CLIENT_ID"), panelOAuthOptions.GithubClientId) ?? string.Empty;
panelOAuthOptions.GithubClientSecret = FirstNonEmpty(Environment.GetEnvironmentVariable("OPS_OAUTH_GITHUB_CLIENT_SECRET"), panelOAuthOptions.GithubClientSecret) ?? string.Empty;
panelOAuthOptions.GoogleClientId = FirstNonEmpty(Environment.GetEnvironmentVariable("OPS_OAUTH_GOOGLE_CLIENT_ID"), panelOAuthOptions.GoogleClientId) ?? string.Empty;
panelOAuthOptions.GoogleClientSecret = FirstNonEmpty(Environment.GetEnvironmentVariable("OPS_OAUTH_GOOGLE_CLIENT_SECRET"), panelOAuthOptions.GoogleClientSecret) ?? string.Empty;

var corsOptions = builder.Configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>() ?? new CorsOptions();
var allowedOrigins = corsOptions.AllowedOrigins.Length > 0
    ? corsOptions.AllowedOrigins
    : new[] { "https://mikzielinski.github.io" };

builder.Services.AddSingleton(databaseOptions);
builder.Services.AddSingleton(serverSettings);
builder.Services.AddSingleton(panelOAuthOptions);
builder.Services.AddSingleton<HandshakeService>();
builder.Services.AddSingleton<SessionTokenService>();
builder.Services.AddSingleton<CatalogService>();
builder.Services.AddHttpClient<PanelOAuthService>();

if (string.IsNullOrWhiteSpace(databaseOptions.ConnectionString))
{
    builder.Services.AddSingleton<ILicenseStore, InMemoryLicenseStore>();
    builder.Services.AddSingleton<IPanelUserStore, InMemoryPanelUserStore>();
}
else
{
    builder.Services.AddSingleton<ILicenseStore, PostgresLicenseStore>();
    builder.Services.AddSingleton<IPanelUserStore, PostgresPanelUserStore>();
}

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

var store = app.Services.GetRequiredService<ILicenseStore>();
var panelUsers = app.Services.GetRequiredService<IPanelUserStore>();
try
{
    await store.EnsureSchemaAsync().ConfigureAwait(false);
    await panelUsers.EnsureSchemaAsync().ConfigureAwait(false);

    var panelAdminUser = FirstNonEmpty(Environment.GetEnvironmentVariable("OPS_PANEL_ADMIN_USERNAME"), "mikolaj") ?? "mikolaj";
    var panelAdminPassword = FirstNonEmpty(
        Environment.GetEnvironmentVariable("OPS_PANEL_ADMIN_PASSWORD"),
        serverSettings.OperatorSecret);
    var panelAdminGithub = FirstNonEmpty(Environment.GetEnvironmentVariable("OPS_PANEL_ADMIN_GITHUB_LOGIN"));
    if (!string.IsNullOrWhiteSpace(panelAdminPassword))
    {
        await panelUsers.EnsureBootstrapAdminAsync(panelAdminUser, panelAdminPassword, panelAdminGithub).ConfigureAwait(false);
    }
    else
    {
        app.Logger.LogWarning("OPS_PANEL_ADMIN_PASSWORD is not configured — panel login accounts were not bootstrapped.");
    }

    app.Logger.LogInformation("Database schema is ready.");
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "Database schema initialization failed.");
}

if (string.IsNullOrWhiteSpace(databaseOptions.ConnectionString))
{
    app.Logger.LogWarning("DATABASE_URL is not configured — using in-memory storage (development only).");
}

if (string.IsNullOrWhiteSpace(serverSettings.OperatorSecret))
{
    app.Logger.LogWarning("OPS_OPERATOR_SECRET is not configured — operator endpoints will fail.");
}

if (string.IsNullOrWhiteSpace(serverSettings.SessionSigningKey))
{
    app.Logger.LogWarning("OPS_SESSION_SIGNING_KEY is not configured — session tokens will fail.");
}

app.UseCors();

app.MapGet("/health", () => Results.Ok(new { status = "ok", storage = string.IsNullOrWhiteSpace(databaseOptions.ConnectionString) ? "memory" : "postgres" }));

app.MapPost("/v1/runtime/challenge", (RuntimeChallengeRequest request, HandshakeService handshake) =>
{
    var machine = (request.Machine ?? string.Empty).Trim().ToUpperInvariant();
    if (string.IsNullOrWhiteSpace(machine))
    {
        return Results.BadRequest(new { error = "machine_required" });
    }

    var challenge = handshake.CreateRuntimeChallenge(machine);
    return Results.Ok(new ChallengeResponse
    {
        ChallengeId = challenge.ChallengeId,
        ServerNonce = challenge.ServerNonce,
        ExpiresAt = challenge.ExpiresAt.ToString("O")
    });
});

app.MapPost("/v1/runtime/authorize", async (
    RuntimeAuthorizeRequest request,
    HandshakeService handshake,
    CatalogService catalog,
    SessionTokenService sessions,
    ServerSettings settings,
    CancellationToken ct) =>
{
    var machine = (request.Machine ?? string.Empty).Trim().ToUpperInvariant();
    var tokenId = (request.TokenId ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(tokenId) || string.IsNullOrWhiteSpace(machine))
    {
        return Results.BadRequest(new { success = false, code = "boot-0x01" });
    }

    if (!handshake.TryConsumeRuntimeChallenge(request.ChallengeId ?? string.Empty, out var challenge) ||
        !string.Equals(challenge.Subject, machine, StringComparison.OrdinalIgnoreCase))
    {
        return Results.Json(new AuthorizeResponse { Success = false, Code = "boot-0x63" });
    }

    if (!HandshakeProof.VerifyRuntimeProof(
            tokenId,
            settings.Pepper,
            request.ChallengeId ?? string.Empty,
            challenge.ServerNonce,
            request.ClientNonce ?? string.Empty,
            machine,
            tokenId,
            request.Proof ?? string.Empty))
    {
        return Results.Json(new AuthorizeResponse { Success = false, Code = "boot-0x65" });
    }

    try
    {
        var catalogDoc = await catalog.GetCatalogAsync(ct).ConfigureAwait(false);
        var key = Crypto.DeriveAesKey(tokenId, settings.Pepper);
        var entry = LicenseCatalog.ResolveEntry(catalogDoc, tokenId, machine);
        var profile = LicenseCatalog.MaterializeProfile(entry, tokenId, key);
        var sessionToken = sessions.IssueRuntime(tokenId, machine);
        return Results.Json(new AuthorizeResponse
        {
            Success = true,
            Code = "boot-ok-remote",
            SessionToken = sessionToken,
            Profile = profile
        });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Json(new AuthorizeResponse { Success = false, Code = ex.Message });
    }
    catch (System.Security.Cryptography.CryptographicException ex)
    {
        return Results.Json(new AuthorizeResponse { Success = false, Code = ex.Message });
    }
    catch (FileNotFoundException ex)
    {
        return Results.Json(new AuthorizeResponse { Success = false, Code = ex.Message });
    }
});

app.MapPost("/v1/runtime/telemetry", async (
    HttpRequest httpRequest,
    TelemetryAppendRequest request,
    SessionTokenService sessions,
    ILicenseStore store,
    CancellationToken ct) =>
{
    if (!SessionAuth.RequireRuntime(httpRequest, sessions, out var claims))
    {
        return Results.Unauthorized();
    }

    if (!string.Equals(claims.TokenId, request.TokenId, StringComparison.Ordinal) ||
        !string.Equals(claims.Machine, request.Machine, StringComparison.OrdinalIgnoreCase))
    {
        return Results.Forbid();
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

    var result = await store.AppendRobotEventAsync(entry, ct).ConfigureAwait(false);
    return Results.Ok(new { ok = true, revision = result.Revision });
});

app.MapPost("/v1/operator/challenge", (OperatorChallengeRequest request, HandshakeService handshake) =>
{
    var operatorId = (request.OperatorId ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(operatorId))
    {
        return Results.BadRequest(new { error = "operatorId_required" });
    }

    var challenge = handshake.CreateOperatorChallenge(operatorId);
    return Results.Ok(new ChallengeResponse
    {
        ChallengeId = challenge.ChallengeId,
        ServerNonce = challenge.ServerNonce,
        ExpiresAt = challenge.ExpiresAt.ToString("O")
    });
});

app.MapPost("/v1/operator/session", (
    OperatorSessionRequest request,
    HandshakeService handshake,
    SessionTokenService sessions,
    ServerSettings settings) =>
{
    var operatorId = (request.OperatorId ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(operatorId) || string.IsNullOrWhiteSpace(settings.OperatorSecret))
    {
        return Results.BadRequest(new { error = "invalid_request" });
    }

    if (!handshake.TryConsumeOperatorChallenge(request.ChallengeId ?? string.Empty, out var challenge) ||
        !string.Equals(challenge.Subject, operatorId, StringComparison.Ordinal))
    {
        return Results.Json(new { error = "invalid_challenge" }, statusCode: StatusCodes.Status401Unauthorized);
    }

    if (!HandshakeProof.VerifyOperatorProof(
            settings.OperatorSecret,
            request.ChallengeId ?? string.Empty,
            challenge.ServerNonce,
            request.ClientNonce ?? string.Empty,
            operatorId,
            request.Proof ?? string.Empty))
    {
        return Results.Json(new { error = "invalid_proof" }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var sessionToken = sessions.IssueOperator(operatorId);
    return Results.Ok(new { sessionToken });
});

app.MapGet("/v1/catalog", async (HttpRequest httpRequest, SessionTokenService sessions, CatalogService catalog, CancellationToken ct) =>
{
    if (!SessionAuth.RequireOperator(httpRequest, sessions, out _))
    {
        return Results.Unauthorized();
    }

    var jwt = await catalog.GetSeedJwtAsync(ct).ConfigureAwait(false);
    return Results.Ok(new { jwt });
});

app.MapPost("/v1/catalog/publish", async (
    HttpRequest httpRequest,
    SeedPublishRequest request,
    SessionTokenService sessions,
    CatalogService catalog,
    CancellationToken ct) =>
{
    if (!SessionAuth.RequireOperator(httpRequest, sessions, out _))
    {
        return Results.Unauthorized();
    }

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
    var result = await catalog.PublishSeedJwtAsync(jwt, message, ct).ConfigureAwait(false);
    return Results.Ok(new { ok = true, revision = result.Revision });
});

app.MapGet("/v1/audit", async (
    HttpRequest httpRequest,
    SessionTokenService sessions,
    ILicenseStore store,
    CancellationToken ct) =>
{
    if (!SessionAuth.RequireOperator(httpRequest, sessions, out _))
    {
        return Results.Unauthorized();
    }

    var doc = await store.GetAuditAsync(ct).ConfigureAwait(false);
    return Results.Ok(doc);
});

app.MapPost("/v1/audit", async (
    HttpRequest httpRequest,
    AuditReplaceRequest request,
    SessionTokenService sessions,
    ILicenseStore store,
    CancellationToken ct) =>
{
    if (!SessionAuth.RequireOperator(httpRequest, sessions, out _))
    {
        return Results.Unauthorized();
    }

    var entries = request.Entries.Take(500).ToList();
    var result = await store.ReplaceAuditAsync(entries, ct).ConfigureAwait(false);
    return Results.Ok(new { ok = true, revision = result.Revision, count = entries.Count });
});

app.MapGet("/v1/robot-events", async (
    HttpRequest httpRequest,
    SessionTokenService sessions,
    ILicenseStore store,
    CancellationToken ct) =>
{
    if (!SessionAuth.RequireOperator(httpRequest, sessions, out _))
    {
        return Results.Unauthorized();
    }

    var doc = await store.GetRobotEventsAsync(ct).ConfigureAwait(false);
    return Results.Ok(doc);
});

app.MapPost("/v1/panel/login", async (
    PanelLoginRequest request,
    IPanelUserStore panelUsers,
    SessionTokenService sessions,
    ServerSettings settings) =>
{
    var username = (request.Username ?? string.Empty).Trim();
    var password = request.Password ?? string.Empty;
    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
    {
        return Results.BadRequest(new { error = "invalid_credentials" });
    }

    if (string.IsNullOrWhiteSpace(settings.SessionSigningKey))
    {
        return Results.Json(new { error = "server_misconfigured" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var user = await panelUsers.FindByUsernameAsync(username).ConfigureAwait(false);
    if (user is null || !PasswordHasher.Verify(password, user.PasswordHash))
    {
        return Results.Json(new { error = "invalid_credentials" }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var sessionToken = sessions.IssueOperator(user.Username, user.IsAdmin);
    return Results.Ok(BuildPanelLogin(user, sessions, settings));
});

app.MapGet("/v1/panel/me", (HttpRequest httpRequest, SessionTokenService sessions) =>
{
    if (!SessionAuth.RequireOperator(httpRequest, sessions, out var claims))
    {
        return Results.Unauthorized();
    }

    return Results.Ok(new
    {
        username = claims.OperatorId,
        isAdmin = claims.IsAdmin
    });
});

app.MapGet("/v1/panel/accounts", async (
    HttpRequest httpRequest,
    SessionTokenService sessions,
    IPanelUserStore panelUsers,
    CancellationToken ct) =>
{
    if (!SessionAuth.RequireAdmin(httpRequest, sessions, out _))
    {
        return Results.Forbid();
    }

    var accounts = await panelUsers.ListUsersAsync(ct).ConfigureAwait(false);
    return Results.Ok(new PanelAccountsResponse { Accounts = accounts.ToList() });
});

app.MapPost("/v1/panel/accounts", async (
    HttpRequest httpRequest,
    PanelAccountCreateRequest request,
    SessionTokenService sessions,
    IPanelUserStore panelUsers,
    CancellationToken ct) =>
{
    if (!SessionAuth.RequireAdmin(httpRequest, sessions, out _))
    {
        return Results.Forbid();
    }

    var username = (request.Username ?? string.Empty).Trim();
    var password = request.Password ?? string.Empty;
    if (username.Length < 3)
    {
        return Results.BadRequest(new { error = "username_min_3" });
    }

    var hasOAuth = !string.IsNullOrWhiteSpace(request.GithubLogin) || !string.IsNullOrWhiteSpace(request.GoogleEmail);
    if (password.Length < 8 && !hasOAuth)
    {
        return Results.BadRequest(new { error = "password_min_8_or_oauth_link" });
    }

    if (await panelUsers.FindByUsernameAsync(username, ct).ConfigureAwait(false) is not null)
    {
        return Results.Conflict(new { error = "username_exists" });
    }

    if (!string.IsNullOrWhiteSpace(request.GithubLogin) &&
        await panelUsers.FindByGithubLoginAsync(request.GithubLogin, ct).ConfigureAwait(false) is not null)
    {
        return Results.Conflict(new { error = "github_login_exists" });
    }

    if (!string.IsNullOrWhiteSpace(request.GoogleEmail) &&
        await panelUsers.FindByGoogleEmailAsync(request.GoogleEmail, ct).ConfigureAwait(false) is not null)
    {
        return Results.Conflict(new { error = "google_email_exists" });
    }

    var passwordHash = password.Length >= 8
        ? PasswordHasher.Hash(password)
        : PasswordHasher.Hash(Guid.NewGuid().ToString("N"));

    await panelUsers.CreateUserAsync(new PanelUserCreateOptions
    {
        Username = username,
        PasswordHash = passwordHash,
        IsAdmin = request.IsAdmin,
        GithubLogin = request.GithubLogin,
        GoogleEmail = request.GoogleEmail
    }, ct).ConfigureAwait(false);
    return Results.Ok(new { ok = true, username });
});

app.MapPatch("/v1/panel/accounts/{username}", async (
    HttpRequest httpRequest,
    string username,
    PanelAccountUpdateRequest request,
    SessionTokenService sessions,
    IPanelUserStore panelUsers,
    CancellationToken ct) =>
{
    if (!SessionAuth.RequireAdmin(httpRequest, sessions, out _))
    {
        return Results.Forbid();
    }

    var normalized = username.Trim();
    var existing = await panelUsers.FindByUsernameAsync(normalized, ct).ConfigureAwait(false);
    if (existing is null)
    {
        return Results.NotFound();
    }

    var githubLogin = request.GithubLogin ?? existing.GithubLogin;
    var googleEmail = request.GoogleEmail ?? existing.GoogleEmail;

    if (!string.IsNullOrWhiteSpace(githubLogin) &&
        await panelUsers.FindByGithubLoginAsync(githubLogin, ct).ConfigureAwait(false) is { } githubUser &&
        !string.Equals(githubUser.Username, normalized, StringComparison.OrdinalIgnoreCase))
    {
        return Results.Conflict(new { error = "github_login_exists" });
    }

    if (!string.IsNullOrWhiteSpace(googleEmail) &&
        await panelUsers.FindByGoogleEmailAsync(googleEmail, ct).ConfigureAwait(false) is { } googleUser &&
        !string.Equals(googleUser.Username, normalized, StringComparison.OrdinalIgnoreCase))
    {
        return Results.Conflict(new { error = "google_email_exists" });
    }

    var updated = await panelUsers.UpdateUserLinksAsync(
        normalized,
        githubLogin,
        googleEmail,
        ct).ConfigureAwait(false);
    return updated ? Results.Ok(new { ok = true }) : Results.NotFound();
});

app.MapGet("/v1/panel/oauth/providers", (PanelOAuthService oauth) =>
    Results.Ok(new
    {
        github = oauth.GithubEnabled,
        google = oauth.GoogleEnabled
    }));

app.MapGet("/v1/panel/oauth/github/start", (PanelOAuthService oauth) =>
{
    if (!oauth.GithubEnabled)
    {
        return Results.NotFound();
    }

    return Results.Redirect(oauth.BuildGithubAuthorizeUrl());
});

app.MapGet("/v1/panel/oauth/google/start", (PanelOAuthService oauth) =>
{
    if (!oauth.GoogleEnabled)
    {
        return Results.NotFound();
    }

    return Results.Redirect(oauth.BuildGoogleAuthorizeUrl());
});

app.MapGet("/v1/panel/oauth/github/callback", async (
    HttpRequest request,
    PanelOAuthService oauth,
    IPanelUserStore panelUsers,
    SessionTokenService sessions,
    ServerSettings settings) =>
    await HandleOAuthCallbackAsync(request, oauth, panelUsers, sessions, settings, "github").ConfigureAwait(false));

app.MapGet("/v1/panel/oauth/google/callback", async (
    HttpRequest request,
    PanelOAuthService oauth,
    IPanelUserStore panelUsers,
    SessionTokenService sessions,
    ServerSettings settings) =>
    await HandleOAuthCallbackAsync(request, oauth, panelUsers, sessions, settings, "google").ConfigureAwait(false));

app.MapDelete("/v1/panel/accounts/{username}", async (
    HttpRequest httpRequest,
    string username,
    SessionTokenService sessions,
    IPanelUserStore panelUsers,
    CancellationToken ct) =>
{
    if (!SessionAuth.RequireAdmin(httpRequest, sessions, out var claims))
    {
        return Results.Forbid();
    }

    var normalized = username.Trim();
    if (string.Equals(claims.OperatorId, normalized, StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { error = "cannot_delete_self" });
    }

    var deleted = await panelUsers.DeleteUserAsync(normalized, ct).ConfigureAwait(false);
    return deleted ? Results.Ok(new { ok = true }) : Results.NotFound();
});

app.Run();

static PanelLoginResponse BuildPanelLogin(PanelUserRecord user, SessionTokenService sessions, ServerSettings settings) =>
    new()
    {
        SessionToken = sessions.IssueOperator(user.Username, user.IsAdmin),
        Username = user.Username,
        IsAdmin = user.IsAdmin,
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(settings.SessionTtlMinutes).ToString("O")
    };

static async Task<IResult> HandleOAuthCallbackAsync(
    HttpRequest request,
    PanelOAuthService oauth,
    IPanelUserStore panelUsers,
    SessionTokenService sessions,
    ServerSettings settings,
    string provider)
{
    var state = request.Query["state"].ToString();
    if (!oauth.TryValidateState(state, provider, out var stateError))
    {
        return Results.Redirect(oauth.BuildPanelErrorRedirect(stateError ?? "invalid_state"));
    }

    var code = request.Query["code"].ToString();
    if (string.IsNullOrWhiteSpace(code))
    {
        return Results.Redirect(oauth.BuildPanelErrorRedirect("missing_code"));
    }

    if (string.IsNullOrWhiteSpace(settings.SessionSigningKey))
    {
        return Results.Redirect(oauth.BuildPanelErrorRedirect("server_misconfigured"));
    }

    OAuthIdentity? identity = provider switch
    {
        "github" => await oauth.ExchangeGithubAsync(code).ConfigureAwait(false),
        "google" => await oauth.ExchangeGoogleAsync(code).ConfigureAwait(false),
        _ => null
    };

    if (identity is null)
    {
        return Results.Redirect(oauth.BuildPanelErrorRedirect("oauth_exchange_failed"));
    }

    PanelUserRecord? user = provider switch
    {
        "github" => await panelUsers.FindByGithubLoginAsync(identity.Key).ConfigureAwait(false),
        "google" => await panelUsers.FindByGoogleEmailAsync(identity.Key).ConfigureAwait(false),
        _ => null
    };

    if (user is null)
    {
        return Results.Redirect(oauth.BuildPanelErrorRedirect("no_account"));
    }

    var login = BuildPanelLogin(user, sessions, settings);
    return Results.Redirect(oauth.BuildPanelSuccessRedirect(login));
}

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

internal static class JsonDefaults
{
    internal static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public partial class Program { }
