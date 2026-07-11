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

var corsOptions = builder.Configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>() ?? new CorsOptions();
var allowedOrigins = corsOptions.AllowedOrigins.Length > 0
    ? corsOptions.AllowedOrigins
    : new[] { "https://mikzielinski.github.io" };

builder.Services.AddSingleton(databaseOptions);
builder.Services.AddSingleton(serverSettings);
builder.Services.AddSingleton<HandshakeService>();
builder.Services.AddSingleton<SessionTokenService>();
builder.Services.AddSingleton<CatalogService>();

if (string.IsNullOrWhiteSpace(databaseOptions.ConnectionString))
{
    builder.Services.AddSingleton<ILicenseStore, InMemoryLicenseStore>();
}
else
{
    builder.Services.AddSingleton<ILicenseStore, PostgresLicenseStore>();
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
try
{
    await store.EnsureSchemaAsync().ConfigureAwait(false);
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

internal static class JsonDefaults
{
    internal static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public partial class Program { }
