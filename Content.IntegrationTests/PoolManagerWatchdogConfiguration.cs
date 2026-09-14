using System.Globalization;

namespace Content.IntegrationTests;

internal static class PoolManagerWatchdogConfiguration
{
    internal const string EnvironmentVariableName = "SOLREIGN_INTEGRATION_WATCHDOG_MINUTES";

    private const int DefaultMinutes = 20;
    private const int MinimumMinutes = DefaultMinutes;
    private const int MaximumMinutes = 30;
    private static readonly TimeSpan HardStopGrace = TimeSpan.FromMinutes(1);

    internal static WatchdogLimits ResolveFromEnvironment()
    {
        return Resolve(Environment.GetEnvironmentVariable(EnvironmentVariableName));
    }

    internal static WatchdogLimits Resolve(string raw)
    {
        if (raw == null)
            return FromMinutes(DefaultMinutes, false);

        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) ||
            minutes < MinimumMinutes ||
            minutes > MaximumMinutes)
        {
            throw new InvalidOperationException(
                $"{EnvironmentVariableName} must be an invariant whole number from " +
                $"{MinimumMinutes} through {MaximumMinutes}. Unset it to use the " +
                $"{DefaultMinutes}-minute default; disabling the watchdog is not supported.");
        }

        return FromMinutes(minutes, true);
    }

    private static WatchdogLimits FromMinutes(int minutes, bool isOverride)
    {
        var softLimit = TimeSpan.FromMinutes(minutes);
        return new WatchdogLimits(softLimit, softLimit + HardStopGrace, isOverride);
    }
}

internal readonly record struct WatchdogLimits(
    TimeSpan SoftLimit,
    TimeSpan HardStopLimit,
    bool IsOverride);
