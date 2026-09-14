using System;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Server._Solreign.Cargo;

/// <summary>
///     Event raised when an automated cargo shuttle arrives under SR-W-062.
/// </summary>
public sealed class SolreignCargoShuttleArrivedEvent : EntityEventArgs
{
    public EntityUid ShuttleUid { get; }
    public SolreignCargoEscrowComponent Component { get; }
    public int DispatchNumber { get; }

    public SolreignCargoShuttleArrivedEvent(EntityUid shuttleUid, SolreignCargoEscrowComponent component, int dispatchNumber)
    {
        ShuttleUid = shuttleUid;
        Component = component;
        DispatchNumber = dispatchNumber;
    }
}

/// <summary>
///     Event raised when an escrow deposit, withdrawal, or payout transaction occurs under SR-W-062.
/// </summary>
public sealed class SolreignCargoEscrowTransactionEvent : EntityEventArgs
{
    public SolreignCargoEscrowComponent Component { get; }
    public string TransactionType { get; }
    public int Amount { get; }
    public int RemainingEscrow { get; }

    public SolreignCargoEscrowTransactionEvent(SolreignCargoEscrowComponent component, string transactionType, int amount, int remainingEscrow)
    {
        Component = component;
        TransactionType = transactionType;
        Amount = amount;
        RemainingEscrow = remainingEscrow;
    }
}

/// <summary>
///     Pure, unit-testable evaluation and calculation logic for <see cref="SolreignCargoEscrowSystem"/>.
/// </summary>
public static class SolreignCargoEscrowEvaluator
{
    /// <summary>
    ///     Calculates bounty fulfillment payout and fee deduction based on base value, speed bonus multiplier, and delivery fee percentage.
    /// </summary>
    public static int CalculatePayout(
        int baseBountyValue,
        float speedBonusMultiplier,
        float deliveryFeePercent,
        bool applySpeedBonus,
        out int feeDeduction)
    {
        if (baseBountyValue <= 0)
        {
            feeDeduction = 0;
            return 0;
        }

        double gross = baseBountyValue;
        if (applySpeedBonus && speedBonusMultiplier > 0.0f)
        {
            gross *= speedBonusMultiplier;
        }

        float feePercentClamp = Math.Clamp(deliveryFeePercent, 0.0f, 100.0f);
        feeDeduction = (int)Math.Floor(gross * (feePercentClamp / 100.0f));

        int netPayout = (int)Math.Floor(gross) - feeDeduction;
        return Math.Max(0, netPayout);
    }

    /// <summary>
    ///     Evaluates whether automated shuttle dispatch is permitted given system posture and current shuttle state.
    /// </summary>
    public static bool CanDispatch(bool cvarEnabled, bool componentAutomatedEnabled, SolreignCargoShuttleState currentState)
    {
        if (!cvarEnabled || !componentAutomatedEnabled)
            return false;

        return currentState == SolreignCargoShuttleState.Idle || currentState == SolreignCargoShuttleState.Completed;
    }

    /// <summary>
    ///     Evaluates whether an escrow payout can be processed given current escrow balance and required funds.
    /// </summary>
    public static bool CanProcessPayout(bool cvarEnabled, int escrowBalance, int requiredEscrow)
    {
        if (!cvarEnabled || requiredEscrow <= 0)
            return false;

        return escrowBalance >= requiredEscrow;
    }
}

