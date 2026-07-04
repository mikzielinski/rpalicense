namespace Ops.Runtime.Seed.Tests;

internal static class GitHubPagesTestHelpers
{
    internal static string UnwrapJwt(string jwt)
    {
        var keygen = Path.GetFullPath(Path.Combine(FindRepoRoot(), "keygen", "SeedForge.csproj"));
        EnsureKeygenBuilt(keygen);

        var jwtFile = Path.Combine(Path.GetTempPath(), $"unwrap-{Guid.NewGuid():N}.jwt");
        File.WriteAllText(jwtFile, jwt);
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments =
                    $"run --project \"{keygen}\" -c Release --no-build -- unwrapjwt \"{jwtFile}\" " +
                    "\"test-jwt-signing-key-ops-runtime-seed-2026\" " +
                    "\"test-envelope-pepper-ops-runtime-2026\" " +
                    "\"https://mikzielinski.github.io/rpalicense\" " +
                    "\"ops-runtime-seed\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            })!;
            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            if (proc.ExitCode != 0)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
            }

            return stdout.Trim();
        }
        finally
        {
            try { File.Delete(jwtFile); } catch { /* ignore */ }
        }
    }

    internal static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10; i++)
        {
            if (File.Exists(Path.Combine(dir, "Ops.Runtime.Seed.sln")) ||
                File.Exists(Path.Combine(dir, "scripts", "license-api.sh")))
            {
                return Path.GetFullPath(dir);
            }

            dir = Path.Combine(dir, "..");
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    private static void EnsureKeygenBuilt(string keygenProject)
    {
        var dll = Path.Combine(Path.GetDirectoryName(keygenProject)!, "bin", "Release", "net8.0", "SeedForge.dll");
        if (File.Exists(dll))
        {
            return;
        }

        using var build = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{keygenProject}\" -c Release -v q",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        })!;
        build.WaitForExit();
        if (build.ExitCode != 0)
        {
            throw new InvalidOperationException(build.StandardError.ReadToEnd());
        }
    }
}
