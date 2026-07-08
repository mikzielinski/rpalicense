using System.Text.Json;
using Ops.Runtime.Seed;

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
