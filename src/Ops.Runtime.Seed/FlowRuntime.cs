namespace Ops.Runtime.Seed;

/// <summary>
/// Public entry point for automation hosts (UiPath). License enforcement is internal.
/// </summary>
public static class FlowRuntime
{
    private static readonly object Gate = new();
    private static string? _boundToken;
    private static volatile bool _revoked;
    private static Timer? _watchdog;

    private const int WatchdogIntervalMinutes = 15;

    /// <summary>
    /// Bind orchestrator credential once at process start (Asset value).
    /// </summary>
    public static void Bind(string orchestratorCredential)
    {
        if (string.IsNullOrWhiteSpace(orchestratorCredential))
        {
            ThrowAccessDenied();
        }

        lock (Gate)
        {
            _boundToken = orchestratorCredential.Trim();
            _revoked = false;
        }

        EnsureLicensed(forceThrow: true);
        EnsureWatchdog();
    }

    public static string ApiEndpoint => EnsureLicensed().ApiEndpoint;

    public static string ConnectionString => EnsureLicensed().ConnectionString;

    public static string AgentSystemPrompt => EnsureLicensed().AgentSystemPrompt;

    public static string Owner => EnsureLicensed().Owner;

    public static DateTimeOffset ValidToUtc => EnsureLicensed().ValidToUtc;

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

        EnsureWatchdog();
    }

    internal static void NotifyBackgroundFailure(string code)
    {
        if (IsPolicyBlock(code))
        {
            _revoked = true;
        }
    }

    private static RuntimeProfile EnsureLicensed(bool forceThrow = false)
    {
        if (_revoked)
        {
            if (forceThrow)
            {
                ThrowAccessDenied();
            }

            throw new InvalidOperationException("Runtime service unavailable.");
        }

        var token = ResolveToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            ThrowAccessDenied();
        }

        if (!Bootstrapper.TryInitialize(token, out var profile) || profile is null)
        {
            var code = Bootstrapper.LastCheck.Code;
            if (IsPolicyBlock(code))
            {
                _revoked = true;
            }

            ThrowAccessDenied();
        }

        return profile;
    }

    private static string? ResolveToken()
    {
        lock (Gate)
        {
            if (!string.IsNullOrWhiteSpace(_boundToken))
            {
                return _boundToken;
            }
        }

        return Environment.GetEnvironmentVariable("FLOW_RUNTIME_TOKEN")
            ?? Environment.GetEnvironmentVariable("APP_BOOT_TOKEN");
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
            var token = ResolveToken();
            if (string.IsNullOrWhiteSpace(token))
            {
                _revoked = true;
                return;
            }

            if (!Bootstrapper.TryInitialize(token, out _))
            {
                NotifyBackgroundFailure(Bootstrapper.LastCheck.Code);
            }
        }
        catch
        {
            _revoked = true;
        }
    }

    private static bool IsPolicyBlock(string code) =>
        code is "boot-0x11" or "boot-0x12" or "boot-0x14" or "boot-0x15" or "boot-0x16"
            or "boot-0x52" or "boot-0x53" or "boot-0x54" or "boot-0x55" or "boot-0x56"
            or "boot-0x57" or "boot-0x58" or "boot-0x59" or "boot-0x5A";

    private static void ThrowAccessDenied() =>
        throw new InvalidOperationException("Runtime service unavailable.");
}
