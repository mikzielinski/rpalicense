using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ops.Runtime.Seed;

public static class Bootstrapper
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private static readonly object Gate = new();
    private static RuntimeProfile? _current;
    private static ValidationSnapshot _lastCheck = new()
    {
        Success = false,
        UsedCache = false,
        SourceUrl = BootstrapperSettings.SourceUrl,
        CheckedAtUtc = DateTimeOffset.MinValue,
        Code = "boot-0x00",
        Notes = "not-initialized"
    };

    public static RuntimeProfile Current
    {
        get
        {
            lock (Gate)
            {
                return _current ?? throw new InvalidOperationException("boot-0x00");
            }
        }
    }

    public static ValidationSnapshot LastCheck
    {
        get
        {
            lock (Gate)
            {
                return _lastCheck;
            }
        }
    }

    public static async Task<RuntimeProfile> InitializeAsync(
        string runtimeToken,
        string? machineAlias = null,
        CancellationToken cancellationToken = default)
    {
        BootstrapperSettings.ApplyFromEnvironment();

        if (string.IsNullOrWhiteSpace(runtimeToken))
        {
            throw new InvalidOperationException("boot-0x01");
        }

        var machine = (machineAlias ?? Environment.MachineName).Trim().ToUpperInvariant();
        var cache = new CacheStore(BootstrapperSettings.CachePathOverride);
        var key = Crypto.DeriveAesKey(runtimeToken, BootstrapperSettings.Pepper);
        var tokenHash = Crypto.HashToken(runtimeToken, BootstrapperSettings.Pepper);

        try
        {
            var sourceBody = BootstrapperSettings.CatalogLoaderOverride is not null
                ? await BootstrapperSettings.CatalogLoaderOverride(BootstrapperSettings.SourceUrl).ConfigureAwait(false)
                : await new CatalogClient(Http).LoadRawAsync(
                    BootstrapperSettings.SourceUrl,
                    BootstrapperSettings.SourceToken,
                    cancellationToken).ConfigureAwait(false);
            var catalog = ParseCatalog(sourceBody);
            var entry = ResolveEntry(catalog, runtimeToken, machine);
            var profile = MaterializeProfile(entry, runtimeToken, key);

            try
            {
                PersistCache(cache, profile, tokenHash, machine, key);
            }
            catch
            {
                // Cache is best-effort; remote validation already succeeded.
            }

            lock (Gate)
            {
                _current = profile;
                _lastCheck = NewSnapshot(
                    success: true,
                    usedCache: false,
                    tokenId: runtimeToken,
                    machine: machine,
                    code: "boot-ok-remote",
                    notes: "remote-validated");
            }

            return profile;
        }
        catch (Exception ex) when (CanUseGraceCache(ex) && TryReadGraceCache(cache, tokenHash, machine, key, out var cached))
        {
            lock (Gate)
            {
                _current = cached;
                _lastCheck = NewSnapshot(
                    success: true,
                    usedCache: true,
                    tokenId: runtimeToken,
                    machine: machine,
                    code: "boot-ok-cache",
                    notes: $"cache-fallback-after:{ExtractCode(ex)}");
            }

            return cached;
        }
        catch (Exception ex)
        {
            var code = ExtractCode(ex);
            lock (Gate)
            {
                if (IsPolicyDenial(code))
                {
                    _current = null;
                    try
                    {
                        cache.Delete();
                    }
                    catch
                    {
                        // Best-effort cache purge.
                    }
                }

                _lastCheck = NewSnapshot(
                    success: false,
                    usedCache: false,
                    tokenId: runtimeToken,
                    machine: machine,
                    code: code,
                    notes: "validation-failed");
            }

            if (IsPolicyDenial(code))
            {
                EnforceHostDenial(code);
            }

            throw;
        }
    }

    /// <summary>
    /// Re-validates license against remote catalog. On policy denial clears session and,
    /// when <c>OPS_SEED_KILL_ON_DENY</c> is enabled (default on Windows), terminates UiPath processes.
    /// </summary>
    public static RuntimeProfile EnsureAuthorized(string runtimeToken, string? machineAlias = null)
    {
        if (TryInitialize(runtimeToken, out var profile, machineAlias) && profile is not null)
        {
            return profile;
        }

        var code = LastCheck.Code;
        EnforceHostDenial(code);
        throw new InvalidOperationException(code);
    }

    public static RuntimeProfile Initialize(string runtimeToken, string? machineAlias = null)
    {
        return InitializeAsync(runtimeToken, machineAlias).GetAwaiter().GetResult();
    }

    public static bool TryInitialize(string runtimeToken, out RuntimeProfile? profile, string? machineAlias = null)
    {
        try
        {
            profile = Initialize(runtimeToken, machineAlias);
            return true;
        }
        catch
        {
            profile = null;
            if (IsPolicyDenial(LastCheck.Code))
            {
                EnforceHostDenial(LastCheck.Code);
            }

            return false;
        }
    }

    /// <summary>
    /// Terminates UiPath host processes (Robot, Assistant, Studio, Executor, …). Returns number of PIDs targeted.
    /// </summary>
    public static int TerminateUiPathHost()
    {
        return HostGuard.TerminateUiPathProcesses();
    }

    internal static void ResetForTesting()
    {
        lock (Gate)
        {
            _current = null;
            _lastCheck = new ValidationSnapshot
            {
                Success = false,
                UsedCache = false,
                SourceUrl = BootstrapperSettings.SourceUrl,
                CheckedAtUtc = DateTimeOffset.MinValue,
                Code = "boot-0x00",
                Notes = "not-initialized"
            };
        }

        BootstrapperDiagnostics.Reset();
    }

    private static CatalogEntry ResolveEntry(CatalogDocument catalog, string runtimeToken, string machine)
    {
        var entry = catalog.Entries.FirstOrDefault(e => string.Equals(e.TokenId, runtimeToken, StringComparison.Ordinal));
        if (entry is null)
        {
            throw new InvalidOperationException("boot-0x11");
        }

        if (!entry.Enabled)
        {
            throw new InvalidOperationException("boot-0x12");
        }

        if (!DateTimeOffset.TryParse(entry.ValidToUtc, out var validToUtc))
        {
            throw new InvalidOperationException("boot-0x13");
        }

        if (DateTimeOffset.UtcNow > validToUtc)
        {
            throw new InvalidOperationException("boot-0x14");
        }

        if (entry.Hosts.Count > 0 && !entry.Hosts.Any(h => string.Equals(h.Trim(), machine, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("boot-0x15");
        }

        var canonical = Crypto.Canonical(entry);
        if (!Crypto.VerifySeal(canonical, entry.Seal, BootstrapperSettings.PublicSealKeyPem))
        {
            throw new InvalidOperationException("boot-0x16");
        }

        return entry;
    }

    private static CatalogDocument ParseCatalog(string sourceBody)
    {
        var body = sourceBody.Trim();
        if (!BootstrapperSettings.SourceUsesJwtEnvelope)
        {
            return JsonSerializer.Deserialize<CatalogDocument>(body, Json.Options)
                ?? throw new InvalidOperationException("boot-0x51");
        }

        return ParseCatalogFromJwt(body);
    }

    private static CatalogDocument ParseCatalogFromJwt(string jwt)
    {
        var parts = jwt.Split('.', StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
        {
            throw new InvalidOperationException("boot-0x52");
        }

        try
        {
            var headerJson = Encoding.UTF8.GetString(Crypto.Base64UrlDecode(parts[0]));
            using var headerDoc = JsonDocument.Parse(headerJson);
            var alg = headerDoc.RootElement.GetProperty("alg").GetString();
            if (!string.Equals(alg, "HS256", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("boot-0x5A");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            throw new InvalidOperationException("boot-0x5A");
        }

        var signed = $"{parts[0]}.{parts[1]}";
        if (!Crypto.VerifyHs256(signed, parts[2], BootstrapperSettings.EnvelopeSigningKey))
        {
            throw new InvalidOperationException("boot-0x53");
        }

        JwtEnvelopeClaims claims;
        try
        {
            var payloadJson = Encoding.UTF8.GetString(Crypto.Base64UrlDecode(parts[1]));
            claims = JsonSerializer.Deserialize<JwtEnvelopeClaims>(payloadJson, Json.Options)
                ?? throw new InvalidOperationException("boot-0x54");
        }
        catch
        {
            throw new InvalidOperationException("boot-0x54");
        }

        if (!string.Equals(claims.Issuer, BootstrapperSettings.EnvelopeIssuer, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("boot-0x55");
        }

        if (!string.Equals(claims.Audience, BootstrapperSettings.EnvelopeAudience, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("boot-0x56");
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (claims.NotBefore > 0 && now < claims.NotBefore)
        {
            throw new InvalidOperationException("boot-0x57");
        }

        if (claims.Exp > 0 && now >= claims.Exp)
        {
            throw new InvalidOperationException("boot-0x58");
        }

        var envelopeKey = Crypto.DeriveAesKey(BootstrapperSettings.EnvelopeAudience, BootstrapperSettings.EnvelopePepper);
        var catalogJson = Crypto.Decrypt(claims.Blob, claims.Nonce, claims.Tag, envelopeKey);
        return JsonSerializer.Deserialize<CatalogDocument>(catalogJson, Json.Options)
            ?? throw new InvalidOperationException("boot-0x59");
    }

    private static RuntimeProfile MaterializeProfile(CatalogEntry entry, string runtimeToken, byte[] key)
    {
        var payloadJson = Crypto.Decrypt(entry.Blob, entry.Nonce, entry.Tag, key);
        var payload = JsonSerializer.Deserialize<SecretPayload>(payloadJson, Json.Options)
            ?? throw new CryptographicException("boot-0x31");

        if (!DateTimeOffset.TryParse(entry.ValidToUtc, out var validToUtc))
        {
            throw new InvalidOperationException("boot-0x13");
        }

        return new RuntimeProfile
        {
            ApiEndpoint = payload.ApiEndpoint,
            ConnectionString = payload.ConnectionString,
            AgentSystemPrompt = payload.AgentSystemPrompt,
            ValidToUtc = validToUtc,
            Owner = entry.Owner,
            TokenId = runtimeToken
        };
    }

    private static bool TryReadGraceCache(CacheStore cache, string tokenHash, string machine, byte[] key, out RuntimeProfile profile)
    {
        profile = null!;
        var record = cache.Read();
        if (record is null)
        {
            return false;
        }

        if (!string.Equals(record.TokenHash, tokenHash, StringComparison.Ordinal) ||
            !string.Equals(record.Machine, machine, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        if (now > record.ValidatedAtUtc.AddDays(BootstrapperSettings.GraceDays) || now > record.ValidToUtc)
        {
            return false;
        }

        try
        {
            var json = Crypto.Decrypt(record.Blob, record.Nonce, record.Tag, key);
            profile = JsonSerializer.Deserialize<RuntimeProfile>(json, Json.Options)
                ?? throw new CryptographicException("boot-0x32");
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void PersistCache(CacheStore cache, RuntimeProfile profile, string tokenHash, string machine, byte[] key)
    {
        var json = JsonSerializer.Serialize(profile, Json.Options);
        var encrypted = Crypto.Encrypt(json, key);
        var parts = encrypted.Split('.', StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
        {
            return;
        }

        cache.Write(new CachedRecord
        {
            TokenHash = tokenHash,
            Machine = machine,
            Owner = profile.Owner,
            TokenId = profile.TokenId,
            ValidToUtc = profile.ValidToUtc,
            ValidatedAtUtc = DateTimeOffset.UtcNow,
            Blob = parts[0],
            Nonce = parts[1],
            Tag = parts[2]
        });
    }

    [ModuleInitializer]
    internal static void ModuleInit()
    {
        BootstrapperSettings.ApplyFromEnvironment();
        BootstrapperDiagnostics.ModuleInitAttempted = true;

        var token = Environment.GetEnvironmentVariable("FLOW_RUNTIME_TOKEN")
            ?? Environment.GetEnvironmentVariable("APP_BOOT_TOKEN");

        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        try
        {
            _ = Initialize(token);
            BootstrapperDiagnostics.ModuleInitSucceeded = true;
        }
        catch (Exception ex)
        {
            BootstrapperDiagnostics.ModuleInitFailure = ex.ToString();
            var code = ExtractCode(ex);
            lock (Gate)
            {
                if (IsPolicyDenial(code))
                {
                    _current = null;
                }
            }

            if (IsPolicyDenial(code))
            {
                EnforceHostDenial(code);
            }
        }
    }

    private static bool IsPolicyDenial(string code) =>
        code is "boot-0x01" or "boot-0x11" or "boot-0x12" or "boot-0x13" or "boot-0x14" or "boot-0x15" or "boot-0x16";

    private static void EnforceHostDenial(string code)
    {
        if (OperatingSystem.IsWindows())
        {
            HostGuard.TerminateUiPathProcesses();
        }

        if (BootstrapperSettings.QuietMode)
        {
            Environment.Exit(1);
        }

        if (!BootstrapperSettings.KillOnDeny)
        {
            return;
        }

        Environment.FailFast(code);
    }

    private static bool CanUseGraceCache(Exception ex)
    {
        var code = ExtractCode(ex);
        return code is not (
            "boot-0x11" or "boot-0x12" or "boot-0x14" or "boot-0x15" or "boot-0x16"
            or "boot-0x52" or "boot-0x53" or "boot-0x54" or "boot-0x55" or "boot-0x56"
            or "boot-0x57" or "boot-0x58" or "boot-0x59" or "boot-0x5A");
    }

    private static ValidationSnapshot NewSnapshot(
        bool success,
        bool usedCache,
        string tokenId,
        string machine,
        string code,
        string notes)
    {
        return new ValidationSnapshot
        {
            Success = success,
            UsedCache = usedCache,
            SourceUrl = BootstrapperSettings.SourceUrl,
            TokenId = tokenId,
            Machine = machine,
            CheckedAtUtc = DateTimeOffset.UtcNow,
            Code = code,
            Notes = notes
        };
    }

    private static string ExtractCode(Exception ex)
    {
        if (ex is InvalidOperationException && !string.IsNullOrWhiteSpace(ex.Message))
        {
            return ex.Message.Trim();
        }

        if (ex is CryptographicException && !string.IsNullOrWhiteSpace(ex.Message))
        {
            return ex.Message.Trim();
        }

        return "boot-0xFF";
    }
}
