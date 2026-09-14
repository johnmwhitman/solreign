using Content.Server._Solreign.MiniBoss;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(MiniBossTelegraph))]
public sealed class MiniBossTelegraphTests
{
    [Test]
    public void BeforeWindow_DoesNotFire()
    {
        Assert.That(MiniBossTelegraph.WarningElapsed(elapsedSeconds: 30f, warningSeconds: 60f), Is.False);
    }

    [Test]
    public void AtWindow_Fires()
    {
        Assert.That(MiniBossTelegraph.WarningElapsed(elapsedSeconds: 60f, warningSeconds: 60f), Is.True);
    }

    [Test]
    public void PastWindow_Fires()
    {
        Assert.That(MiniBossTelegraph.WarningElapsed(elapsedSeconds: 61f, warningSeconds: 60f), Is.True);
    }

    [Test]
    public void ZeroElapsed_ZeroWindow_FiresImmediately()
    {
        Assert.That(MiniBossTelegraph.WarningElapsed(elapsedSeconds: 0f, warningSeconds: 0f), Is.True);
    }

    [Test]
    public void NegativeWarningWindow_ClampsToZero_FiresImmediately()
    {
        // A designer misconfiguration (-10) should behave like an instant spawn, not never fire.
        Assert.That(MiniBossTelegraph.WarningElapsed(elapsedSeconds: 0f, warningSeconds: -10f), Is.True);
    }
}
