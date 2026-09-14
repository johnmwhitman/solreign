#nullable enable
using System;
using System.Linq;
using Content.Server._Solreign.Noticeboards;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     SR-W-066 Unit Test Suite for <see cref="SolreignNoticeboardEscrowSystem"/>.
///     Verifies notice pinning, priority sorting, fee calculation, expiration decay, and zero-exception execution.
/// </summary>
[TestFixture]
[TestOf(typeof(SolreignNoticeboardEscrowSystem))]
public sealed class SolreignNoticeboardEscrowTests
{
    private SolreignNoticeboardEscrowSystem _system = null!;

    [SetUp]
    public void SetUp()
    {
        _system = new SolreignNoticeboardEscrowSystem();
        _system.ResetState();
        _system.SetConfigValues(
            enabled: true,
            basePinFee: 50,
            urgentMultiplier: 2.5f,
            broadcastMinEscrow: 200,
            decayRatePerHour: 10.0f
        );
    }

    [Test]
    public void FeeCalculation_StandardAndFlyer_ReturnsBaseFee()
    {
        var standardFee = _system.CalculatePinFee(NoticePriority.Standard, isUrgent: false);
        var flyerFee = _system.CalculatePinFee(NoticePriority.Flyer, isUrgent: false);

        Assert.That(standardFee, Is.EqualTo(50));
        Assert.That(flyerFee, Is.EqualTo(50));
    }

    [Test]
    public void FeeCalculation_Broadcast_IncludesBroadcastEscrow()
    {
        // (50 * 2) + 200 = 300
        var broadcastFee = _system.CalculatePinFee(NoticePriority.Broadcast, isUrgent: false);
        Assert.That(broadcastFee, Is.EqualTo(300));
    }

    [Test]
    public void FeeCalculation_UrgentAnnouncement_AppliesUrgentMultiplier()
    {
        // Flyer: 50 * 2.5 = 125
        var urgentFlyerFee = _system.CalculatePinFee(NoticePriority.Flyer, isUrgent: true);
        Assert.That(urgentFlyerFee, Is.EqualTo(125));

        // EmergencyUrgent: (50 * 3 + 200) * 2.5 = 350 * 2.5 = 875
        var emergencyFee = _system.CalculatePinFee(NoticePriority.EmergencyUrgent, isUrgent: false);
        Assert.That(emergencyFee, Is.EqualTo(875));
    }

    [Test]
    public void FeeCalculation_DurationScaling_ScalesWithHours()
    {
        // Flyer 2 hours: 50 * 2 = 100
        var multiHourFee = _system.CalculatePinFee(NoticePriority.Flyer, isUrgent: false, customDuration: TimeSpan.FromHours(2));
        Assert.That(multiHourFee, Is.EqualTo(100));
    }

    [Test]
    public void NoticePinning_ValidInputs_PinsNoticeSuccessfully()
    {
        var ok = _system.TryPinNotice(
            author: "Officer Miller",
            title: "Security Warning",
            content: "Keep airlocks clear.",
            priority: NoticePriority.Flyer,
            feePaid: 100,
            isUrgent: false,
            duration: TimeSpan.FromHours(1),
            out var notice,
            out var error
        );

        Assert.That(ok, Is.True);
        Assert.That(error, Is.Empty);
        Assert.That(notice, Is.Not.Null);
        Assert.That(notice!.Id, Is.EqualTo(1));
        Assert.That(notice.Author, Is.EqualTo("Officer Miller"));
        Assert.That(notice.Title, Is.EqualTo("Security Warning"));
        Assert.That(notice.EscrowFee, Is.EqualTo(100));
        Assert.That(_system.GetActiveNoticeCount(), Is.EqualTo(1));
    }

    [Test]
    public void NoticePinning_InsufficientFee_FailsWithErrorMessage()
    {
        var ok = _system.TryPinNotice(
            author: "Cargo Tech",
            title: "Bounty Offer",
            content: "Need 10 plasma sheets.",
            priority: NoticePriority.Broadcast,
            feePaid: 50, // Required is 300
            isUrgent: false,
            duration: TimeSpan.FromHours(1),
            out var notice,
            out var error
        );

        Assert.That(ok, Is.False);
        Assert.That(notice, Is.Null);
        Assert.That(error, Does.Contain("Insufficient escrow fee"));
        Assert.That(_system.GetActiveNoticeCount(), Is.EqualTo(0));
    }

    [Test]
    public void NoticePinning_SystemDisabled_Fails()
    {
        _system.SetConfigValues(enabled: false, basePinFee: 50, urgentMultiplier: 2.5f, broadcastMinEscrow: 200, decayRatePerHour: 10.0f);

        var ok = _system.TryPinNotice(
            author: "Captain",
            title: "Evac Alert",
            content: "Shuttle is arriving.",
            priority: NoticePriority.EmergencyUrgent,
            feePaid: 1000,
            isUrgent: true,
            duration: TimeSpan.FromHours(1),
            out var notice,
            out var error
        );

        Assert.That(ok, Is.False);
        Assert.That(notice, Is.Null);
        Assert.That(error, Does.Contain("disabled"));
    }