/// <summary>
///     SR-W-062: SS14 Solreign Cargo Escrow & Automated Delivery System.
///     Manages automated cargo shuttle dispatch, credit escrow deposits, bounty fulfillment payout calculations,
///     and CVar thresholds (<c>solreign.cargo_escrow_enabled</c>).
/// </summary>
public sealed partial class SolreignCargoEscrowSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;

    private bool _cvarEnabled = true;

    /// <summary>
    ///     Whether cargo escrow and automated delivery is enabled via CVar (<c>solreign.cargo_escrow_enabled</c>).
    /// </summary>
    public bool IsEscrowEnabled => _cfg != null ? _cfg.GetCVar(CCVars.SolreignCargoEscrowEnabled) : _cvarEnabled;

    public override void Initialize()
    {
        base.Initialize();

        if (_cfg != null)
        {
            _cfg.OnValueChanged(CCVars.SolreignCargoEscrowEnabled, SetCargoEscrowEnabled, true);
        }
    }

    /// <summary>
    ///     Updates the CVar override state (used for testing or explicit configuration changes).
    /// </summary>
    public void SetCargoEscrowEnabled(bool enabled)
    {
        _cvarEnabled = enabled;
    }

    /// <summary>
    ///     Deposits credits into escrow balance.
    /// </summary>
    public bool DepositEscrow(SolreignCargoEscrowComponent comp, int amount)
    {
        if (!IsEscrowEnabled || amount <= 0)
            return false;

        comp.EscrowBalance += amount;

        if (EntityManager != null)
        {
            RaiseLocalEvent(new SolreignCargoEscrowTransactionEvent(comp, "Deposit", amount, comp.EscrowBalance));
        }

        return true;
    }

    /// <summary>
    ///     Withdraws credits from escrow balance if sufficient funds exist.
    /// </summary>
    public bool WithdrawEscrow(SolreignCargoEscrowComponent comp, int amount)
    {
        if (!IsEscrowEnabled || amount <= 0 || comp.EscrowBalance < amount)
            return false;

        comp.EscrowBalance -= amount;

        if (EntityManager != null)
        {
            RaiseLocalEvent(new SolreignCargoEscrowTransactionEvent(comp, "Withdraw", amount, comp.EscrowBalance));
        }

        return true;
    }

    /// <summary>
    ///     Calculates bounty payout and fee deduction for a given base bounty value.
    /// </summary>
    public int CalculateBountyPayout(SolreignCargoEscrowComponent comp, int baseBountyValue, bool applySpeedBonus, out int feeDeduction)
    {
        if (!IsEscrowEnabled)
        {
            feeDeduction = 0;
            return 0;
        }

        return SolreignCargoEscrowEvaluator.CalculatePayout(
            baseBountyValue,
            comp.SpeedBonusMultiplier,
            comp.DeliveryFeePercent,
            applySpeedBonus,
            out feeDeduction);
    }

    /// <summary>
    ///     Processes a bounty fulfillment payout, deducting total required funds from escrow balance and recording pending payout.
    /// </summary>
    public bool ProcessBountyPayout(SolreignCargoEscrowComponent comp, int baseBountyValue, bool applySpeedBonus, out int netPayout, out int feeDeduction)
    {
        netPayout = 0;
        feeDeduction = 0;

        if (!IsEscrowEnabled)
            return false;

        int calculatedNet = CalculateBountyPayout(comp, baseBountyValue, applySpeedBonus, out int calculatedFee);
        int totalRequiredEscrow = calculatedNet + calculatedFee;

        if (!SolreignCargoEscrowEvaluator.CanProcessPayout(IsEscrowEnabled, comp.EscrowBalance, totalRequiredEscrow))
            return false;

        netPayout = calculatedNet;
        feeDeduction = calculatedFee;

        comp.EscrowBalance -= totalRequiredEscrow;
        comp.PendingPayout += netPayout;
        comp.TotalPayoutsSettled += netPayout;

        if (EntityManager != null)
        {
            RaiseLocalEvent(new SolreignCargoEscrowTransactionEvent(comp, "Payout", netPayout, comp.EscrowBalance));
        }

        return true;
    }

    /// <summary>
    ///     Dispatches an automated cargo shuttle if posture and state permit.
    /// </summary>
    public bool DispatchShuttle(SolreignCargoEscrowComponent comp)
    {
        if (!SolreignCargoEscrowEvaluator.CanDispatch(IsEscrowEnabled, comp.AutomatedDeliveryEnabled, comp.ShuttleState))
            return false;

        comp.ShuttleState = SolreignCargoShuttleState.Dispatched;
        comp.DispatchCount++;
        return true;
    }

    /// <summary>
    ///     Handles arrival of an automated cargo shuttle and raises arrival event.
    /// </summary>
    public bool OnShuttleArrival(EntityUid shuttleUid, SolreignCargoEscrowComponent comp)
    {
        if (!IsEscrowEnabled)
            return false;

        if (comp.ShuttleState != SolreignCargoShuttleState.Dispatched)
            return false;

        comp.ShuttleState = SolreignCargoShuttleState.Arrived;

        if (EntityManager != null)
        {
            RaiseLocalEvent(new SolreignCargoShuttleArrivedEvent(shuttleUid, comp, comp.DispatchCount));
        }

        return true;
    }

    /// <summary>
    ///     Completes delivery cycle, updating shuttle state to Completed and clearing pending payout.
    /// </summary>
    public bool CompleteDelivery(SolreignCargoEscrowComponent comp)
    {
        if (!IsEscrowEnabled)
            return false;

        comp.ShuttleState = SolreignCargoShuttleState.Completed;
        comp.PendingPayout = 0;
        return true;
    }

    /// <summary>
    ///     Resets component escrow balance, payouts, and shuttle state.
    /// </summary>
    public void ResetEscrowState(SolreignCargoEscrowComponent comp)
    {
        comp.EscrowBalance = 0;
        comp.PendingPayout = 0;
        comp.ShuttleState = SolreignCargoShuttleState.Idle;
        comp.DispatchCount = 0;
        comp.TotalPayoutsSettled = 0;
    }
}
