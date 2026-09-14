using Robust.Shared.GameObjects;

namespace Content.Server._Solreign.Cargo;

/// <summary>
///     Automated shuttle dispatch state for SR-W-062 Cargo Escrow & Delivery System.
/// </summary>
public enum SolreignCargoShuttleState : byte
{
    Idle = 0,
    Dispatched = 1,
    Arrived = 2,
    Docked = 3,
    Returning = 4,
    Completed = 5
}

/// <summary>
///     SR-W-062: Cargo Escrow & Automated Delivery System Component.
///     Tracks credit escrow deposits, pending payouts, shuttle dispatch state, delivery fees, and speed bonuses.
/// </summary>
// Server-only bookkeeping. This must not be a NetworkedComponent: the client has no matching
// component type, so assigning it a NetId shifts every later component id and corrupts the
// client's Full GameState (most visibly, TradeStation state is read as Transform and the world
// viewport is left on MapId.Nullspace).
[RegisterComponent]
public sealed partial class SolreignCargoEscrowComponent : Component
{
    /// <summary>Total credits currently deposited and held in escrow.</summary>
    [DataField("escrowBalance")]
    public int EscrowBalance = 0;

    /// <summary>Total pending payout allocated from escrow for bounty fulfillment.</summary>
    [DataField("pendingPayout")]
    public int PendingPayout = 0;

    /// <summary>Current state of the automated cargo shuttle.</summary>
    [DataField("shuttleState")]
    public SolreignCargoShuttleState ShuttleState = SolreignCargoShuttleState.Idle;

    /// <summary>Total automated shuttle dispatches executed.</summary>
    [DataField("dispatchCount")]
    public int DispatchCount = 0;

    /// <summary>Percentage fee deducted during automated delivery payout calculation (e.g. 5.0 = 5%).</summary>
    [DataField("deliveryFeePercent")]
    public float DeliveryFeePercent = 5.0f;

    /// <summary>Speed bonus multiplier applied to payout when delivery speed criteria are met (e.g. 1.2 = 20% bonus).</summary>
    [DataField("speedBonusMultiplier")]
    public float SpeedBonusMultiplier = 1.2f;

    /// <summary>Whether automated shuttle delivery is enabled for this escrow component.</summary>
    [DataField("automatedDeliveryEnabled")]
    public bool AutomatedDeliveryEnabled = true;

    /// <summary>Total credit payouts settled through escrow.</summary>
    [DataField("totalPayoutsSettled")]
    public int TotalPayoutsSettled = 0;
}
