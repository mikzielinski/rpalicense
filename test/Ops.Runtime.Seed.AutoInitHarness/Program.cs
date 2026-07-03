using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Ops.Runtime.Seed.AutoInitHarness;

internal static class Program
{
    private static int Main(string[] args)
    {
        var fixturesRoot = args.Length > 0
            ? Path.GetFullPath(args[0])
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "fixtures"));

        if (!Directory.Exists(fixturesRoot))
        {
            Console.Error.WriteLine("Run test/scripts/generate-fixtures.sh first.");
            return 1;
        }

        var config = FixtureConfig.Load(fixturesRoot);
        var probeDll = ResolveProbeDll();
        if (probeDll is null)
        {
            Console.Error.WriteLine("Build demo/Ops.Runtime.Seed.AutoInitProbe first.");
            return 1;
        }

        var scenarios = BuildScenarios(config);
        using var server = new SeedJwtServer(fixturesRoot);
        var failures = 0;

        Console.WriteLine("Ops.Runtime.Seed AutoInit harness (no explicit Initialize in probe)");
        Console.WriteLine($"Probe: {probeDll}");
        Console.WriteLine($"Fixtures: {fixturesRoot}");
        Console.WriteLine();

        foreach (var scenario in scenarios)
        {
            server.Start(config.SeedPath(scenario.SeedVariant));
            var env = BuildEnv(config, server.SourceUrl, scenario);
            var sw = Stopwatch.StartNew();
            var (exitCode, json) = RunProbe(probeDll, env);
            sw.Stop();

            var result = ParseResult(json);
            var passed = Evaluate(scenario, result, exitCode, out var detail);
            PrintLine(scenario.Name, passed, scenario.ExpectedCode, result?.Code ?? "-", sw.ElapsedMilliseconds, detail);
            if (!passed)
            {
                failures++;
            }
        }

        Console.WriteLine();
        Console.WriteLine(failures == 0
            ? $"All {scenarios.Count} auto-init scenarios passed."
            : $"{failures} of {scenarios.Count} auto-init scenarios failed.");
        return failures == 0 ? 0 : 2;
    }

    private static string? ResolveProbeDll()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir, "demo", "Ops.Runtime.Seed.AutoInitProbe", "bin", "Debug", "net8.0", "Ops.Runtime.Seed.AutoInitProbe.dll");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            var parent = Directory.GetParent(dir);
            if (parent is null)
            {
                break;
            }

            dir = parent.FullName;
        }

        return null;
    }

    private static List<AutoScenario> BuildScenarios(FixtureConfig config) =>
    [
        new("valid-remote", config.ValidToken, "ROBOT01", "valid", true, "boot-ok-remote", ExpectCurrent: true),
        new("no-token", null, "ROBOT01", "valid", false, "boot-0x00", ExpectCurrentBlocked: true),
        new("unknown-token", "RT-DOES-NOT-EXIST", "ROBOT01", "valid", false, "boot-0x11", ExpectCurrentBlocked: true),
        new("disabled-token", config.ValidToken, "ROBOT01", "disabled", false, "boot-0x12", ExpectCurrentBlocked: true),
        new("expired-token", config.ValidToken, "ROBOT01", "expired", false, "boot-0x14", ExpectCurrentBlocked: true),
        new("wrong-machine", config.ValidToken, "UNKNOWN-MACHINE", "valid", false, "boot-0x15", ExpectCurrentBlocked: true),
        new("invalid-seal", config.ValidToken, "ROBOT01", "bad-seal", false, "boot-0x16", ExpectCurrentBlocked: true),
    ];

    private static Dictionary<string, string?> BuildEnv(FixtureConfig config, string sourceUrl, AutoScenario scenario)
    {
        var env = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["FLOW_RUNTIME_SOURCE_URL"] = sourceUrl,
            ["FLOW_RUNTIME_PEPPER"] = config.Pepper,
            ["FLOW_RUNTIME_ENVELOPE_PEPPER"] = config.EnvelopePepper,
            ["FLOW_RUNTIME_ENVELOPE_SIGNING_KEY"] = config.EnvelopeSigningKey,
            ["FLOW_RUNTIME_ENVELOPE_ISSUER"] = config.EnvelopeIssuer,
            ["FLOW_RUNTIME_ENVELOPE_AUDIENCE"] = config.EnvelopeAudience,
            ["FLOW_RUNTIME_PUBLIC_SEAL_KEY_PEM_FILE"] = config.PublicKeyPemPath,
            ["FLOW_RUNTIME_MACHINE"] = scenario.Machine,
            ["FLOW_RUNTIME_CACHE_PATH"] = Path.Combine(config.FixturesRoot, "cache", $"autoinit-{scenario.Name}.json"),
        };

        if (!string.IsNullOrWhiteSpace(scenario.Token))
        {
            env["APP_BOOT_TOKEN"] = scenario.Token;
            env["FLOW_RUNTIME_TOKEN"] = scenario.Token;
        }

        return env;
    }

    private static (int ExitCode, string Output) RunProbe(string probeDll, Dictionary<string, string?> env)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList = { probeDll },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var (key, value) in env)
        {
            if (value is not null)
            {
                psi.Environment[key] = value;
            }
            else
            {
                psi.Environment.Remove(key);
            }
        }

        psi.Environment.Remove("APP_BOOT_TOKEN");
        psi.Environment.Remove("FLOW_RUNTIME_TOKEN");
        if (!string.IsNullOrWhiteSpace(env.GetValueOrDefault("APP_BOOT_TOKEN")))
        {
            psi.Environment["APP_BOOT_TOKEN"] = env["APP_BOOT_TOKEN"]!;
            psi.Environment["FLOW_RUNTIME_TOKEN"] = env["FLOW_RUNTIME_TOKEN"]!;
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start probe");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(TimeSpan.FromSeconds(30));
        return (process.ExitCode, string.IsNullOrWhiteSpace(stdout) ? stderr : stdout);
    }

    private static ProbeResult? ParseResult(string output)
    {
        try
        {
            var line = output.Trim().Split('\n').LastOrDefault(l => l.StartsWith('{')) ?? output;
            return JsonSerializer.Deserialize<ProbeResult>(line, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static bool Evaluate(AutoScenario scenario, ProbeResult? result, int exitCode, out string detail)
    {
        if (result is null)
        {
            detail = "probe output parse failed";
            return false;
        }

        if (!string.Equals(result.Code, scenario.ExpectedCode, StringComparison.Ordinal))
        {
            detail = $"code mismatch";
            return false;
        }

        if (result.Success != scenario.ExpectSuccess)
        {
            detail = $"Success expected {scenario.ExpectSuccess}, got {result.Success}";
            return false;
        }

        if (scenario.ExpectCurrent && string.IsNullOrWhiteSpace(result.ApiEndpoint))
        {
            detail = "expected Current.ApiEndpoint";
            return false;
        }

        if (scenario.ExpectCurrentBlocked && !result.CurrentBlocked)
        {
            detail = "expected Current to be blocked";
            return false;
        }

        detail = scenario.ExpectSuccess ? "remote ok" : $"blocked: {result.CurrentError ?? "ok"}";
        return true;
    }

    private static void PrintLine(string name, bool passed, string expected, string actual, long ms, string detail)
    {
        Console.ForegroundColor = passed ? ConsoleColor.Green : ConsoleColor.Red;
        Console.Write(passed ? "PASS" : "FAIL");
        Console.ResetColor();
        Console.WriteLine($"  {name,-18} ({ms,4} ms)  expected={expected}  actual={actual}  {detail}");
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

internal sealed record AutoScenario(
    string Name,
    string? Token,
    string Machine,
    string SeedVariant,
    bool ExpectSuccess,
    string ExpectedCode,
    bool ExpectCurrent = false,
    bool ExpectCurrentBlocked = false);

internal sealed record ProbeResult
{
    public string Code { get; init; } = string.Empty;
    public bool Success { get; init; }
    public bool UsedCache { get; init; }
    public bool CurrentBlocked { get; init; }
    public string? CurrentError { get; init; }
    public string? ApiEndpoint { get; init; }
}

internal sealed class FixtureConfig
{
    internal string FixturesRoot { get; }
    internal string ValidToken { get; }
    internal string Pepper { get; }
    internal string EnvelopePepper { get; }
    internal string EnvelopeSigningKey { get; }
    internal string EnvelopeIssuer { get; }
    internal string EnvelopeAudience { get; }
    internal string PublicKeyPemPath { get; }

    internal FixtureConfig(string fixturesRoot, Dictionary<string, string> metadata)
    {
        FixturesRoot = fixturesRoot;
        ValidToken = metadata["validToken"];
        Pepper = metadata["pepper"];
        EnvelopePepper = metadata["envelopePepper"];
        EnvelopeSigningKey = metadata["envelopeSigningKey"];
        EnvelopeIssuer = metadata["envelopeIssuer"];
        EnvelopeAudience = metadata["envelopeAudience"];
        PublicKeyPemPath = Path.Combine(fixturesRoot, "keys", "seal.public.pem");
    }

    internal static FixtureConfig Load(string fixturesRoot)
    {
        var metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(fixturesRoot, "metadata.json")))!;
        return new FixtureConfig(fixturesRoot, metadata);
    }

    internal string SeedPath(string variant) => Path.Combine(FixturesRoot, $"seed.{variant}.jwt");
}

internal sealed class SeedJwtServer : IDisposable
{
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private string _seedPath = string.Empty;
    private int _port;

    internal SeedJwtServer(string _) { }

    internal void Start(string seedPath)
    {
        Stop();
        _seedPath = seedPath;
        _port = FindFreePort();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
        _listener.Start();
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    internal string SourceUrl => $"http://127.0.0.1:{_port}/seed.jwt";

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is { IsListening: true })
        {
            HttpListenerContext context;
            try
            {
                context = await _listener!.GetContextAsync().WaitAsync(ct).ConfigureAwait(false);
            }
            catch when (ct.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                continue;
            }

            var body = File.Exists(_seedPath) ? await File.ReadAllTextAsync(_seedPath, ct).ConfigureAwait(false) : string.Empty;
            var bytes = Encoding.UTF8.GetBytes(body);
            context.Response.StatusCode = string.IsNullOrWhiteSpace(body) ? 404 : 200;
            context.Response.ContentType = "text/plain";
            await context.Response.OutputStream.WriteAsync(bytes, ct).ConfigureAwait(false);
            context.Response.Close();
        }
    }

    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public void Dispose() => Stop();

    private void Stop()
    {
        try
        {
            _cts?.Cancel();
            _listener?.Stop();
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch { /* ignore */ }
        finally
        {
            _cts?.Dispose();
            _listener = null;
            _loop = null;
        }
    }
}
