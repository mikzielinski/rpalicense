using System.Text.Json;

namespace Ops.Runtime.Seed;

internal sealed class CacheStore
{
    private readonly string _path;

    internal CacheStore(string? customPath = null)
    {
        _path = customPath ?? BuildPath();
    }

    internal CachedRecord? Read()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<CachedRecord>(json, Json.Options);
        }
        catch
        {
            return null;
        }
    }

    internal void Write(CachedRecord record)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(record, Json.Options);
        File.WriteAllText(_path, json);
    }

    private static string BuildPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.GetTempPath();
        }

        return Path.Combine(root, "OpsRuntime", "seed.cache.json");
    }
}
