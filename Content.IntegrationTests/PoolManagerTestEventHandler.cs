namespace Content.IntegrationTests;

[SetUpFixture]
public sealed class PoolManagerTestEventHandler
{
    [OneTimeSetUp]
    public void Setup()
    {
        // Resolve once and fail before starting the pool if an override is malformed.
        var limits = PoolManagerWatchdogConfiguration.ResolveFromEnvironment();
        var source = limits.IsOverride
            ? PoolManagerWatchdogConfiguration.EnvironmentVariableName
            : "default";
        TestContext.Progress.WriteLine(
            $"{nameof(PoolManagerTestEventHandler)}: soft watchdog={limits.SoftLimit}, " +
            $"hard stop={limits.HardStopLimit}, source={source}");

        PoolManager.Startup();
        // If the tests seem to be stuck, we try to end it semi-nicely
        _ = Task.Delay(limits.SoftLimit).ContinueWith(_ =>
        {
            // This can and probably will cause server/client pairs to shut down MID test, and will lead to really confusing test failures.
            TestContext.Error.WriteLine(
                $"\n\n{nameof(PoolManagerTestEventHandler)}: ERROR: Tests exceeded the " +
                $"{limits.SoftLimit} soft watchdog ({source}). Shutting down all tests. " +
                $"This may lead to weird failures/exceptions.\n\n");
            PoolManager.Shutdown();
        });

        // If ending it nicely doesn't work within a minute, we do something a bit meaner.
        _ = Task.Delay(limits.HardStopLimit).ContinueWith(_ =>
        {
            var deathReport = PoolManager.DeathReport();
            Environment.FailFast(
                $"Tests exceeded the {limits.HardStopLimit} hard watchdog " +
                $"(soft={limits.SoftLimit}, source={source}).\nDeath Report:\n{deathReport}");
        });
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        PoolManager.Shutdown();
    }
}
