using Content.Shared.Polymorph;
using Robust.Shared.Prototypes;

namespace Content.Server._Solreign.Antags.Werewolf;

/// <summary>
///     Marks an employee as Lunar-Reactive: the subject of an Unscheduled Fur Event when the Company
///     declares a Full Moon Window. The transformation decision layer is the pure
///     <see cref="WerewolfStateMachine"/>; this component is its per-entity memory plus YAML-tunable
///     durations. Design contract: docs/specs/2026-07-11-werewolf-vampire-spec.md §3.
///
///     Server-only: state changes surface to clients through polymorph/appearance/popups at build
///     time, so nothing here needs networking.
/// </summary>
[RegisterComponent, Access(typeof(SolreignWerewolfSystem))]
public sealed partial class SolreignWerewolfComponent : Component
{
    /// <summary>Current phase of the fur-event cycle.</summary>
    [ViewVariables]
    public WerewolfState State = WerewolfState.Dormant;

    /// <summary>Game time the current phase began (compared against IGameTiming.CurTime; never wall clock).</summary>
    [ViewVariables]
    public TimeSpan StateEnteredAt;

    /// <summary>
    ///     Set once a Follicle Stabilizer Draught (or the chapel's Sunrise Clause) has been administered.
    ///     The state machine resolves it to <see cref="WerewolfState.Cured"/> at the next safe boundary.
    /// </summary>
    [ViewVariables]
    public bool CureApplied;

    /// <summary>Warning-phase length: fur specks and fair notice before the fur event proper.</summary>
    [DataField]
    public float StirringSeconds = 30f;

    /// <summary>Per-window cap on wolf-form time. The moon window can still end the phase earlier.</summary>
    [DataField]
    public float TransformedSeconds = 180f;

    /// <summary>Visible, vulnerable revert window.</summary>
    [DataField]
    public float WaningSeconds = 10f;

    /// <summary>Knockdown applied by a maul. The maul deals no damage — this is the whole payload.</summary>
    [DataField]
    public float MaulKnockdownSeconds = 4f;

    /// <summary>Minimum time between mauls (anti-grief rule 3: no corridor bowling).</summary>
    [DataField]
    public float MaulCooldownSeconds = 5f;

    /// <summary>Game time before which the next maul is refused. Scheduling state, not config.</summary>
    [ViewVariables]
    public TimeSpan NextMaulAllowed;

    /// <summary>How long a mauled colleague stays Moon-Touched (and thereby maul-immune).</summary>
    [DataField]
    public float MoonTouchedSeconds = 180f;

    /// <summary>
    ///     The Solreign wolf-form polymorph prototype (spec §2.2: "we will not write a bespoke
    ///     body-swap"). Referenced by id only. Phase-2 Track A1 (docs/plans/2026-07-11-ROADMAP-PHASE2.md)
    ///     built the prototype YAML this id points to —
    ///     Resources/Prototypes/_Solreign/Polymorphs/werewolf_polymorph.yml (configuration) +
    ///     Resources/Prototypes/_Solreign/Entities/Antags/werewolf.yml (the wolf-form entity itself,
    ///     inventory: None, revertOnCrit: true) — closing the "built but never instantiated" defect this
    ///     field used to carry. <c>SolreignWerewolfSystem.Transform.cs</c> still resolves it defensively
    ///     (<c>TryIndex</c>, not <c>Index</c>) so a FUTURE missing/renamed prototype still fails safe —
    ///     no crash, plus a diegetic popup (Track A1 minimum bar) — instead of throwing mid-round.
    /// </summary>
    [DataField]
    public ProtoId<PolymorphPrototype> WolfPolymorphPrototype = "SolreignWerewolfPolymorph";

    /// <summary>
    ///     The polymorphed wolf-form entity while <see cref="State"/> is <see cref="WerewolfState.Transformed"/>
    ///     or <see cref="WerewolfState.Waning"/>; null otherwise. Scheduling state, not config — this is
    ///     how the maul handler (subscribed on the wolf body via <see cref="SolreignWolfFormComponent"/>)
    ///     finds its way back to this component, and how <see cref="SolreignWerewolfSystem"/> knows what
    ///     to hand to <c>PolymorphSystem.Revert</c> when the state machine says to revert.
    /// </summary>
    [ViewVariables]
    public EntityUid? WolfForm;

    /// <summary>How long a chaplain's Sunrise Clause ritual do-after takes (spec §3.4, shared with vampire §4.4).</summary>
    [DataField]
    public float RitualDoAfterSeconds = 6f;
}
