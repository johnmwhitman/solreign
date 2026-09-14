using NUnit.Framework;

namespace Content.IntegrationTests.Tests._Solreign;

[TestFixture]
[TestOf(typeof(PoolManagerWatchdogConfiguration))]
public sealed class SolreignIntegrationWatchdogConfigurationTest
{
    [Test]
    public void UnsetValuePreservesDefaultLimits()
    {
        var limits = PoolManagerWatchdogConfiguration.Resolve(null);

        Assert.Multiple(() =>
        {
            Assert.That(limits.SoftLimit, Is.EqualTo(TimeSpan.FromMinutes(20)));
            Assert.That(limits.HardStopLimit, Is.EqualTo(TimeSpan.FromMinutes(21)));
            Assert.That(limits.IsOverride, Is.False);
        });
    }

    [TestCase("20", 20)]
    [TestCase("25", 25)]
    [TestCase("30", 30)]
    public void BoundedWholeMinuteOverrideResolvesOnce(string raw, int expectedMinutes)
    {
        var limits = PoolManagerWatchdogConfiguration.Resolve(raw);

        Assert.Multiple(() =>
        {
            Assert.That(limits.SoftLimit, Is.EqualTo(TimeSpan.FromMinutes(expectedMinutes)));
            Assert.That(limits.HardStopLimit, Is.EqualTo(TimeSpan.FromMinutes(expectedMinutes + 1)));
            Assert.That(limits.IsOverride, Is.True);
        });
    }

    [TestCase("")]
    [TestCase(" ")]
    [TestCase("0")]
    [TestCase("19")]
    [TestCase("31")]
    [TestCase("-1")]
    [TestCase("+20")]
    [TestCase("20.5")]
    [TestCase("1e2")]
    [TestCase("infinite")]
    [TestCase("999999999999999999999999")]
    public void InvalidOverrideFailsLoudly(string raw)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => PoolManagerWatchdogConfiguration.Resolve(raw));

        Assert.That(
            exception!.Message,
            Does.Contain(PoolManagerWatchdogConfiguration.EnvironmentVariableName));
    }
}