    [Test]
    public void PrioritySorting_OrdersNoticesByPriorityAndUrgency()
    {
        _system.TryPinNotice("A", "Standard Notice", "Content A", NoticePriority.Standard, 50, false, TimeSpan.FromHours(1), out _, out _);
        _system.TryPinNotice("B", "Broadcast Notice", "Content B", NoticePriority.Broadcast, 300, false, TimeSpan.FromHours(1), out _, out _);
        _system.TryPinNotice("C", "Emergency Notice", "Content C", NoticePriority.EmergencyUrgent, 900, true, TimeSpan.FromHours(1), out _, out _);
        _system.TryPinNotice("D", "Flyer Notice", "Content D", NoticePriority.Flyer, 50, false, TimeSpan.FromHours(1), out _, out _);

        var sorted = _system.GetSortedNotices();

        Assert.That(sorted.Count, Is.EqualTo(4));
        Assert.That(sorted[0].Priority, Is.EqualTo(NoticePriority.EmergencyUrgent));
        Assert.That(sorted[1].Priority, Is.EqualTo(NoticePriority.Broadcast));
        Assert.That(sorted[2].Priority, Is.EqualTo(NoticePriority.Flyer));
        Assert.That(sorted[3].Priority, Is.EqualTo(NoticePriority.Standard));
    }

    [Test]
    public void ExpirationDecay_DecaysHealthOverTimeAndExpiresNotices()
    {
        var fee = _system.CalculatePinFee(NoticePriority.Flyer, false, TimeSpan.FromHours(5));
        var ok = _system.TryPinNotice("Doc", "Medical Warning", "Outbreak in Medbay.", NoticePriority.Flyer, fee, false, TimeSpan.FromHours(5), out var notice, out var err);
        Assert.That(ok, Is.True, err);
        Assert.That(notice!.Health, Is.EqualTo(100.0f));

        // Update 5 hours -> decay rate 10%/hr = 50% health remaining
        _system.UpdateDecay(TimeSpan.FromHours(5));
        Assert.That(notice.Health, Is.EqualTo(50.0f));
        Assert.That(notice.IsActive, Is.True);

        // Update another 5 hours -> 0% health -> notice expires
        _system.UpdateDecay(TimeSpan.FromHours(5));
        Assert.That(notice.Health, Is.EqualTo(0.0f));
        Assert.That(notice.IsActive, Is.False);
        Assert.That(_system.GetActiveNoticeCount(), Is.EqualTo(0));
    }

    [Test]
    public void StationBroadcasts_FiltersOnlyBroadcastsAndEmergencyNotices()
    {
        _system.TryPinNotice("Janitor", "Floor Wax", "Wet floor.", NoticePriority.Standard, 50, false, TimeSpan.FromHours(1), out _, out _);
        _system.TryPinNotice("Hop", "Station Broadcast", "All hands report.", NoticePriority.Broadcast, 300, false, TimeSpan.FromHours(1), out _, out _);

        var broadcasts = _system.GetStationBroadcasts();

        Assert.That(broadcasts.Count, Is.EqualTo(1));
        Assert.That(broadcasts[0].Title, Is.EqualTo("Station Broadcast"));
    }

    [Test]
    public void EscrowBalance_TracksTotalActiveEscrowFees()
    {
        _system.TryPinNotice("User1", "Note 1", "Text 1", NoticePriority.Flyer, 50, false, TimeSpan.FromHours(1), out _, out _);
        _system.TryPinNotice("User2", "Note 2", "Text 2", NoticePriority.Broadcast, 300, false, TimeSpan.FromHours(1), out _, out _);

        Assert.That(_system.GetTotalEscrowBalance(), Is.EqualTo(350));
    }

    [Test]
    public void CancelNotice_ReturnsProratedRefund()
    {
        var fee = _system.CalculatePinFee(NoticePriority.Flyer, false, TimeSpan.FromHours(10));
        var ok = _system.TryPinNotice("Chef", "Soup Special", "Free soup.", NoticePriority.Flyer, fee, false, TimeSpan.FromHours(10), out var notice, out var err);
        Assert.That(ok, Is.True, err);

        // 4 hours decay -> 40% health lost -> 60% health remaining
        _system.UpdateDecay(TimeSpan.FromHours(4));

        var cancelled = _system.CancelNotice(notice!.Id, out var refund);

        Assert.That(cancelled, Is.True);
        Assert.That(refund, Is.EqualTo(fee * 60 / 100));
        Assert.That(notice.IsActive, Is.False);
    }

    [Test]
    public void ZeroExceptionExecution_HandlesEdgeCasesWithoutThrowing()
    {
        Assert.DoesNotThrow(() =>
        {
            _system.TryPinNotice("", "", "", NoticePriority.Standard, -100, false, TimeSpan.Zero, out _, out _);
            _system.TryPinNotice(null!, null!, null!, NoticePriority.Flyer, 0, true, TimeSpan.FromMinutes(-10), out _, out _);
            _system.CalculatePinFee((NoticePriority) 255, true, TimeSpan.FromDays(-5));
            _system.UpdateDecay(TimeSpan.Zero);
            _system.UpdateDecay(TimeSpan.FromDays(-1));
            _system.CancelNotice(99999, out _);
            _system.PurgeExpiredNotices();
            _system.GetSortedNotices();
            _system.GetStationBroadcasts();
            _system.GetTotalEscrowBalance();
        });
    }
}
