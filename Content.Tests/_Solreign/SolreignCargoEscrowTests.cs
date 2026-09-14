using Content.Server._Solreign.Cargo;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(SolreignCargoEscrowSystem))]
public sealed class SolreignCargoEscrowTests
{
    [Test]
    public void CargoEscrow_DefaultComponentState_InitializedCorrectly()
    {
        var comp = new SolreignCargoEscrowComponent();

        Assert.Multiple(() =>
        {
            Assert.That(comp.EscrowBalance, Is.EqualTo(0));
            Assert.That(comp.PendingPayout, Is.EqualTo(0));
            Assert.That(comp.ShuttleState, Is.EqualTo(SolreignCargoShuttleState.Idle));
            Assert.That(comp.DispatchCount, Is.EqualTo(0));
            Assert.That(comp.DeliveryFeePercent, Is.EqualTo(5.0f));
            Assert.That(comp.SpeedBonusMultiplier, Is.EqualTo(1.2f));
            Assert.That(comp.AutomatedDeliveryEnabled, Is.True);
            Assert.That(comp.TotalPayoutsSettled, Is.EqualTo(0));
        });
    }

    [Test]
    public void DepositEscrow_ValidAmount_IncreasesEscrowBalance()
    {
        var sys = new SolreignCargoEscrowSystem();
        var comp = new SolreignCargoEscrowComponent();

        bool result = sys.DepositEscrow(comp, 1000);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(comp.EscrowBalance, Is.EqualTo(1000));
        });

        sys.DepositEscrow(comp, 500);
        Assert.That(comp.EscrowBalance, Is.EqualTo(1500));
    }

    [Test]
    public void DepositEscrow_ZeroOrNegativeAmount_ReturnsFalse()
    {
        var sys = new SolreignCargoEscrowSystem();
        var comp = new SolreignCargoEscrowComponent();

        Assert.Multiple(() =>
        {
            Assert.That(sys.DepositEscrow(comp, 0), Is.False);
            Assert.That(sys.DepositEscrow(comp, -100), Is.False);
            Assert.That(comp.EscrowBalance, Is.EqualTo(0));
        });
    }

    [Test]
    public void WithdrawEscrow_ValidAmount_DeductsFromBalance()
    {
        var sys = new SolreignCargoEscrowSystem();
        var comp = new SolreignCargoEscrowComponent { EscrowBalance = 1000 };

        bool result = sys.WithdrawEscrow(comp, 400);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(comp.EscrowBalance, Is.EqualTo(600));
        });
    }

    [Test]
    public void WithdrawEscrow_InsufficientBalanceOrInvalidAmount_Fails()
    {
        var sys = new SolreignCargoEscrowSystem();
        var comp = new SolreignCargoEscrowComponent { EscrowBalance = 300 };

        Assert.Multiple(() =>
        {
            Assert.That(sys.WithdrawEscrow(comp, 500), Is.False);
            Assert.That(sys.WithdrawEscrow(comp, 0), Is.False);
            Assert.That(sys.WithdrawEscrow(comp, -50), Is.False);
            Assert.That(comp.EscrowBalance, Is.EqualTo(300));
        });
    }

    [Test]
    public void CalculateBountyPayout_StandardAndSpeedBonus_ComputesCorrectNetAndFee()
    {
        var sys = new SolreignCargoEscrowSystem();
        var comp = new SolreignCargoEscrowComponent
        {
            DeliveryFeePercent = 5.0f,      // 5% fee
            SpeedBonusMultiplier = 1.2f,     // 20% bonus multiplier
        };

        // Case 1: Standard payout without speed bonus (1000 base, 5% fee = 50 fee, 950 net)
        int netNoBonus = sys.CalculateBountyPayout(comp, 1000, applySpeedBonus: false, out int feeNoBonus);
        Assert.Multiple(() =>
        {
            Assert.That(feeNoBonus, Is.EqualTo(50));
            Assert.That(netNoBonus, Is.EqualTo(950));
        });

        // Case 2: Speed bonus applied (1000 * 1.2 = 1200 gross, 5% fee = 60 fee, 1140 net)
        int netWithBonus = sys.CalculateBountyPayout(comp, 1000, applySpeedBonus: true, out int feeWithBonus);
        Assert.Multiple(() =>
        {
            Assert.That(feeWithBonus, Is.EqualTo(60));
            Assert.That(netWithBonus, Is.EqualTo(1140));
        });
    }

    [Test]
    public void ProcessBountyPayout_SufficientEscrow_DeductsAndRecordsPayout()
    {
        var sys = new SolreignCargoEscrowSystem();
        var comp = new SolreignCargoEscrowComponent
        {
            EscrowBalance = 2000,
            DeliveryFeePercent = 5.0f,
            SpeedBonusMultiplier = 1.0f
        };

        bool success = sys.ProcessBountyPayout(comp, 1000, applySpeedBonus: false, out int netPayout, out int feeDeduction);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(feeDeduction, Is.EqualTo(50));
            Assert.That(netPayout, Is.EqualTo(950));
            Assert.That(comp.EscrowBalance, Is.EqualTo(1000)); // 2000 - (950 + 50)
            Assert.That(comp.PendingPayout, Is.EqualTo(950));
            Assert.That(comp.TotalPayoutsSettled, Is.EqualTo(950));
        });
    }

    [Test]
    public void ProcessBountyPayout_InsufficientEscrow_Fails()
    {
        var sys = new SolreignCargoEscrowSystem();
        var comp = new SolreignCargoEscrowComponent
        {
            EscrowBalance = 400,
            DeliveryFeePercent = 5.0f,
            SpeedBonusMultiplier = 1.0f
        };

        bool success = sys.ProcessBountyPayout(comp, 1000, applySpeedBonus: false, out int netPayout, out int feeDeduction);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.False);
            Assert.That(netPayout, Is.EqualTo(0));
            Assert.That(feeDeduction, Is.EqualTo(0));
            Assert.That(comp.EscrowBalance, Is.EqualTo(400));
            Assert.That(comp.PendingPayout, Is.EqualTo(0));
        });
    }

    [Test]
    public void DispatchShuttle_TransitionsStateAndIncrementsCount()
    {
        var sys = new SolreignCargoEscrowSystem();
        var comp = new SolreignCargoEscrowComponent();

        bool result = sys.DispatchShuttle(comp);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(comp.ShuttleState, Is.EqualTo(SolreignCargoShuttleState.Dispatched));
            Assert.That(comp.DispatchCount, Is.EqualTo(1));
        });

        // Second dispatch while already Dispatched should fail
        Assert.That(sys.DispatchShuttle(comp), Is.False);
    }

    [Test]
    public void ShuttleArrivalAndDeliveryCompletion_LifecycleTransitions()
    {
        var sys = new SolreignCargoEscrowSystem();
        var comp = new SolreignCargoEscrowComponent();

        // Dispatch shuttle
        sys.DispatchShuttle(comp);
        Assert.That(comp.ShuttleState, Is.EqualTo(SolreignCargoShuttleState.Dispatched));

        // Shuttle arrives
        bool arrivalResult = sys.OnShuttleArrival(EntityUid.Invalid, comp);
        Assert.Multiple(() =>
        {
            Assert.That(arrivalResult, Is.True);
            Assert.That(comp.ShuttleState, Is.EqualTo(SolreignCargoShuttleState.Arrived));
        });

        // Complete delivery
        bool completionResult = sys.CompleteDelivery(comp);
        Assert.Multiple(() =>
        {
            Assert.That(completionResult, Is.True);
            Assert.That(comp.ShuttleState, Is.EqualTo(SolreignCargoShuttleState.Completed));
        });

        // Can dispatch again after completion
        Assert.That(sys.DispatchShuttle(comp), Is.True);
        Assert.That(comp.DispatchCount, Is.EqualTo(2));
    }

    [Test]
    public void CVarThreshold_DisabledPosture_BlocksOperations()
    {
        var sys = new SolreignCargoEscrowSystem();
        var comp = new SolreignCargoEscrowComponent { EscrowBalance = 1000 };

        // Disable CVar posture
        sys.SetCargoEscrowEnabled(false);

        Assert.Multiple(() =>
        {
            Assert.That(sys.IsEscrowEnabled, Is.False);
            Assert.That(sys.DepositEscrow(comp, 500), Is.False);
            Assert.That(sys.WithdrawEscrow(comp, 100), Is.False);
            Assert.That(sys.DispatchShuttle(comp), Is.False);
            Assert.That(sys.ProcessBountyPayout(comp, 500, false, out _, out _), Is.False);
            Assert.That(sys.CompleteDelivery(comp), Is.False);
        });

        // Re-enable CVar posture
        sys.SetCargoEscrowEnabled(true);
        Assert.Multiple(() =>
        {
            Assert.That(sys.IsEscrowEnabled, Is.True);
            Assert.That(sys.DepositEscrow(comp, 500), Is.True);
            Assert.That(comp.EscrowBalance, Is.EqualTo(1500));
        });
    }

    [Test]
    public void Evaluator_CalculatePayout_EdgeCasesAndZeroExceptions()
    {
        int fee;

        Assert.Multiple(() =>
        {
            // Zero base bounty
            Assert.That(SolreignCargoEscrowEvaluator.CalculatePayout(0, 1.2f, 5.0f, true, out fee), Is.EqualTo(0));
            Assert.That(fee, Is.EqualTo(0));

            // Negative base bounty
            Assert.That(SolreignCargoEscrowEvaluator.CalculatePayout(-500, 1.2f, 5.0f, true, out fee), Is.EqualTo(0));
            Assert.That(fee, Is.EqualTo(0));

            // 0% delivery fee
            Assert.That(SolreignCargoEscrowEvaluator.CalculatePayout(1000, 1.0f, 0.0f, false, out fee), Is.EqualTo(1000));
            Assert.That(fee, Is.EqualTo(0));

            // 100% delivery fee
            Assert.That(SolreignCargoEscrowEvaluator.CalculatePayout(1000, 1.0f, 100.0f, false, out fee), Is.EqualTo(0));
            Assert.That(fee, Is.EqualTo(1000));
        });
    }

    [Test]
    public void ResetEscrowState_ResetsAllFields()
    {
        var sys = new SolreignCargoEscrowSystem();
        var comp = new SolreignCargoEscrowComponent
        {
            EscrowBalance = 5000,
            PendingPayout = 2000,
            ShuttleState = SolreignCargoShuttleState.Dispatched,
            DispatchCount = 10,
            TotalPayoutsSettled = 4000
        };

        sys.ResetEscrowState(comp);

        Assert.Multiple(() =>
        {
            Assert.That(comp.EscrowBalance, Is.EqualTo(0));
            Assert.That(comp.PendingPayout, Is.EqualTo(0));
            Assert.That(comp.ShuttleState, Is.EqualTo(SolreignCargoShuttleState.Idle));
            Assert.That(comp.DispatchCount, Is.EqualTo(0));
            Assert.That(comp.TotalPayoutsSettled, Is.EqualTo(0));
        });
    }
}
