namespace Ops.Runtime.Seed;

public static class BootstrapperDiagnostics
{
    public static bool ModuleInitAttempted { get; internal set; }
    public static bool ModuleInitSucceeded { get; internal set; }
    public static string? ModuleInitFailure { get; internal set; }

    internal static void Reset()
    {
        ModuleInitAttempted = false;
        ModuleInitSucceeded = false;
        ModuleInitFailure = null;
    }
}
