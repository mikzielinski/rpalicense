using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ops.Runtime.Seed;

namespace Ops.Runtime.Seed.TestHarness;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static int Main(string[] args)
    {
        var fixturesRoot = args.Length > 0
            ? Path.GetFullPath(args[0])
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "fixtures"));

        if (!Directory.Exists(fixturesRoot))
        {
            Console.Error.WriteLine($"Fixtures directory not found: {fixturesRoot}");
            Console.Error.WriteLine("Run: test/scripts/generate-fixtures.sh");
            return 1;
        }

        var config = FixtureConfig.Load(fixturesRoot);
        var scenarios = BuildScenarios(config);
        var failures = 0;
        var server = new SeedJwtServer(config.FixturesRoot);

        Console.WriteLine("Ops.Runtime.Seed test harness");
        Console.WriteLine($"Fixtures: {fixturesRoot}");
        Console.WriteLine($"Scenarios: {scenarios.Count}");
        Console.WriteLine();

        foreach (var scenario in scenarios)
        {
            if (string.Equals(scenario.Name, "cache-fallback", StringComparison.Ordinal))
            {
                failures += RunCacheFallbackScenario(config, server) ? 0 : 1;
                continue;
            }

            server.Start(config.SeedJwtForScenario(scenario.Name));
            ApplyEnvironment(config, scenario, server.SourceUrl);
            ClearBootstrapperState();

            var sw = Stopwatch.StartNew();
            var result = RunScenario(scenario);
            sw.Stop();

            var status = result.Passed ? "PASS" : "FAIL";
            var color = result.Passed ? ConsoleColor.Green : ConsoleColor.Red;
            Console.ForegroundColor = color;
            Console.Write($"{status}");
            Console.ResetColor();
            Console.WriteLine($"  {scenario.Name,-22} ({sw.ElapsedMilliseconds,4} ms)  expected={scenario.ExpectedCode}  actual={result.ActualCode}");
            if (!result.Passed)
            {
                Console.WriteLine($"       {result.Details}");
                failures++;
            }
        }

        server.Dispose();
        Console.WriteLine();
        Console.WriteLine(failures == 0
            ? $"All {scenarios.Count} scenarios passed."
            : $"{failures} of {scenarios.Count} scenarios failed.");
        return failures == 0 ? 0 : 2;
    }

    private static List<Scenario> BuildScenarios(FixtureConfig config) =>
    [
        new("valid-remote", config.ValidToken, config.AllowedMachine, true, "boot-ok-remote", UsedCache: false),
        new("empty-token", "", config.AllowedMachine, false, "boot-0x01"),
        new("unknown-token", "RT-DOES-NOT-EXIST", config.AllowedMachine, false, "boot-0x11"),
        new("disabled-token", config.ValidToken, config.AllowedMachine, false, "boot-0x12", SeedVariant: "disabled"),
        new("expired-token", config.ValidToken, config.AllowedMachine, false, "boot-0x14", SeedVariant: "expired"),
        new("wrong-machine", config.ValidToken, "UNKNOWN-MACHINE", false, "boot-0x15"),
        new("invalid-seal", config.ValidToken, config.AllowedMachine, false, "boot-0x16", SeedVariant: "bad-seal"),
        new("cache-fallback", config.ValidToken, config.AllowedMachine, true, "boot-ok-cache", UsedCache: true),
        new("try-init-failure", "RT-DOES-NOT-EXIST", config.AllowedMachine, false, "boot-0x11", UseTryInitialize: true)
    ];

    private static ScenarioResult RunScenario(Scenario scenario)
    {
        try
        {
            if (scenario.UseTryInitialize)
            {
                var ok = Bootstrapper.TryInitialize(scenario.Token, out var profile, scenario.Machine);
                var check = Bootstrapper.LastCheck;
                if (ok || profile is not null)
                {
                    return Fail(scenario.ExpectedCode, check.Code, "TryInitialize unexpectedly succeeded.");
                }

                return Match(scenario, check);
            }

            if (scenario.ExpectSuccess)
            {
                var profile = Bootstrapper.Initialize(scenario.Token, scenario.Machine);
                var check = Bootstrapper.LastCheck;
                if (!check.Success)
                {
                    return Fail(scenario.ExpectedCode, check.Code, $"Initialize failed: {check.Notes}");
                }

                if (check.UsedCache != scenario.UsedCache)
                {
                    return Fail(scenario.ExpectedCode, check.Code,
                        $"UsedCache expected {scenario.UsedCache}, got {check.UsedCache}.");
                }

                if (string.IsNullOrWhiteSpace(profile.ApiEndpoint))
                {
                    return Fail(scenario.ExpectedCode, check.Code, "Profile.ApiEndpoint is empty.");
                }

                return Match(scenario, check);
            }

            try
            {
                _ = Bootstrapper.Initialize(scenario.Token, scenario.Machine);
                var unexpected = Bootstrapper.LastCheck;
                return Fail(scenario.ExpectedCode, unexpected.Code, "Initialize unexpectedly succeeded.");
            }
            catch (Exception ex)
            {
                var check = Bootstrapper.LastCheck;
                var code = ExtractCode(ex, check.Code);
                if (!string.Equals(code, scenario.ExpectedCode, StringComparison.Ordinal))
                {
                    return Fail(scenario.ExpectedCode, code, ex.Message);
                }

                if (!check.Success)
                {
                    return new ScenarioResult(true, code, check.Notes);
                }

                return Fail(scenario.ExpectedCode, code, "LastCheck.Success should be false.");
            }
        }
        catch (Exception ex)
        {
            return Fail(scenario.ExpectedCode, "boot-0xFF", ex.Message);
        }
    }

    private static ScenarioResult Match(Scenario scenario, ValidationSnapshot check)
    {
        if (!string.Equals(check.Code, scenario.ExpectedCode, StringComparison.Ordinal))
        {
            return Fail(scenario.ExpectedCode, check.Code, check.Notes);
        }

        return new ScenarioResult(true, check.Code, check.Notes);
    }

    private static ScenarioResult Fail(string expected, string actual, string details) =>
        new(false, actual, $"expected {expected}: {details}");

    private static string ExtractCode(Exception ex, string fallback)
    {
        if (ex is InvalidOperationException or CryptographicException)
        {
            return string.IsNullOrWhiteSpace(ex.Message) ? fallback : ex.Message.Trim();
        }

        return fallback;
    }

    private static bool RunCacheFallbackScenario(FixtureConfig config, SeedJwtServer server)
    {
        var scenario = new Scenario("cache-fallback", config.ValidToken, config.AllowedMachine, true, "boot-ok-cache", UsedCache: true);
        var cachePath = config.CachePathForScenario(scenario.Name);
        if (File.Exists(cachePath))
        {
            File.Delete(cachePath);
        }

        server.Start(config.SeedJwtForScenario("valid-remote"));
        ApplyEnvironment(config, scenario, server.SourceUrl);

        try
        {
            _ = Bootstrapper.Initialize(config.ValidToken, config.AllowedMachine);
            if (!Bootstrapper.LastCheck.Success || Bootstrapper.LastCheck.UsedCache)
            {
                Console.WriteLine("FAIL  cache-fallback        seed step failed");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL  cache-fallback        seed step exception: {ex.Message}");
            return false;
        }

        server.Stop();
        ApplyEnvironment(config, scenario, "http://127.0.0.1:1/unreachable/seed.jwt");

        try
        {
            _ = Bootstrapper.Initialize(config.ValidToken, config.AllowedMachine);
            var check = Bootstrapper.LastCheck;
            var passed = check.Success
                && check.UsedCache
                && string.Equals(check.Code, "boot-ok-cache", StringComparison.Ordinal);
            var status = passed ? "PASS" : "FAIL";
            Console.WriteLine($"{status}  cache-fallback        expected=boot-ok-cache  actual={check.Code}  usedCache={check.UsedCache}");
            return passed;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL  cache-fallback        {ex.Message}");
            return false;
        }
    }

    private static void ApplyEnvironment(FixtureConfig config, Scenario scenario, string sourceUrl)
    {
        Environment.SetEnvironmentVariable("FLOW_RUNTIME_SOURCE_URL", sourceUrl);
        Environment.SetEnvironmentVariable("FLOW_RUNTIME_PEPPER", config.Pepper);
        Environment.SetEnvironmentVariable("FLOW_RUNTIME_ENVELOPE_PEPPER", config.EnvelopePepper);
        Environment.SetEnvironmentVariable("FLOW_RUNTIME_ENVELOPE_SIGNING_KEY", config.EnvelopeSigningKey);
        Environment.SetEnvironmentVariable("FLOW_RUNTIME_ENVELOPE_ISSUER", config.EnvelopeIssuer);
        Environment.SetEnvironmentVariable("FLOW_RUNTIME_ENVELOPE_AUDIENCE", config.EnvelopeAudience);
        Environment.SetEnvironmentVariable("FLOW_RUNTIME_PUBLIC_SEAL_KEY_PEM_FILE", config.PublicKeyPemPath);
        Environment.SetEnvironmentVariable("FLOW_RUNTIME_CACHE_PATH", config.CachePathForScenario(scenario.Name));
        Environment.SetEnvironmentVariable("FLOW_RUNTIME_TOKEN", null);
        Environment.SetEnvironmentVariable("APP_BOOT_TOKEN", null);
    }

    private static void ClearBootstrapperState()
    {
        // ModuleInit may have run; subsequent Initialize calls overwrite state when successful/failed.
    }
}

