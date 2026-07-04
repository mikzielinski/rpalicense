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
        case "wrapjwt":
            return RunWrapJwt(args);
        case "exportjwk":
            return RunExportJwk(args);
        case "reseal":
            return RunReseal(args);
        case "unwrapjwt":
            return RunUnwrapJwt(args);
        case "packclient":
            return RunPackClient(args);
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

static int RunWrapJwt(string[] args)
{
    if (args.Length != 7)
    {
        Console.WriteLine("usage: wrapjwt <catalogJsonPath> <jwtSigningKey> <envelopePepper> <issuer> <audience> <expUtc>");
        return 1;
    }

    var catalogJsonPath = args[1];
    var jwtSigningKey = args[2];
    var envelopePepper = args[3];
    var issuer = args[4];
    var audience = args[5];
    var expUtcRaw = args[6];

    if (!DateTimeOffset.TryParse(expUtcRaw, out var expUtc))
    {
        throw new InvalidOperationException("expUtc must be ISO date, e.g. 2026-12-31T23:59:59Z");
    }

    var catalogJson = File.ReadAllText(catalogJsonPath);
    var envelopeKey = DeriveAesKey(audience, envelopePepper);
    var encrypted = Encrypt(catalogJson, envelopeKey);
    var encParts = encrypted.Split('.', StringSplitOptions.TrimEntries);
    if (encParts.Length != 3)
    {
        throw new InvalidOperationException("unexpected encryption output");
    }

    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    var claims = new JwtEnvelopeClaims
    {
        Issuer = issuer,
        Audience = audience,
        Exp = expUtc.ToUnixTimeSeconds(),
        NotBefore = now - 30,
        Blob = encParts[0],
        Nonce = encParts[1],
        Tag = encParts[2]
    };

    var headerJson = JsonSerializer.Serialize(new JwtHeader(), JsonOptions.Instance);
    var payloadJson = JsonSerializer.Serialize(claims, JsonOptions.Instance);
    var headerPart = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
    var payloadPart = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
    var signedContent = $"{headerPart}.{payloadPart}";
    var signature = SignHs256(signedContent, jwtSigningKey);

    Console.WriteLine($"{signedContent}.{signature}");
    return 0;
}

static int RunExportJwk(string[] args)
{
    if (args.Length != 2)
    {
        Console.WriteLine("usage: exportjwk <privateKeyPemPath>");
        return 1;
    }

    var privateKeyPemPath = args[1];
    var pem = File.ReadAllText(privateKeyPemPath);
    using var rsa = RSA.Create();
    rsa.ImportFromPem(pem);
    var p = rsa.ExportParameters(true);

    var jwk = new Dictionary<string, object?>
    {
        ["kty"] = "RSA",
        ["alg"] = "RS256",
        ["use"] = "sig",
        ["key_ops"] = new[] { "sign" },
        ["n"] = ToBase64Url(p.Modulus),
        ["e"] = ToBase64Url(p.Exponent),
        ["d"] = ToBase64Url(p.D),
        ["p"] = ToBase64Url(p.P),
        ["q"] = ToBase64Url(p.Q),
        ["dp"] = ToBase64Url(p.DP),
        ["dq"] = ToBase64Url(p.DQ),
        ["qi"] = ToBase64Url(p.InverseQ),
        ["ext"] = false
    };

    Console.WriteLine(JsonSerializer.Serialize(jwk, JsonOptions.Instance));
    return 0;
}

