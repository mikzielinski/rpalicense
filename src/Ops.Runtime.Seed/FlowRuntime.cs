namespace Ops.Runtime.Seed;

/// <summary>
/// Single public entry point for automation hosts. All license logic is internal.
/// </summary>
public static class FlowRuntime
{
    private static readonly object Gate = new();
    private static string? _boundToken;
    private static volatile bool _activated;
    private static Timer? _watchdog;

    private const int WatchdogIntervalMinutes = 15;

    /// <summary>
    /// When false (test harness only), failures throw instead of terminating the host process.
    /// </summary>
    internal static bool KillHostOnFailure { get; set; } = true;

    /// <summary>
    /// One call at process start: validate credential, return runtime config, start background watchdog.
    /// On failure the robot process is terminated (UiPath job hard-stops).
    /// </summary>
    public static void Activate(
        string orchestratorCredential,
        out string apiEndpoint,
        out string connectionString,
        out string agentPrompt,
        out string owner,
        out string validToUtc)
    {
        apiEndpoint = string.Empty;
        connectionString = string.Empty;
        agentPrompt = string.Empty;
        owner = string.Empty;
        validToUtc = string.Empty;

        if (string.IsNullOrWhiteSpace(orchestratorCredential))
        {
            FailHost();
        }

        lock (Gate)
        {
            _boundToken = orchestratorCredential.Trim();
        }

        if (!Bootstrapper.TryInitialize(_boundToken, out var profile) || profile is null)
        {
            FailHost();
        }

        lock (Gate)
        {
            _activated = true;
        }

        apiEndpoint = profile!.ApiEndpoint;
        connectionString = profile.ConnectionString;
        agentPrompt = profile.AgentSystemPrompt;
        owner = profile.Owner;
        validToUtc = profile.ValidToUtc.ToString("u");

        EnsureWatchdog();
    }

    internal static void NotifyBackgroundInit(string token, bool succeeded)
    {
        if (!succeeded || string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        lock (Gate)
        {
            _boundToken ??= token.Trim();
        }
    }

    internal static void NotifyBackgroundFailure(string code)
    {
        if (!IsPolicyBlock(code))
        {
            return;
        }

        if (_activated)
        {
            FailHost();
        }
    }

    private static void EnsureWatchdog()
    {
        if (_watchdog is not null)
        {
            return;
        }

        _watchdog = new Timer(
            _ => WatchdogTick(),
            null,
            TimeSpan.FromMinutes(WatchdogIntervalMinutes),
            TimeSpan.FromMinutes(WatchdogIntervalMinutes));
    }

    private static void WatchdogTick()
    {
        try
        {
            string? token;
            lock (Gate)
            {
                token = _boundToken;
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                FailHost();
                return;
            }

            if (!Bootstrapper.TryInitialize(token, out _))
            {
                NotifyBackgroundFailure(Bootstrapper.LastCheck.Code);
            }
        }
        catch
        {
            FailHost();
        }
    }

    private static void FailHost()
    {
        if (KillHostOnFailure)
        {
            Environment.Exit(1);
        }

        throw new InvalidOperationException("Runtime service unavailable.");
    }

    private static bool IsPolicyBlock(string code) =>
        code is "boot-0x11" or "boot-0x12" or "boot-0x14" or "boot-0x15" or "boot-0x16"
            or "boot-0x52" or "boot-0x53" or "boot-0x54" or "boot-0x55" or "boot-0x56"
            or "boot-0x57" or "boot-0x58" or "boot-0x59" or "boot-0x5A";
}