internal sealed record Scenario(
    string Name,
    string Token,
    string Machine,
    bool ExpectSuccess,
    string ExpectedCode,
    bool UsedCache = false,
    string? SeedVariant = null,
    bool StopServerBeforeRun = false,
    bool UseTryInitialize = false);

internal sealed record ScenarioResult(bool Passed, string ActualCode, string Details);

internal sealed class FixtureConfig
{
    internal string FixturesRoot { get; }
    internal string ValidToken { get; }
    internal string AllowedMachine { get; }
    internal string Pepper { get; }
    internal string EnvelopePepper { get; }
    internal string EnvelopeSigningKey { get; }
    internal string EnvelopeIssuer { get; }
    internal string EnvelopeAudience { get; }
    internal string PublicKeyPemPath { get; }

    private readonly Dictionary<string, string> _seedByVariant;

    private FixtureConfig(string fixturesRoot, Dictionary<string, string> metadata, Dictionary<string, string> seedByVariant)
    {
        FixturesRoot = fixturesRoot;
        ValidToken = metadata["validToken"];
        AllowedMachine = metadata["allowedMachine"];
        Pepper = metadata["pepper"];
        EnvelopePepper = metadata["envelopePepper"];
        EnvelopeSigningKey = metadata["envelopeSigningKey"];
        EnvelopeIssuer = metadata["envelopeIssuer"];
        EnvelopeAudience = metadata["envelopeAudience"];
        PublicKeyPemPath = Path.Combine(fixturesRoot, "keys", "seal.public.pem");
        _seedByVariant = seedByVariant;
    }

