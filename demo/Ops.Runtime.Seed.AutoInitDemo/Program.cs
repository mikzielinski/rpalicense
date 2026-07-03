using System.Text.Json;
using Ops.Runtime.Seed;
// Po załadowaniu biblioteki ModuleInitializer czyta FLOW_RUNTIME_TOKEN / APP_BOOT_TOKEN
// i samodzielnie łączy się z seed.jwt (np. na GitHub Pages).

Console.WriteLine("Ops.Runtime.Seed — AutoInit demo");
Console.WriteLine("Brak jawnego Initialize() — tylko odczyt stanu po ModuleInit.");
Console.WriteLine();

PrintEnvHints();

WaitForAutoInit(TimeSpan.FromSeconds(20));

// Dostęp do typu ładuje assembly → ModuleInitializer startuje init w tle.
var snapshot = Bootstrapper.LastCheck;
PrintSnapshot("LastCheck (po auto-init)", snapshot);

if (snapshot.Success)
{
    try
    {
        var profile = Bootstrapper.Current;
        Console.WriteLine();
        Console.WriteLine("RuntimeProfile (Current):");
        Console.WriteLine($"  TokenId          : {profile.TokenId}");
        Console.WriteLine($"  Owner            : {profile.Owner}");
        Console.WriteLine($"  ApiEndpoint      : {profile.ApiEndpoint}");
        Console.WriteLine($"  ValidToUtc       : {profile.ValidToUtc:O}");
        Console.WriteLine($"  AgentSystemPrompt: {Truncate(profile.AgentSystemPrompt, 80)}");
        Console.WriteLine();
        Console.WriteLine("Połączenie OK — profil runtime gotowy do użycia w bocie.");
        return 0;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Current niedostępny mimo Success: {ex.Message}");
        return 2;
    }
}

Console.WriteLine();
Console.WriteLine("Auto-init nie powiódł się lub token nie był ustawiony przed startem procesu.");
Console.WriteLine("Ustaw APP_BOOT_TOKEN / FLOW_RUNTIME_TOKEN i zmienne FLOW_RUNTIME_* (patrz demo/pages-demo.env).");
return snapshot.Code == "boot-0x00" ? 3 : 1;

static void WaitForAutoInit(TimeSpan timeout)
{
    var token = Environment.GetEnvironmentVariable("FLOW_RUNTIME_TOKEN")
        ?? Environment.GetEnvironmentVariable("APP_BOOT_TOKEN");
    if (string.IsNullOrWhiteSpace(token))
    {
        return;
    }

    var deadline = DateTimeOffset.UtcNow + timeout;
    while (DateTimeOffset.UtcNow < deadline)
    {
        if (Bootstrapper.LastCheck.Code is not ("boot-0x00" or ""))
        {
            return;
        }

        Thread.Sleep(100);
    }

    Console.WriteLine("(timeout oczekiwania na auto-init — sprawdź URL seed.jwt i token)");
}

static void PrintEnvHints()
{
    var token = Environment.GetEnvironmentVariable("FLOW_RUNTIME_TOKEN")
        ?? Environment.GetEnvironmentVariable("APP_BOOT_TOKEN");
    var source = Environment.GetEnvironmentVariable("FLOW_RUNTIME_SOURCE_URL") ?? "(domyślny z biblioteki)";

    Console.WriteLine($"FLOW_RUNTIME_SOURCE_URL : {source}");
    Console.WriteLine($"Token env               : {(string.IsNullOrWhiteSpace(token) ? "(brak)" : Mask(token))}");
    Console.WriteLine();
}

static void PrintSnapshot(string title, ValidationSnapshot snapshot)
{
    Console.WriteLine(title + ":");
    var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine(json);
}

static string Mask(string value) =>
    value.Length <= 8 ? "***" : value[..4] + "..." + value[^4..];

static string Truncate(string value, int max) =>
    value.Length <= max ? value : value[..max] + "...";
