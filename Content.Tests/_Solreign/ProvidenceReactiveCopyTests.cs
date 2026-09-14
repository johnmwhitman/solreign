using Content.Server._Solreign.Providence;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(ProvidenceReactiveCopy))]
public sealed class ProvidenceReactiveCopyTests
{
    // --- ClassifyDeath: dispatch picks the right line for an event -----------------------------------

    [Test]
    public void ClassifyDeath_NoMemory_FirstDeathThisShift_IsGeneric()
    {
        Assert.That(ProvidenceReactiveCopy.ClassifyDeath(hasLedgerMemory: false, accountDeathCountThisRound: 1),
            Is.EqualTo(ProvidenceReactiveDeathLineClass.Generic));
    }

    [Test]
    public void ClassifyDeath_NoMemory_BelowRepeatThreshold_IsGeneric()
    {
        Assert.That(ProvidenceReactiveCopy.ClassifyDeath(hasLedgerMemory: false, accountDeathCountThisRound: ProvidenceReactiveCopy.RepeatThreshold - 1),
            Is.EqualTo(ProvidenceReactiveDeathLineClass.Generic));
    }

    [Test]
    public void ClassifyDeath_NoMemory_AtOrAboveRepeatThreshold_IsRepeat()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProvidenceReactiveCopy.ClassifyDeath(hasLedgerMemory: false, accountDeathCountThisRound: ProvidenceReactiveCopy.RepeatThreshold),
                Is.EqualTo(ProvidenceReactiveDeathLineClass.Repeat));
            Assert.That(ProvidenceReactiveCopy.ClassifyDeath(hasLedgerMemory: false, accountDeathCountThisRound: ProvidenceReactiveCopy.RepeatThreshold + 5),
                Is.EqualTo(ProvidenceReactiveDeathLineClass.Repeat));
        });
    }

    [Test]
    public void ClassifyDeath_WithMemory_IsAlwaysMemory_RegardlessOfDeathCount()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProvidenceReactiveCopy.ClassifyDeath(hasLedgerMemory: true, accountDeathCountThisRound: 1),
                Is.EqualTo(ProvidenceReactiveDeathLineClass.Memory));
            Assert.That(ProvidenceReactiveCopy.ClassifyDeath(hasLedgerMemory: true, accountDeathCountThisRound: 99),
                Is.EqualTo(ProvidenceReactiveDeathLineClass.Memory));
        });
    }

    [Test]
    public void ClassifyDeath_MemoryOutranksRepeat_WhenBothWouldApply()
    {
        // A returning player on their 3rd+ death this shift AND with a prior-round record: memory wins
        // — it is the rarer, more meaningful beat (design doc's core thesis).
        var result = ProvidenceReactiveCopy.ClassifyDeath(hasLedgerMemory: true, accountDeathCountThisRound: ProvidenceReactiveCopy.RepeatThreshold + 2);

        Assert.That(result, Is.EqualTo(ProvidenceReactiveDeathLineClass.Memory));
    }

    // --- PriorityFor -----------------------------------------------------------------------------------

    [TestCase(ProvidenceReactiveDeathLineClass.Generic, ProvidenceReactivePriority.Low)]
    [TestCase(ProvidenceReactiveDeathLineClass.Repeat, ProvidenceReactivePriority.Normal)]
    [TestCase(ProvidenceReactiveDeathLineClass.Memory, ProvidenceReactivePriority.High)]
    public void PriorityFor_MapsEachLineClass(ProvidenceReactiveDeathLineClass lineClass, ProvidenceReactivePriority expected)
    {
        Assert.That(ProvidenceReactiveCopy.PriorityFor(lineClass), Is.EqualTo(expected));
    }

    // --- RoundEndKeyFor ----------------------------------------------------------------------------

    [Test]
    public void RoundEndKeyFor_ZeroDeaths_PicksTheZeroKey()
    {
        Assert.That(ProvidenceReactiveCopy.RoundEndKeyFor(0), Is.EqualTo(ProvidenceReactiveCopy.RoundEndZeroKey));
    }

    [Test]
    public void RoundEndKeyFor_AnyDeaths_PicksTheSomeKey()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProvidenceReactiveCopy.RoundEndKeyFor(1), Is.EqualTo(ProvidenceReactiveCopy.RoundEndSomeKey));
            Assert.That(ProvidenceReactiveCopy.RoundEndKeyFor(40), Is.EqualTo(ProvidenceReactiveCopy.RoundEndSomeKey));
        });
    }

    [Test]
    public void RoundEndKeyFor_NegativeDeaths_DefensivelyPicksTheZeroKey()
    {
        // Should never happen (a death toll can't go negative), but a defensive counter bug must
        // degrade to the calm "zero" line rather than the "some" line with a nonsensical count.
        Assert.That(ProvidenceReactiveCopy.RoundEndKeyFor(-1), Is.EqualTo(ProvidenceReactiveCopy.RoundEndZeroKey));
    }

    // --- Copy pool sanity (no dupes with FirstDeath's own pack, keys unique) --------------------------

    [Test]
    public void DeathGenericKeys_AreAllDistinct()
    {
        Assert.That(ProvidenceReactiveCopy.DeathGenericKeys, Is.Unique);
    }

    [Test]
    public void DeathMemoryKeys_AreAllDistinct()
    {
        Assert.That(ProvidenceReactiveCopy.DeathMemoryKeys, Is.Unique);
    }
}
