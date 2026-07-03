using System.Text.Json;

namespace Ops.Runtime.Seed;

internal static class Json
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };
}
