using System.Diagnostics;

namespace Ops.Runtime.Seed;

internal static class HostGuard
{
    private static readonly string[] KnownExecutableNames =
    {
        "UiRobot.exe",
        "UiPath.Executor.exe",
        "UiPath.Service.UserHost.exe",
        "UiPath.Studio.exe",
        "UiPath.Studio.Web.exe",
        "UiPath.Assistant.exe",
        "UiPath.WorkflowOperations.exe",
        "UiPath.RobotJS.UserHost.exe",
        "UiPath.Proxy.exe",
        "UiPath.Tool.ProcessNode.exe",
        "UiPath.UpdateService.Agent.exe",
        "UiPath.UserCode.Studio.exe",
        "UiPath.ChromeBridge.exe",
        "UiPath.RemoteRuntime.exe"
    };

    internal static int TerminateUiPathProcesses()
    {
        if (!OperatingSystem.IsWindows())
        {
            return 0;
        }

        var killed = new HashSet<int>();

        foreach (var image in KnownExecutableNames)
        {
            TryTaskKillImage(image);
        }

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (!IsUiPathProcessName(process.ProcessName))
                {
                    continue;
                }

                if (!killed.Add(process.Id))
                {
                    continue;
                }

                TryKillProcessTree(process);
            }
            catch
            {
                // Best-effort.
            }
            finally
            {
                process.Dispose();
            }
        }

        foreach (var image in KnownExecutableNames)
        {
            TryTaskKillImage(image);
        }

        return killed.Count;
    }

    private static bool IsUiPathProcessName(string processName) =>
        processName.Equals("UiRobot", StringComparison.OrdinalIgnoreCase) ||
        processName.StartsWith("UiPath", StringComparison.OrdinalIgnoreCase);

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
        }
        catch
        {
            TryTaskKillPid(process.Id);
        }
    }

    private static void TryTaskKillImage(string imageName)
    {
        RunTaskKill($"/F /T /IM {imageName}");
    }

    private static void TryTaskKillPid(int pid)
    {
        RunTaskKill($"/F /T /PID {pid}");
    }

    private static void RunTaskKill(string arguments)
    {
        try
        {
            using var taskKill = Process.Start(new ProcessStartInfo
            {
                FileName = "taskkill.exe",
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            taskKill?.WaitForExit(5000);
        }
        catch
        {
            // Best-effort.
        }
    }
}
