using System;
using Content.Server._Solreign.EasterEggs;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(SolreignWristOrganizerRules))]
public sealed class SolreignWristOrganizerRulesTests
{
    private static readonly TimeSpan T0 = TimeSpan.FromSeconds(500);

    // --- Cooldown math (same shape as CarpComboRules/JudoComboRules) ---

    [Test]
    public void ReadoutReady_ExactlyAtNextReadoutTime_IsReady()
    {
        Assert.That(SolreignWristOrganizerRules.ReadoutReady(T0, T0), Is.True);
    }

    [Test]
    public void ReadoutReady_BeforeNextReadoutTime_IsNotReady()
    {
        Assert.That(SolreignWristOrganizerRules.ReadoutReady(T0, T0 + TimeSpan.FromMilliseconds(1)), Is.False);
    }

    [Test]
    public void FreshComponent_DefaultNextReadoutTime_IsImmediatelyReady()
    {
        Assert.That(SolreignWristOrganizerRules.ReadoutReady(TimeSpan.Zero, default), Is.True);
    }

    [Test]
    public void NextReadoutTime_AddsCooldown()
    {
        Assert.That(
            SolreignWristOrganizerRules.NextReadoutTime(T0, TimeSpan.FromSeconds(3)),
            Is.EqualTo(T0 + TimeSpan.FromSeconds(3)));
    }

    [Test]
    public void NextReadoutTime_NegativeCooldown_ClampsToZero()
    {
        Assert.That(SolreignWristOrganizerRules.NextReadoutTime(T0, TimeSpan.FromSeconds(-10)), Is.EqualTo(T0));
    }

    // --- VitalsStatus ---

    [Test]
    public void VitalsStatus_NoDamage_IsNominal()
    {
        Assert.That(SolreignWristOrganizerRules.VitalsStatus(0f, 100f, 50f), Is.EqualTo("NOMINAL"));
    }

    [Test]
    public void VitalsStatus_SomeDamageBelowCrit_IsDegraded()
    {
        Assert.That(SolreignWristOrganizerRules.VitalsStatus(10f, 100f, 50f), Is.EqualTo("DEGRADED"));
    }

    [Test]
    public void VitalsStatus_ExactlyAtCritThreshold_IsCritical()
    {
        Assert.That(SolreignWristOrganizerRules.VitalsStatus(50f, 100f, 50f), Is.EqualTo("CRITICAL"));
    }

    [Test]
    public void VitalsStatus_JustBelowCritThreshold_IsDegradedNotCritical()
    {
        Assert.That(SolreignWristOrganizerRules.VitalsStatus(49.99f, 100f, 50f), Is.EqualTo("DEGRADED"));
    }

    [Test]
    public void VitalsStatus_ExactlyAtDeadThreshold_IsFlatline()
    {
        Assert.That(SolreignWristOrganizerRules.VitalsStatus(100f, 100f, 50f), Is.EqualTo("FLATLINE"));
    }

    [Test]
    public void VitalsStatus_AboveDeadThreshold_IsFlatline()
    {
        Assert.That(SolreignWristOrganizerRules.VitalsStatus(500f, 100f, 50f), Is.EqualTo("FLATLINE"));
    }

    [Test]
    public void VitalsStatus_NoThresholdsAvailable_FallsBackToDegradedOrNominal()
    {
        Assert.That(SolreignWristOrganizerRules.VitalsStatus(0f, null, null), Is.EqualTo("NOMINAL"));
        Assert.That(SolreignWristOrganizerRules.VitalsStatus(5f, null, null), Is.EqualTo("DEGRADED"));
    }

    [Test]
    public void VitalsStatus_ZeroOrNegativeThreshold_TreatedAsUnknown_NeverInstantlyTrips()
    {
        // A non-positive threshold is a YAML/config edge case, not "already dead at zero damage".
        Assert.That(SolreignWristOrganizerRules.VitalsStatus(0f, 0f, 0f), Is.EqualTo("NOMINAL"));
        Assert.That(SolreignWristOrganizerRules.VitalsStatus(1f, 0f, 0f), Is.EqualTo("DEGRADED"));
    }

    // --- DoomPercent ---

    [Test]
    public void DoomPercent_NullPercentage_IsZero()
    {
        Assert.That(SolreignWristOrganizerRules.DoomPercent(null), Is.EqualTo(0));
    }

    [Test]
    public void DoomPercent_Zero_IsZero()
    {
        Assert.That(SolreignWristOrganizerRules.DoomPercent(0f), Is.EqualTo(0));
    }

    [Test]
    public void DoomPercent_Half_IsFifty()
    {
        Assert.That(SolreignWristOrganizerRules.DoomPercent(0.5f), Is.EqualTo(50));
    }

    [Test]
    public void DoomPercent_One_IsOneHundred()
    {
        Assert.That(SolreignWristOrganizerRules.DoomPercent(1f), Is.EqualTo(100));
    }

    [Test]
    public void DoomPercent_AboveOne_ClampsToOneHundred()
    {
        Assert.That(SolreignWristOrganizerRules.DoomPercent(1.5f), Is.EqualTo(100));
    }

    [Test]
    public void DoomPercent_Negative_ClampsToZero()
    {
        Assert.That(SolreignWristOrganizerRules.DoomPercent(-0.2f), Is.EqualTo(0));
    }

    [Test]
    public void DoomPercent_RoundsToNearestWholeNumber()
    {
        Assert.That(SolreignWristOrganizerRules.DoomPercent(0.336f), Is.EqualTo(34));
    }

    // --- DriftTierForPriorUses (delight-eggs batch) ---

    [Test]
    public void DriftTierForPriorUses_FirstUse_NoDrift()
    {
        Assert.That(SolreignWristOrganizerRules.DriftTierForPriorUses(0), Is.EqualTo(-1));
    }

    [Test]
    public void DriftTierForPriorUses_NegativePriorUses_NoDrift()
    {
        Assert.That(SolreignWristOrganizerRules.DriftTierForPriorUses(-1), Is.EqualTo(-1));
    }

    [Test]
    public void DriftTierForPriorUses_SecondUse_IsTierZero()
    {
        Assert.That(SolreignWristOrganizerRules.DriftTierForPriorUses(1), Is.EqualTo(0));
    }

    [Test]
    public void DriftTierForPriorUses_ThirdUse_IsTierOne()
    {
        Assert.That(SolreignWristOrganizerRules.DriftTierForPriorUses(2), Is.EqualTo(1));
    }

    [Test]
    public void DriftTierForPriorUses_ManyLaterUses_StaysAtTierOne()
    {
        Assert.That(SolreignWristOrganizerRules.DriftTierForPriorUses(50), Is.EqualTo(1));
    }
}
