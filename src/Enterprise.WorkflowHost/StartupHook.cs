using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;

internal static class StartupHook
{
    public static void Initialize()
    {
        if (!ProcessGate.ShouldActivate())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPS_SEED_QUIET")))
        {
            Environment.SetEnvironmentVariable("OPS_SEED_QUIET", "1");
        }

        var hookDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (string.IsNullOrEmpty(hookDir))
        {
            return;
        }

        var seedPath = Path.Combine(hookDir, "Ops.Runtime.Seed.dll");
        if (!File.Exists(seedPath))
        {
            return;
        }

        var token = Environment.GetEnvironmentVariable("FLOW_RUNTIME_TOKEN")
            ?? Environment.GetEnvironmentVariable("APP_BOOT_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var seed = AssemblyLoadContext.Default.LoadFromAssemblyPath(seedPath);

        var settingsType = seed.GetType("Ops.Runtime.Seed.BootstrapperSettings", throwOnError: true)!;
        settingsType.GetMethod(
                "ApplyFromEnvironment",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(null, null);

        var bootstrapper = seed.GetType("Ops.Runtime.Seed.Bootstrapper", throwOnError: true)!;
        var profileType = seed.GetType("Ops.Runtime.Seed.RuntimeProfile", throwOnError: true)!;
        var tryInit = bootstrapper.GetMethod(
            "TryInitialize",
            BindingFlags.Static | BindingFlags.Public,
            binder: null,
            types: new[] { typeof(string), profileType.MakeByRefType(), typeof(string) },
            modifiers: null)!;

        var args = new object?[] { token, null, null };
        tryInit.Invoke(null, args);
    }
}

internal static class ProcessGate
{
    private static readonly string[] RobotProcessNames =
    {
        "UiPath.Executor",
        "UiRobot",
        "UiPath.Service.UserHost"
    };

    internal static bool ShouldActivate()
    {
        var overrideValue = Environment.GetEnvironmentVariable("OPS_SEED_HOOK");
        if (overrideValue is "0" or "false" or "FALSE" or "no" or "NO")
        {
            return false;
        }

        if (overrideValue is "1" or "true" or "TRUE" or "force" or "FORCE")
        {
            return true;
        }

        var processName = Process.GetCurrentProcess().ProcessName;
        return RobotProcessNames.Any(name =>
            processName.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}
