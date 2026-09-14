using Robust.Shared.Prototypes;

namespace Content.Server._Solreign.MiniBoss;

/// <summary>
///     Marks a spawned mob as a Solreign mini-boss for <see cref="SolreignMiniBossSystem"/>: on death it
///     drops <see cref="RewardPrototype"/> at the corpse and fires a defeat announcement. Attached
///     directly on the reskinned mob prototypes in
///     Resources/Prototypes/_Solreign/GameRules/minibosses.yml (The Auditor Prime, Specimen Zero) —
///     server-only bookkeeping, not networked.
/// </summary>
[RegisterComponent, Access(typeof(SolreignMiniBossSystem))]
public sealed partial class SolreignMiniBossComponent : Component
{
    /// <summary>Unique reward item spawned at the corpse's coordinates on death. Null drops nothing.</summary>
    [DataField]
    public EntProtoId? RewardPrototype;

    /// <summary>Locale id for the station-wide defeat announcement. Null skips the announcement.</summary>
    [DataField]
    public LocId? DefeatAnnouncement;

    /// <summary>
    ///     Ledger trace (integration comment only — no <c>SeasonLedgerSystem</c> file is touched by this
    ///     wave, same idiom as <c>SolreignLedgerDiscrepancyRule</c> and <c>SolreignHotPotatoSystem</c>):
    ///     human-readable label for this mini-boss (e.g. "The Auditor Prime") that a future wave can hand
    ///     to <c>SeasonLedgerSystem</c> alongside the killer's account when it wires a "mini-boss
    ///     trophies" ledger category. Logged today via <see cref="Content.Server.Administration.Logs.IAdminLogManager"/>
    ///     so the kill is at least admin-auditable in the interim.
    /// </summary>
    [DataField]
    public string LedgerLabel = string.Empty;

    /// <summary>
    ///     Guards against a double drop/announcement if <c>MobStateChangedEvent</c> re-fires Dead (e.g.
    ///     a damage tick landing again post-mortem). Set true the first time <see cref="SolreignMiniBossSystem"/>
    ///     processes this entity's death. Not networked — server bookkeeping only.
    /// </summary>
    [ViewVariables]
    public bool DefeatHandled;
}
