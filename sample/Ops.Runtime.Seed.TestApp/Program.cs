using System.Text.Json;
using Ops.Runtime.Seed;

ApplyTestHarnessFromEnvironment();
FlowRuntime.KillHostOnFailure = false;

var token = Environment.GetEnvironmentVariable("FLOW_RUNTIME_TOKEN")
    ?? Environment.GetEnvironmentVariable("APP_BOOT_TOKEN")
    ?? string.Empty;

var report = new Dictionary<string, object?>
{
    ["app"] = "Ops.Runtime.Seed.TestApp",
    ["tokenProvided"] = !string.IsNullOrWhiteSpace(token),
};

if (string.IsNullOrWhiteSpace(token))
{
    report["scenario"] = "bez-licencji";
    report["success"] = false;
    report["code"] = "boot-0x01";
    report["message"] = "Brak tokenu runtime — bot nie moze sie zainicjalizowac.";
    WriteReport(report);
    return 2;
}

try
{
    FlowRuntime.Activate(
        token.Trim(),
        out var apiEndpoint,
        out var connectionString,
        out var agentPrompt,
        out var owner,
        out var validToUtc);

    report["scenario"] = "po-nadaniu";
    report["success"] = true;
    report["code"] = Bootstrapper.LastCheck.Code;
    report["usedCache"] = Bootstrapper.LastCheck.UsedCache;
    report["message"] = "Licencja aktywna — profil runtime gotowy.";
    report["lastCheck"] = Bootstrapper.LastCheck;
    report["profile"] = new
    {
        TokenId = token.Trim(),
        owner,
        apiEndpoint,
        ValidToUtc = validToUtc
    };
    WriteReport(report);
    return 0;
}
catch
{
    var check = Bootstrapper.LastCheck;
    report["scenario"] = MapScenario(check.Code);
    report["success"] = false;
    report["code"] = check.Code;
    report["usedCache"] = check.UsedCache;
    report["message"] = DescribeCode(check.Code);
    report["lastCheck"] = check;
    WriteReport(report);
    return 1;
}

static string MapScenario(string code) => code switch
{
    "boot-ok-remote" => "po-nadaniu",
    "boot-ok-cache" => "po-nadaniu-cache",
    "boot-0x12" => "po-deaktywacji",
    "boot-0x11" => "bez-licencji",
    "boot-0x14" => "po-deaktywacji",
    _ => "blad-walidacji"
};

static string DescribeCode(string code) => code switch
{
    "boot-0x11" => "Token nie jest zarejestrowany w katalogu.",
    "boot-0x12" => "Licencja odcieta (enabled=false) — init zablokowany.",
    "boot-0x14" => "Licencja wygasla.",
    "boot-0x15" => "Maszyna spoza listy hosts.",
    "boot-ok-remote" => "Walidacja zdalna OK.",
    "boot-ok-cache" => "Walidacja z cache (offline grace).",
    _ => $"Walidacja nieudana: {code}"
};

static void WriteReport(Dictionary<string, object?> report)
{
    Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    }));
}

static void ApplyTestHarnessFromEnvironment()
{
    var catalogFile = Environment.GetEnvironmentVariable("OPS_SEED_CATALOG_FILE");
    if (!string.IsNullOrWhiteSpace(catalogFile) && File.Exists(catalogFile))
    {
        BootstrapperSettings.CatalogLoaderOverride = _ => Task.FromResult(File.ReadAllText(catalogFile));
    }

    var sourceUrl = Environment.GetEnvironmentVariable("OPS_SEED_SOURCE_URL");
    if (!string.IsNullOrWhiteSpace(sourceUrl))
    {
        BootstrapperSettings.SourceUrl = sourceUrl;
    }

    var pemFile = Environment.GetEnvironmentVariable("OPS_SEED_PUBLIC_SEAL_KEY_FILE");
    if (!string.IsNullOrWhiteSpace(pemFile) && File.Exists(pemFile))
    {
        BootstrapperSettings.PublicSealKeyPem = File.ReadAllText(pemFile);
    }

    var pepper = Environment.GetEnvironmentVariable("OPS_SEED_PEPPER");
    if (!string.IsNullOrWhiteSpace(pepper))
    {
        BootstrapperSettings.Pepper = pepper;
    }

    var envPepper = Environment.GetEnvironmentVariable("OPS_SEED_ENVELOPE_PEPPER");
    if (!string.IsNullOrWhiteSpace(envPepper))
    {
        BootstrapperSettings.EnvelopePepper = envPepper;
    }

    var signingKey = Environment.GetEnvironmentVariable("OPS_SEED_ENVELOPE_SIGNING_KEY");
    if (!string.IsNullOrWhiteSpace(signingKey))
    {
        BootstrapperSettings.EnvelopeSigningKey = signingKey;
    }

    var issuer = Environment.GetEnvironmentVariable("OPS_SEED_ENVELOPE_ISSUER");
    if (!string.IsNullOrWhiteSpace(issuer))
    {
        BootstrapperSettings.EnvelopeIssuer = issuer;
    }

    var audience = Environment.GetEnvironmentVariable("OPS_SEED_ENVELOPE_AUDIENCE");
    if (!string.IsNullOrWhiteSpace(audience))
    {
        BootstrapperSettings.EnvelopeAudience = audience;
    }

    var cachePath = Environment.GetEnvironmentVariable("OPS_SEED_CACHE_PATH");
    if (!string.IsNullOrWhiteSpace(cachePath))
    {
        BootstrapperSettings.CachePathOverride = cachePath;
    }
}
