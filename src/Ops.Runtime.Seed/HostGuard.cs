using System.Diagnostics;

namespace Ops.Runtime.Seed;

internal static class HostGuard
{
    private static readonly string[] UiPathProcessNames =
    {
        "UiRobot",
        "UiPath.Executor",
        "UiPath.Service.UserHost",
        "UiPath.Studio",
        "UiPath.Assistant",
        "UiPath.WorkflowOperations",
        "UiPath.RobotJS.UserHost"
    };

    internal static void TerminateUiPathProcesses()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        foreach (var name in UiPathProcessNames)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(name);
            }
            catch
            {
                continue;
            }

            foreach (var process in processes)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best-effort — host may already be shutting down.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
    }
}
