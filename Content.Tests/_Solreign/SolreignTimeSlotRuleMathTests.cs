using System;
using Content.Shared._Solreign.Audio;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(SolreignTimeSlotRuleMath))]
public sealed class SolreignTimeSlotRuleMathTests
{
    private static readonly TimeSpan SlotDuration = TimeSpan.FromSeconds(45);

    [Test]
    public void CurrentSlot_AtZero_IsSlotZero()
    {
        Assert.That(SolreignTimeSlotRuleMath.CurrentSlot(TimeSpan.Zero, 3, SlotDuration), Is.EqualTo(0));
    }

    [Test]
    public void CurrentSlot_JustBeforeFirstBoundary_StillSlotZero()
    {
        var t = SlotDuration - TimeSpan.FromMilliseconds(1);
        Assert.That(SolreignTimeSlotRuleMath.CurrentSlot(t, 3, SlotDuration), Is.EqualTo(0));
    }

    [Test]
    public void CurrentSlot_ExactlyAtFirstBoundary_AdvancesToSlotOne()
    {
        Assert.That(SolreignTimeSlotRuleMath.CurrentSlot(SlotDuration, 3, SlotDuration), Is.EqualTo(1));
    }

    [Test]
    public void CurrentSlot_ExactlyAtSecondBoundary_AdvancesToSlotTwo()
    {
        var t = SlotDuration * 2;
        Assert.That(SolreignTimeSlotRuleMath.CurrentSlot(t, 3, SlotDuration), Is.EqualTo(2));
    }

    [Test]
    public void CurrentSlot_AfterFullCycle_WrapsBackToSlotZero()
    {
        var t = SlotDuration * 3;
        Assert.That(SolreignTimeSlotRuleMath.CurrentSlot(t, 3, SlotDuration), Is.EqualTo(0));
    }

    [Test]
    public void CurrentSlot_ManyCyclesLater_StillWrapsCorrectly()
    {
        // 7 full cycles (21 slots) plus 1 slot and a bit -> slot 1.
        var t = (SlotDuration * 21) + SlotDuration + TimeSpan.FromSeconds(5);
        Assert.That(SolreignTimeSlotRuleMath.CurrentSlot(t, 3, SlotDuration), Is.EqualTo(1));
    }

    [Test]
    public void CurrentSlot_SlotCountOne_AlwaysSlotZero()
    {
        Assert.That(SolreignTimeSlotRuleMath.CurrentSlot(SlotDuration * 5, 1, SlotDuration), Is.EqualTo(0));
    }

    [Test]
    public void CurrentSlot_SlotCountZeroOrNegative_ClampsToOneSlot()
    {
        Assert.That(SolreignTimeSlotRuleMath.CurrentSlot(SlotDuration * 5, 0, SlotDuration), Is.EqualTo(0));
        Assert.That(SolreignTimeSlotRuleMath.CurrentSlot(SlotDuration * 5, -2, SlotDuration), Is.EqualTo(0));
    }

    [Test]
    public void CurrentSlot_ZeroSlotDuration_NeverDividesByZero_AlwaysSlotZero()
    {
        Assert.That(SolreignTimeSlotRuleMath.CurrentSlot(TimeSpan.FromSeconds(500), 3, TimeSpan.Zero), Is.EqualTo(0));
    }

    [Test]
    public void CurrentSlot_NegativeSlotDuration_TreatedAsZero_AlwaysSlotZero()
    {
        Assert.That(
            SolreignTimeSlotRuleMath.CurrentSlot(TimeSpan.FromSeconds(500), 3, TimeSpan.FromSeconds(-10)),
            Is.EqualTo(0));
    }

    [Test]
    public void CurrentSlot_NegativeCurTime_ClampsToZero_SameAsZero()
    {
        Assert.That(
            SolreignTimeSlotRuleMath.CurrentSlot(TimeSpan.FromSeconds(-5), 3, SlotDuration),
            Is.EqualTo(SolreignTimeSlotRuleMath.CurrentSlot(TimeSpan.Zero, 3, SlotDuration)));
    }

    [Test]
    public void CurrentSlot_ThreeSlots_ExactlyOneSlotEverMatchesAtAnyGivenTime()
    {
        // Sanity check on the actual solreign_ambient.yml wiring shape: for slots 0, 1, 2 sharing the
        // same SlotCount/SlotDuration, exactly one of the three ever reports "current" at once —
        // this is what lets GetAmbience() see a genuinely different top match over time without any
        // tie between them.
        for (var seconds = 0; seconds < 45 * 9; seconds += 3)
        {
            var t = TimeSpan.FromSeconds(seconds);
            var matches = 0;
            for (var slot = 0; slot < 3; slot++)
            {
                if (SolreignTimeSlotRuleMath.CurrentSlot(t, 3, SlotDuration) == slot)
                    matches++;
            }

            Assert.That(matches, Is.EqualTo(1), $"exactly one slot should match at t={seconds}s");
        }
    }
}
