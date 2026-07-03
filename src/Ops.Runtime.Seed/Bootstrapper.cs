using System.Runtime.CompilerServices;
using System.Security.Cryptography;
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

    // Własne wartości podmień przed publikacją biblioteki.
    private const string SourceUrl = "https://raw.githubusercontent.com/example-org/runtime-catalog/main/catalog.json";
    private const string SourceToken = "";
    private const string Pepper = "replace-with-long-random-pepper";
    private const string PublicSealKeyPem = """
-----BEGIN PUBLIC KEY-----
REPLACE_WITH_RSA_PUBLIC_KEY
-----END PUBLIC KEY-----
""";

    private const int GraceDays = 7;

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

    public static async Task<RuntimeProfile> InitializeAsync(
        string runtimeToken,
        string? machineAlias = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(runtimeToken))
        {
            throw new InvalidOperationException("boot-0x01");
        }

        var machine = (machineAlias ?? Environment.MachineName).Trim().ToUpperInvariant();
        var cache = new CacheStore();
        var key = Crypto.DeriveAesKey(runtimeToken, Pepper);
        var tokenHash = Crypto.HashToken(runtimeToken, Pepper);

        try
        {
            var client = new CatalogClient(Http);
            var catalog = await client.LoadAsync(SourceUrl, SourceToken, cancellationToken).ConfigureAwait(false);
            var entry = ResolveEntry(catalog, runtimeToken, machine);
            var profile = MaterializeProfile(entry, runtimeToken, key);

            PersistCache(cache, profile, tokenHash, machine, key);
            lock (Gate)
            {
                _current = profile;
            }

            return profile;
        }
        catch when (TryReadGraceCache(cache, tokenHash, machine, key, out var cached))
        {
            lock (Gate)
            {
                _current = cached;
            }

            return cached;
        }
    }

    public static RuntimeProfile Initialize(string runtimeToken, string? machineAlias = null)
    {
        return InitializeAsync(runtimeToken, machineAlias).GetAwaiter().GetResult();
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
        if (!Crypto.VerifySeal(canonical, entry.Seal, PublicSealKeyPem))
        {
            throw new InvalidOperationException("boot-0x16");
        }

        return entry;
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
        if (now > record.ValidatedAtUtc.AddDays(GraceDays) || now > record.ValidToUtc)
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
        var token = Environment.GetEnvironmentVariable("FLOW_RUNTIME_TOKEN")
            ?? Environment.GetEnvironmentVariable("APP_BOOT_TOKEN");

        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        try
        {
            _ = Initialize(token);
        }
        catch
        {
            // Brak jawnego komunikatu: host zdecyduje co zrobić przy odczycie Current.
        }
    }
}