static int RunReseal(string[] args)
{
    if (args.Length != 3)
    {
        Console.WriteLine("usage: reseal <catalogJsonPath> <privateKeyPemPath>");
        return 1;
    }

    var catalogJsonPath = args[1];
    var privateKeyPemPath = args[2];
    var privatePem = File.ReadAllText(privateKeyPemPath);
    var catalogJson = File.ReadAllText(catalogJsonPath);
    using var doc = JsonDocument.Parse(catalogJson);
    if (!doc.RootElement.TryGetProperty("entries", out var entriesEl) || entriesEl.ValueKind != JsonValueKind.Array)
    {
        throw new InvalidOperationException("catalog must contain entries[]");
    }

    var entries = new List<CatalogEntry>();
    foreach (var item in entriesEl.EnumerateArray())
    {
        var entry = JsonSerializer.Deserialize<CatalogEntry>(item.GetRawText(), JsonOptions.Instance)
            ?? throw new InvalidOperationException("invalid catalog entry");
        entry.Seal = Sign(Canonical(entry), privatePem);
        entries.Add(entry);
    }

    Console.WriteLine(JsonSerializer.Serialize(new { entries }, JsonOptions.Instance));
    return 0;
}

static int RunUnwrapJwt(string[] args)
{
    if (args.Length != 6)
    {
        Console.WriteLine("usage: unwrapjwt <jwtPathOrDash> <jwtSigningKey> <envelopePepper> <issuer> <audience>");
        return 1;
    }

    var jwt = args[1] == "-"
        ? Console.In.ReadToEnd().Trim()
        : File.ReadAllText(args[1]).Trim();
    var jwtSigningKey = args[2];
    var envelopePepper = args[3];
    var issuer = args[4];
    var audience = args[5];

    var parts = jwt.Split('.', StringSplitOptions.TrimEntries);
    if (parts.Length != 3)
    {
        throw new InvalidOperationException("invalid jwt");
    }

    var signed = $"{parts[0]}.{parts[1]}";
    if (!VerifyHs256(signed, parts[2], jwtSigningKey))
    {
        throw new InvalidOperationException("invalid jwt signature");
    }

    var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
    var claims = JsonSerializer.Deserialize<JwtEnvelopeClaims>(payloadJson, JsonOptions.Instance)
        ?? throw new InvalidOperationException("invalid jwt payload");
    if (!string.Equals(claims.Issuer, issuer, StringComparison.Ordinal) ||
        !string.Equals(claims.Audience, audience, StringComparison.Ordinal))
    {
        throw new InvalidOperationException("iss/aud mismatch");
    }

    var envelopeKey = DeriveAesKey(audience, envelopePepper);
    var catalogJson = Decrypt(claims.Blob, claims.Nonce, claims.Tag, envelopeKey);
    Console.WriteLine(catalogJson);
    return 0;
}

static bool VerifyHs256(string message, string signatureB64Url, string signingKey)
{
    var expected = SignHs256(message, signingKey);
    return string.Equals(expected, signatureB64Url, StringComparison.Ordinal);
}

static string Decrypt(string cipherB64, string nonceB64, string tagB64, byte[] key)
{
    var cipher = Convert.FromBase64String(cipherB64);
    var nonce = Convert.FromBase64String(nonceB64);
    var tag = Convert.FromBase64String(tagB64);
    var plain = new byte[cipher.Length];
    using var aes = new AesGcm(key);
    aes.Decrypt(nonce, cipher, tag, plain);
    return Encoding.UTF8.GetString(plain);
}

static byte[] Base64UrlDecode(string value)
{
    var s = value.Replace('-', '+').Replace('_', '/');
    var padding = 4 - (s.Length % 4);
    if (padding is > 0 and < 4)
    {
        s += new string('=', padding);
    }

    return Convert.FromBase64String(s);
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

static string Base64UrlEncode(byte[] data)
{
    return Convert.ToBase64String(data)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}

static string ToBase64Url(byte[]? data)
{
    if (data is null || data.Length == 0)
    {
        return string.Empty;
    }

    return Base64UrlEncode(data);
}

static string SignHs256(string message, string signingKey)
{
    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingKey));
    var signature = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
    return Base64UrlEncode(signature);
}