    internal static FixtureConfig Load(string fixturesRoot)
    {
        var metadataPath = Path.Combine(fixturesRoot, "metadata.json");
        if (!File.Exists(metadataPath))
        {
            throw new FileNotFoundException("metadata.json not found. Run generate-fixtures.sh first.", metadataPath);
        }

        var metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(metadataPath))
            ?? throw new InvalidOperationException("Invalid metadata.json");

        var seedByVariant = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["valid"] = Path.Combine(fixturesRoot, "seed.valid.jwt"),
            ["disabled"] = Path.Combine(fixturesRoot, "seed.disabled.jwt"),
            ["expired"] = Path.Combine(fixturesRoot, "seed.expired.jwt"),
            ["bad-seal"] = Path.Combine(fixturesRoot, "seed.bad-seal.jwt")
        };

        return new FixtureConfig(fixturesRoot, metadata, seedByVariant);
    }

    internal string SeedJwtForScenario(string scenarioName) => scenarioName switch
    {
        "disabled-token" => _seedByVariant["disabled"],
        "expired-token" => _seedByVariant["expired"],
        "invalid-seal" => _seedByVariant["bad-seal"],
        _ => _seedByVariant["valid"]
    };

    internal string CachePathForScenario(string scenarioName) =>
        Path.Combine(FixturesRoot, "cache", $"{scenarioName}.cache.json");
}

internal sealed class SeedJwtServer : IDisposable
{
    private readonly string _fixturesRoot;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private string _seedPath = string.Empty;
    private int _port;

    internal SeedJwtServer(string fixturesRoot)
    {
        _fixturesRoot = fixturesRoot;
    }

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
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    internal void Stop()
    {
        StopInternal();
    }

    public void Dispose() => StopInternal();

    private void StopInternal()
    {
        try
        {
            _cts?.Cancel();
            _listener?.Stop();
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // ignore shutdown races
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _listener = null;
            _loop = null;
        }
    }
}
