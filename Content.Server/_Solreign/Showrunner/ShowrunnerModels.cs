using System.Collections.Generic;

namespace Content.Server._Solreign.Showrunner;

/// <summary>
///     A Showrunner plan is always an opening, an escalation, and a finale.
/// </summary>
public enum ShowrunnerBeatRole
{
    Opening,
    Escalation,
    Finale,
}

/// <summary>
///     Only <see cref="Eligible"/> capability evidence may enter a plan.
///     This state is supplied by a future trusted evidence-verification boundary;
///     the planner validates its shape but does not authenticate it.
/// </summary>
public enum ShowrunnerCapabilityState
{
    Unknown,
    PreviewOnly,
    Ineligible,
    Eligible,
}

/// <summary>
///     Closed, machine-readable explanations for a refused plan.
///     Values are ordered so a sorted result is stable across runs.
/// </summary>
public enum ShowrunnerReasonCode
{
    EmergencyStop,
    InvalidContext,
    TooManyDescriptors,
    InvalidDescriptor,
    DuplicateBeatId,
    DuplicateRuleId,
    CapabilityNotEligible,
    MapUnsupported,
    PopulationOutOfRange,
    CooldownActive,
    ActiveRuleConflict,
    HeadlinerLimit,
    MissingRoleCandidate,
    NoCompletePlan,
}

/// <summary>
///     Policy-neutral description of a single possible show beat.
///     This is not a game-rule prototype and cannot execute anything.
///     CapabilityState and CapabilityDigest must come from a trusted verifier;
///     a syntactically valid value here is not proof of review or authorization.
/// </summary>
public sealed record ShowrunnerBeatDescriptor(
    string BeatId,
    string RuleId,
    ShowrunnerBeatRole Role,
    int MinPlayers,
    int MaxPlayers,
    IReadOnlyCollection<string> AllowedMaps,
    bool IsHeadliner,
    ShowrunnerCapabilityState CapabilityState,
    string CapabilityDigest);

/// <summary>
///     Immutable facts supplied to a planning attempt. The seed is explicit;
///     the planner never consults wall-clock time or a random service.
/// </summary>
public sealed record ShowrunnerPlanningContext(
    ulong Seed,
    int Population,
    string MapId,
    bool EmergencyStop,
    IReadOnlyCollection<string> CoolingDownBeatIds,
    IReadOnlyCollection<string> ActiveRuleIds);

/// <summary>
///     A deterministic preview only. No runtime consumer or production catalog
///     is provided by this package. <see cref="PlanFingerprint"/> identifies the
///     normalized preview; it is unkeyed and must never authorize execution.
/// </summary>
public sealed record ShowrunnerPlan(
    ShowrunnerBeatDescriptor Opening,
    ShowrunnerBeatDescriptor Escalation,
    ShowrunnerBeatDescriptor Finale,
    string PlanFingerprint);

public sealed record ShowrunnerPlanningResult(
    ShowrunnerPlan? Plan,
    IReadOnlyList<ShowrunnerReasonCode> Reasons)
{
    public bool Success => Plan != null;
}
