using System.Text.Json;

namespace Ops.Runtime.Seed;

internal sealed class CacheStore
{
    private readonly string _path;

    internal CacheStore(string? customPath = null)
    {
        _path = !string.IsNullOrWhiteSpace(customPath)
            ? customPath
            : BuildPath();
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
        foreach (var root in CandidateRoots())
        {
            if (TryResolveCachePath(root, out var path))
            {
                return path;
            }
        }

        return Path.Combine(Path.GetTempPath(), "OpsRuntime", "seed.cache.json");
    }

    private static IEnumerable<string> CandidateRoots()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdg))
        {
            yield return xdg;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
        {
            yield return Path.Combine(home, ".local", "share");
        }

        var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (!string.IsNullOrWhiteSpace(common))
        {
            yield return common;
        }

        yield return Path.GetTempPath();
    }

    private static bool TryResolveCachePath(string root, out string path)
    {
        path = Path.Combine(root, "OpsRuntime", "seed.cache.json");
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(dir))
            {
                return false;
            }

            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, ".write-probe");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
