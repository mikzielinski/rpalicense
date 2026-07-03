using System.Text.Json;
using Ops.Runtime.Seed;

// Minimalny host: zero wywołań Initialize/TryInitialize.
// ModuleInitializer w Ops.Runtime.Seed startuje walidację po ustawieniu tokenu przed launch.

await Task.Delay(TimeSpan.FromMilliseconds(800));

var check = Bootstrapper.LastCheck;
string? currentError = null;
string? apiEndpoint = null;

if (check.Success)
{
    try
    {
        apiEndpoint = Bootstrapper.Current.ApiEndpoint;
    }
    catch (Exception ex)
    {
        currentError = ex.Message;
    }
}
else
{
    try
    {
        _ = Bootstrapper.Current;
        currentError = "Current should be blocked but was accessible";
    }
    catch (Exception ex)
    {
        currentError = ex.Message;
    }
}

var result = new ProbeResult
{
    Code = check.Code,
    Success = check.Success,
    UsedCache = check.UsedCache,
    CurrentBlocked = !check.Success || currentError is not null,
    CurrentError = currentError,
    ApiEndpoint = apiEndpoint
};

Console.WriteLine(JsonSerializer.Serialize(result, ProbeJson.Options));

return check.Success && apiEndpoint is not null ? 0 : 1;

internal sealed record ProbeResult
{
    public string Code { get; init; } = string.Empty;
    public bool Success { get; init; }
    public bool UsedCache { get; init; }
    public bool CurrentBlocked { get; init; }
    public string? CurrentError { get; init; }
    public string? ApiEndpoint { get; init; }
}

internal static class ProbeJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
