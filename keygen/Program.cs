using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

if (args.Length == 0)
{
    PrintHelp();
    return 1;
}

try
{
    switch (args[0].ToLowerInvariant())
    {
        case "newkeys":
            return RunNewKeys(args);
        case "issue":
            return RunIssue(args);
        default:
            PrintHelp();
            return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 2;
}

static int RunNewKeys(string[] args)
{
    if (args.Length != 2)
    {
        Console.WriteLine("usage: newkeys <outputDir>");
        return 1;
    }

    var outDir = args[1];
    Directory.CreateDirectory(outDir);

    using var rsa = RSA.Create(3072);
    var privatePem = rsa.ExportRSAPrivateKeyPem();
    var publicPem = rsa.ExportRSAPublicKeyPem();

    File.WriteAllText(Path.Combine(outDir, "seal.private.pem"), privatePem);
    File.WriteAllText(Path.Combine(outDir, "seal.public.pem"), publicPem);

    Console.WriteLine($"created: {Path.GetFullPath(outDir)}");
    return 0;
}

static int RunIssue(string[] args)
{
    if (args.Length != 8)
    {
        Console.WriteLine("usage: issue <privateKeyPemPath> <pepper> <tokenId> <owner> <validToUtc> <hostsCsv> <payloadJsonPath>");
        return 1;
    }

    var privateKeyPemPath = args[1];
    var pepper = args[2];
    var tokenId = args[3];
    var owner = args[4];
    var validToUtc = args[5];
    var hostsCsv = args[6];
    var payloadJsonPath = args[7];

    if (!DateTimeOffset.TryParse(validToUtc, out var _))
    {
        throw new InvalidOperationException("validToUtc must be ISO date, e.g. 2026-12-31T23:59:59Z");
    }

    var payloadJson = File.ReadAllText(payloadJsonPath);
    var payload = JsonSerializer.Deserialize<SecretPayload>(payloadJson, JsonOptions.Instance)
        ?? throw new InvalidOperationException("invalid payload json");

    if (string.IsNullOrWhiteSpace(payload.ApiEndpoint) ||
        string.IsNullOrWhiteSpace(payload.ConnectionString) ||
        string.IsNullOrWhiteSpace(payload.AgentSystemPrompt))
    {
        throw new InvalidOperationException("payload must contain apiEndpoint, connectionString, agentSystemPrompt");
    }

    var key = DeriveAesKey(tokenId, pepper);
    var encrypted = Encrypt(payloadJson, key);
    var parts = encrypted.Split('.', StringSplitOptions.TrimEntries);
    if (parts.Length != 3)
    {
        throw new InvalidOperationException("unexpected encryption output");
    }

    var hosts = hostsCsv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Select(h => h.ToUpperInvariant())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    var entry = new CatalogEntry
    {
        TokenId = tokenId,
        Owner = owner,
        ValidToUtc = validToUtc,
        Enabled = true,
        Hosts = hosts,
        Blob = parts[0],
        Nonce = parts[1],
        Tag = parts[2],
        Seal = string.Empty
    };

    var canonical = Canonical(entry);
    var privatePem = File.ReadAllText(privateKeyPemPath);
    entry.Seal = Sign(canonical, privatePem);

    var json = JsonSerializer.Serialize(entry, JsonOptions.Instance);
    Console.WriteLine(json);
    return 0;
}

static string Canonical(CatalogEntry entry)
{
    var hosts = entry.Hosts.Count == 0
        ? string.Empty
        : string.Join(",", entry.Hosts.Select(h => h.Trim().ToUpperInvariant()).OrderBy(h => h, StringComparer.Ordinal));

    return string.Join("|",
        entry.TokenId.Trim(),
        entry.Owner.Trim(),
        entry.ValidToUtc.Trim(),
        entry.Enabled ? "1" : "0",
        hosts,
        entry.Blob.Trim(),
        entry.Nonce.Trim(),
        entry.Tag.Trim());
}

static string Sign(string canonical, string privatePem)
{
    using var rsa = RSA.Create();
    rsa.ImportFromPem(privatePem);
    var data = Encoding.UTF8.GetBytes(canonical);
    var sig = rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    return Convert.ToBase64String(sig);
}

static byte[] DeriveAesKey(string runtimeToken, string pepper)
{
    using var sha = SHA256.Create();
    return sha.ComputeHash(Encoding.UTF8.GetBytes($"{runtimeToken}:{pepper}"));
}

static string Encrypt(string plaintext, byte[] key)
{
    var nonce = RandomNumberGenerator.GetBytes(12);
    var plain = Encoding.UTF8.GetBytes(plaintext);
    var cipher = new byte[plain.Length];
    var tag = new byte[16];
    using var aes = new AesGcm(key);
    aes.Encrypt(nonce, plain, cipher, tag);
    return $"{Convert.ToBase64String(cipher)}.{Convert.ToBase64String(nonce)}.{Convert.ToBase64String(tag)}";
}

static void PrintHelp()
{
    Console.WriteLine("SeedForge");
    Console.WriteLine("  newkeys <outputDir>");
    Console.WriteLine("  issue <privateKeyPemPath> <pepper> <tokenId> <owner> <validToUtc> <hostsCsv> <payloadJsonPath>");
}

internal sealed class CatalogEntry
{
    [JsonPropertyName("tokenId")]
    public string TokenId { get; init; } = string.Empty;

    [JsonPropertyName("owner")]
    public string Owner { get; init; } = string.Empty;

    [JsonPropertyName("validToUtc")]
    public string ValidToUtc { get; init; } = string.Empty;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("hosts")]
    public List<string> Hosts { get; init; } = [];

    [JsonPropertyName("blob")]
    public string Blob { get; init; } = string.Empty;

    [JsonPropertyName("nonce")]
    public string Nonce { get; init; } = string.Empty;

    [JsonPropertyName("tag")]
    public string Tag { get; init; } = string.Empty;

    [JsonPropertyName("seal")]
    public string Seal { get; set; } = string.Empty;
}

internal sealed class SecretPayload
{
    [JsonPropertyName("apiEndpoint")]
    public string ApiEndpoint { get; init; } = string.Empty;

    [JsonPropertyName("connectionString")]
    public string ConnectionString { get; init; } = string.Empty;

    [JsonPropertyName("agentSystemPrompt")]
    public string AgentSystemPrompt { get; init; } = string.Empty;
}

internal sealed class JsonOptions
{
    internal static readonly JsonSerializerOptions Instance = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };
}
