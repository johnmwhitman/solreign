namespace Content.Server._Solreign.Terminator;

/// <summary>
///     TERMINATOR SPIKE (antag &amp; creature pack): marks a mob as a Compliance Retrieval Unit — a
///     ghost-role hunter that fixates on exactly ONE crew member (chosen by
///     <see cref="FixationRules.SelectTarget"/> from round-state criteria) and pursues them for a
///     NONLETHAL takedown: drag the target to a Compliance Desk, never kill. EMP staggers it;
///     teamwork blocks it. See docs/specs/2026-07-11-terminator-spike.md for the full mechanic.
///
///     Server-only on purpose: fixation state drives server AI + server-side aggro exceptions and
///     needs no prediction. Client-facing tells (eye glow toward target, HUD arrow for the pilot)
///     are next wave.
/// </summary>
[RegisterComponent, Access(typeof(ComplianceRetrievalUnitSystem))]
public sealed partial class ComplianceFixationComponent : Component
{
    /// <summary>The one crew member this unit is fixated on. Null while dormant (no eligible pool yet).</summary>
    [ViewVariables]
    public EntityUid? Target;

    /// <summary>New-join protection window; players on shift less than this are never selected.</summary>
    [DataField]
    public float GraceMinutes = FixationRules.DefaultGraceMinutes;

    /// <summary>How often the unit re-polls for a target while dormant (or after losing one).</summary>
    [DataField]
    public TimeSpan RetargetPoll = TimeSpan.FromSeconds(15);

    /// <summary>How long an EMP pulse staggers the unit (full paralyze; its one hard counter).</summary>
    [DataField]
    public TimeSpan EmpStagger = TimeSpan.FromSeconds(8);

    /// <summary>When the next dormant-poll for a target is allowed.</summary>
    [ViewVariables]
    public TimeSpan NextPoll;

    /// <summary>Until when the unit is EMP-staggered (see <see cref="FixationRules.IsStaggered"/>).</summary>
    [ViewVariables]
    public TimeSpan StaggeredUntil;
}
