namespace Fullview.Device.Logging;

/// <summary>
/// Gate for the extra diagnostic logging added to help debug on-device sync issues (env var
/// resolution at startup, sync engine internals, background-trigger firings) without a
/// rebuild: toggled by ENABLE_LOGGING in /etc/fullview.env, sourced by run.sh alongside
/// /etc/fullview-sync.env (see docs/device-setup.md). Off by default so normal operation
/// doesn't grow fullview.log unnecessarily; the app's existing always-on status/tap/timing
/// lines in Program.cs are unaffected either way.
/// </summary>
public static class DeviceLog
{
    public static bool Enabled { get; } = IsTruthy(Environment.GetEnvironmentVariable("ENABLE_LOGGING"));

    public static void Debug(string message)
    {
        if (Enabled)
        {
            Console.WriteLine(message);
        }
    }

    private static bool IsTruthy(string? value) =>
        value == "1" || (value is not null && value.Equals("true", StringComparison.OrdinalIgnoreCase));
}
