using System;
using System.Collections.Generic;

namespace Content.Server._Solreign.StationAudits;

/// <summary>One assigned criterion's self-graded verdict this shift (inspection layer, gap-closure
/// pass). Deliberately carries only the closed-vocabulary id + verdict — rendering resolves the id to
/// its Loc-keyed name/pass/fail text via <see cref="StationAuditCriterionCatalog"/>, same "pure data,
/// resolved separately" split as everything else in this report.</summary>
public readonly record struct StationAuditCriterionResult(
    string CriterionId,
    StationAuditCriterionVerdict Verdict);

/// <summary>
///     Everything <see cref="StationAuditComposer.Compose"/> reads to assemble one shift's Station
///     Audit. Deliberately a flat, ECS-free struct — every field is something the calling system
///     already tracked locally (no daemon round-trip, no free text) — so composition stays pure and
///     unit-testable without spinning up the game (<c>Content.Tests/_Solreign/StationAuditComposerTests.cs</c>).
///
///     A <c>null</c>/false optional field means "this shift genuinely has nothing here", not
///     "unknown" — the composer's job is to render that honestly (spec: "empty shift must produce an
///     honest minimal audit, never fake numbers"), not to fabricate a placeholder.
/// </summary>
public readonly record struct StationAuditInputs(
    int RoundId,
    TimeSpan ShiftDuration,
    int CrewCount,
    int DeathCount,
    bool FirstDeathCommemorated,
    string? DirectiveTitle,
    bool DirectiveOutcomeReported,
    bool DirectiveOutcomeFulfilled,
    bool StipendsEnabled,
    int StipendsProcessed,
    bool BountiesEnabled,
    int BountyVerdicts,
    int NotableEventCount,
    string? CommendationName,
    int CommendationScore,
    string ItemOfConcernId,
    // --- Inspection layer (gap-closure pass) — trailing, defaulted fields so every existing
    // named-argument call site (StationAuditSystem.BuildInputs, all three pre-existing test files)
    // keeps compiling unmodified. A null AssignedCriteria means "the inspection layer produced
    // nothing this shift" (disabled, or a misconfigured zero-count) — same "null means genuinely
    // nothing here" honesty law as every other optional field on this record.
    IReadOnlyList<StationAuditCriterionResult>? AssignedCriteria = null,
    StationAuditConsequenceKind CheckpointConsequenceKind = StationAuditConsequenceKind.None);

/// <summary>
///     The composed, closed-vocabulary shape of one Station Audit. Every field here maps 1:1 onto a
///     Loc key (or a small fixed set of them) in <c>station-audits.ftl</c> — nothing here is free
///     text, and nothing here is ever populated from anything but real locally-observed state (with
///     the single documented exception of <see cref="ItemOfConcernId"/>, which is explicitly
///     fictional flavor by design — see the spec's "Item of Concern" note).
/// </summary>
public readonly record struct StationAuditReport(
    int RoundId,
    int ShiftDurationMinutes,
    int CrewCount,
    bool HasDeaths,
    int DeathCount,
    bool FirstDeathCommemorated,
    bool HasDirective,
    string? DirectiveTitle,
    bool DirectiveOutcomeReported,
    bool DirectiveOutcomeFulfilled,
    bool StipendsEnabled,
    int StipendsProcessed,
    bool BountiesEnabled,
    int BountyVerdicts,
    int NotableEventCount,
    bool HasCommendation,
    string? CommendationName,
    int CommendationScore,
    string ItemOfConcernId,
    // Composer-resolved: always non-null (empty list when the inspection layer produced nothing this
    // shift) — see StationAuditInputs.AssignedCriteria's doc comment for the honesty law this mirrors.
    IReadOnlyList<StationAuditCriterionResult> AssignedCriteria,
    StationAuditConsequenceKind CheckpointConsequenceKind = StationAuditConsequenceKind.None);
