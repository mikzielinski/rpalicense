using System.Text.Json;
using Ops.Runtime.Seed;

WaitForModuleInitCompletion(TimeSpan.FromSeconds(30));

var report = new Dictionary<string, object?>
{
    ["moduleInitAttempted"] = BootstrapperDiagnostics.ModuleInitAttempted,
    ["moduleInitSucceeded"] = BootstrapperDiagnostics.ModuleInitSucceeded,
    ["moduleInitFailure"] = BootstrapperDiagnostics.ModuleInitFailure,
    ["lastCheck"] = Bootstrapper.LastCheck,
    ["profile"] = BootstrapperDiagnostics.ModuleInitSucceeded
        ? Bootstrapper.Current
        : null
};

Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = false
}));

return BootstrapperDiagnostics.ModuleInitSucceeded ? 0 : 1;

static void WaitForModuleInitCompletion(TimeSpan timeout)
{
    if (!BootstrapperDiagnostics.ModuleInitAttempted)
    {
        return;
    }

    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline
           && !BootstrapperDiagnostics.ModuleInitSucceeded
           && string.IsNullOrWhiteSpace(BootstrapperDiagnostics.ModuleInitFailure))
    {
        Thread.Sleep(20);
    }
}