static int RunPackClient(string[] args)
{
    if (args.Length < 2)
    {
        Console.WriteLine("usage: packclient <tokenId> [outputDir] [--client-name NAME]");
        return 1;
    }

    var tokenId = args[1];
    if (!tokenId.StartsWith("RT-", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("tokenId must start with RT-");
    }

    var outputDir = "dist/clients";
    var clientName = string.Empty;
    var scriptArgs = new List<string>();

    for (var i = 2; i < args.Length; i++)
    {
        if (args[i] == "--client-name" && i + 1 < args.Length)
        {
            clientName = args[++i];
            scriptArgs.Add("--client-name");
            scriptArgs.Add(clientName);
            continue;
        }

        if (!args[i].StartsWith('-'))
        {
            outputDir = args[i];
            continue;
        }

        throw new InvalidOperationException($"unknown option: {args[i]}");
    }

    var repoRoot = FindRepoRoot();
    var scriptPath = Path.Combine(repoRoot, "scripts", "generate-client-package.sh");
    if (!File.Exists(scriptPath))
    {
        throw new FileNotFoundException("generate-client-package.sh not found", scriptPath);
    }

    var psi = new System.Diagnostics.ProcessStartInfo
    {
        FileName = "/bin/bash",
        WorkingDirectory = repoRoot,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    psi.ArgumentList.Add(scriptPath);
    psi.ArgumentList.Add(tokenId);
    psi.ArgumentList.Add("--output-dir");
    psi.ArgumentList.Add(Path.GetFullPath(outputDir));
    foreach (var arg in scriptArgs)
    {
        psi.ArgumentList.Add(arg);
    }

    using var proc = System.Diagnostics.Process.Start(psi)!;
    var stdout = proc.StandardOutput.ReadToEnd();
    var stderr = proc.StandardError.ReadToEnd();
    proc.WaitForExit();

    if (!string.IsNullOrWhiteSpace(stdout))
    {
        Console.Write(stdout);
    }

    if (!string.IsNullOrWhiteSpace(stderr))
    {
        Console.Error.Write(stderr);
    }

    return proc.ExitCode;
}

static string FindRepoRoot()
{
    var dir = AppContext.BaseDirectory;
    while (!string.IsNullOrEmpty(dir))
    {
        if (File.Exists(Path.Combine(dir, "scripts", "generate-client-package.sh")))
        {
            return dir;
        }

        dir = Directory.GetParent(dir)?.FullName ?? string.Empty;
    }

    throw new FileNotFoundException("Could not locate repo root (scripts/generate-client-package.sh).");
}

static void PrintHelp()
{
    Console.WriteLine("SeedForge");
    Console.WriteLine("  newkeys <outputDir>");
    Console.WriteLine("  issue <privateKeyPemPath> <pepper> <tokenId> <owner> <validToUtc> <hostsCsv> <payloadJsonPath>");
    Console.WriteLine("  wrapjwt <catalogJsonPath> <jwtSigningKey> <envelopePepper> <issuer> <audience> <expUtc>");
    Console.WriteLine("  exportjwk <privateKeyPemPath>");
    Console.WriteLine("  reseal <catalogJsonPath> <privateKeyPemPath>");
    Console.WriteLine("  unwrapjwt <jwtPathOrDash> <jwtSigningKey> <envelopePepper> <issuer> <audience>");
    Console.WriteLine("  packclient <tokenId> [outputDir] [--client-name NAME]");
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
    public List<string> Hosts { get; init; } = new();

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

internal sealed class JwtHeader
{
    [JsonPropertyName("alg")]
    public string Algorithm { get; init; } = "HS256";

    [JsonPropertyName("typ")]
    public string Type { get; init; } = "JWT";
}

internal sealed class JwtEnvelopeClaims
{
    [JsonPropertyName("iss")]
    public string Issuer { get; init; } = string.Empty;

    [JsonPropertyName("aud")]
    public string Audience { get; init; } = string.Empty;

    [JsonPropertyName("exp")]
    public long Exp { get; init; }

    [JsonPropertyName("nbf")]
    public long NotBefore { get; init; }

    [JsonPropertyName("blob")]
    public string Blob { get; init; } = string.Empty;

    [JsonPropertyName("nonce")]
    public string Nonce { get; init; } = string.Empty;

    [JsonPropertyName("tag")]
    public string Tag { get; init; } = string.Empty;
}
