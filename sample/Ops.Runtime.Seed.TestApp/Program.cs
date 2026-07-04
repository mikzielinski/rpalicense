using System.Text.Json;
using Ops.Runtime.Seed;

// Minimalna aplikacja bota UiPath — używa biblioteki tak jak w README:
//   var profile = Bootstrapper.Initialize(tokenFromOrchestratorAsset);

BootstrapperSettings.ApplyFromEnvironment();

var token = Environment.GetEnvironmentVariable("FLOW_RUNTIME_TOKEN")
    ?? Environment.GetEnvironmentVariable("APP_BOOT_TOKEN")
    ?? string.Empty;

var report = new Dictionary<string, object?>
{
    ["app"] = "Ops.Runtime.Seed.TestApp",
    ["tokenProvided"] = !string.IsNullOrWhiteSpace(token),
    ["sourceUrl"] = BootstrapperSettings.SourceUrl
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

if (!Bootstrapper.TryInitialize(token.Trim(), out var profile))
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

report["scenario"] = "po-nadaniu";
report["success"] = true;
report["code"] = Bootstrapper.LastCheck.Code;
report["usedCache"] = Bootstrapper.LastCheck.UsedCache;
report["message"] = "Licencja aktywna — profil runtime gotowy.";
report["lastCheck"] = Bootstrapper.LastCheck;
report["profile"] = new
{
    profile!.TokenId,
    profile.Owner,
    profile.ApiEndpoint,
    profile.ValidToUtc
};
WriteReport(report);
return 0;

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
