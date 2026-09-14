using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.Chat.Systems;
using Content.Shared._Solreign.FX;
using Content.Shared.CCVar;

namespace Content.Server._Solreign.StationAudits;

/// <summary>
///     Inspection / mandatory same-session PROVIDENCE consequence layer (v14 gap-closure pass) —
///     extends the already-shipped, CVar-gated-OFF <see cref="StationAuditSystem"/> without touching
///     its existing, tested behavior. Closes the council's Pope-test gate
///     (docs/council/2026-07-17-v14-content-adjudication.md, line 147-150): a completed Station Audit
///     must produce a PROVIDENCE consequence within the same session, not just a filed report.
///
///     Two firing points:
///       1. Round start (<see cref="OnRoundStartingInspection"/>, wired from the existing
///          <c>OnRoundStarting</c> handler): PROVIDENCE assigns a small set of named criteria from
///          <see cref="StationAuditCriterionCatalog"/> and announces them over the PA; a mid-shift
///          checkpoint is scheduled at <see cref="CCVars.SolreignStationAuditCheckpointMinutes"/>.
///       2. The checkpoint itself (<see cref="Update"/>, same lightweight polling idiom as
///          <c>ProvidenceVoiceSystem.Update</c>'s idle-musings timer): self-grades the assigned
///          criteria against real, already-tracked round state and fires the mandatory consequence —
///          Commendation pays real HR Points via the already-shipped
///          <c>SeasonLedgerSystem.AwardHrPointsAsync</c>, Escalation raises the already-shipped
///          <see cref="SolreignScreenFxEvent"/>, QuietlyCorrectedError is PA/ledger-only by design
///          (Pope's test explicitly accepts "a ledger note" as a sufficient minimum consequence shape).
///       A round that ends before the checkpoint fires (short/low-pop shift) fires the same logic
///       inline at round end instead (<see cref="EnsureCheckpointFiredForRoundEnd"/>), wired ahead of
///       the base system's own CVar gate so the guarantee holds even in the (unusual) ops configuration
///       where the inspection layer is on but the base report is off.
///
///     Re-checked at both firing points — mid-flight-off guarantee, same discipline
///     <see cref="StationAuditSystem"/>'s own <c>OnRoundEndTextAppend</c> already documents. Both new
///     CVars shipped FALSE/default-off at the time this was written; the whole layer was a strict
///     no-op unless <see cref="CCVars.SolreignStationAuditInspectionEnabled"/> was explicitly
///     flipped on. ⚠️ CORRECTED 2026-08-02: that is no longer true. The 2026-07-25 activation pass
///     changed the default to TRUE (see the CVar's own doc, which is current). Anything reasoning
///     from "this layer is dormant unless switched on" is reasoning from a stale premise.
///
///     Audio note: the spec that preceded this build proposed 3 new
///     <c>ProvidenceLineCategory</c> voice lines for the checkpoint beats. This build deliberately
///     does NOT add them: every existing Providence voice category ships with real recorded audio
///     (Resources/Prototypes/_Solreign/providence_sounds.yml, 60 files / 12 categories), and no
///     recording exists for 3 brand-new categories. Shipping a <c>soundCollection</c> with an empty
///     <c>files</c> list is not the silent no-op the spec assumed — confirmed against
///     RobustToolbox/Robust.Shared/Audio/Systems/SharedAudioSystem.cs's <c>ResolveSound</c>:
///     <c>RandMan.Next(0)</c> returns index 0, and indexing an empty <c>PickFiles</c> list at 0 throws.
///     The mandatory consequence itself does not depend on audio — the PA announcement (real Loc text,
///     <see cref="ChatSystem.DispatchGlobalAnnouncement"/>) is the load-bearing "PROVIDENCE reacted"
///     signal; voice lines can be wired for free once the category is actually recorded.
/// </summary>
public sealed partial class StationAuditSystem
{
    [Dependency] private ChatSystem _chat = default!;

    /// <summary>Solreign HR PA color, same hex every Solreign HR-voice broadcast in this fork uses.</summary>
    private static readonly Color HrColor = Color.FromHex("#c0a062");

    private static readonly IReadOnlySet<int> AlwaysEligibleCatalogIndices = BuildAlwaysEligibleIndices();

    private static IReadOnlySet<int> BuildAlwaysEligibleIndices()
    {
        var set = new HashSet<int>();
        for (var i = 0; i < StationAuditCriterionCatalog.Criteria.Count; i++)
        {
            if (StationAuditCriterionCatalog.Criteria[i].AlwaysEligible)
                set.Add(i);
        }

        return set;
    }

    // Per-round inspection state — cleared alongside the base system's own per-round trackers (see
    // ClearRoundState's additive call to ClearInspectionRoundState below).
    private List<int> _inspectionAssignedIndices = new();
    private bool _checkpointScheduled;
    private bool _checkpointFired;
    private bool _checkpointFiredViaRoundEndFallback;
    private TimeSpan _checkpointFireTime;
    private StationAuditConsequenceKind _checkpointConsequenceKindThisRound = StationAuditConsequenceKind.None;
    private string? _checkpointFiredAtUtc;

    /// <summary>Snapshotted synchronously by <c>BuildInputs</c> each time it runs, read synchronously
    /// (before any <c>await</c>) at the top of <c>PersistAsync</c> — see that method's doc note. Avoids
    /// re-deriving the commendation account from <c>_commendationTally</c> inside an async
    /// continuation that could theoretically resume after <c>ClearRoundState</c> has already run.</summary>
    private Guid? _lastCommendationGuid;

    private void ClearInspectionRoundState()
    {
        _inspectionAssignedIndices = new List<int>();
        _checkpointScheduled = false;
        _checkpointFired = false;
        _checkpointFiredViaRoundEndFallback = false;
        _checkpointConsequenceKindThisRound = StationAuditConsequenceKind.None;
        _checkpointFiredAtUtc = null;
        _lastCommendationGuid = null;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_checkpointScheduled || _checkpointFired)
            return;

        if (_timing.CurTime < _checkpointFireTime)
            return;

        // Re-checked at fire time — mid-flight-off guarantee. Flipping the CVar off between the
        // scheduling point and the fire time suppresses the fire entirely (no PA, no side effect),
        // same posture as the base system's own OnRoundEndTextAppend re-check.
        if (!_cfg.GetCVar(CCVars.SolreignStationAuditInspectionEnabled))
        {
            _checkpointFired = true;
            return;
        }

        FireCheckpoint(viaRoundEndFallback: false);
    }

    private void OnRoundStartingInspection()
    {
        if (!_cfg.GetCVar(CCVars.SolreignStationAuditInspectionEnabled))
            return;

        var catalogCount = StationAuditCriterionCatalog.Criteria.Count;
        var assignCount = _cfg.GetCVar(CCVars.SolreignStationAuditInspectionCount);

        var indices = StationAuditInspectionSelection.SelectAssigned(
            _ticker.RoundId, catalogCount, assignCount, AlwaysEligibleCatalogIndices);

        _inspectionAssignedIndices = indices.ToList();

        if (_inspectionAssignedIndices.Count == 0)
        {
            // A misconfigured zero (or zero-size catalog) — nothing to assign, so the consequence
            // layer no-ops this round rather than throwing or scheduling a checkpoint that would
            // grade nothing.
            return;
        }

        var names = string.Join(", ", _inspectionAssignedIndices
            .Select(i => Loc.GetString(StationAuditCriterionCatalog.Criteria[i].NameLocKey)));

        _chat.DispatchGlobalAnnouncement(
            Loc.GetString("solreign-station-audit-inspection-assigned-announcement",
                ("count", _inspectionAssignedIndices.Count), ("names", names)),
            Loc.GetString("solreign-station-audit-hr-sender"),
            playSound: true,
            colorOverride: HrColor);

        var checkpointMinutes = Math.Max(0, _cfg.GetCVar(CCVars.SolreignStationAuditCheckpointMinutes));
        _checkpointFireTime = _timing.CurTime + TimeSpan.FromMinutes(checkpointMinutes);
        _checkpointScheduled = true;
        _checkpointFired = false;
    }

    /// <summary>Round-end fallback: if the layer scheduled a checkpoint but the round ended before it
    /// fired, fire it now (inline, using true end-of-shift state) so a same-session consequence still
    /// lands even on a short shift. Deliberately called ahead of the base system's own
    /// <c>SolreignStationAuditEnabled</c> gate in <c>OnRoundEndTextAppend</c> — the mandatory
    /// consequence must not silently depend on whether the base report itself is enabled.</summary>
    private void EnsureCheckpointFiredForRoundEnd()
    {
        if (!_checkpointScheduled || _checkpointFired)
            return;

        if (!_cfg.GetCVar(CCVars.SolreignStationAuditInspectionEnabled))
        {
            _checkpointFired = true;
            return;
        }

        FireCheckpoint(viaRoundEndFallback: true);
    }

    private void FireCheckpoint(bool viaRoundEndFallback)
    {
        if (_checkpointFired)
            return;

        _checkpointFired = true;
        _checkpointFiredViaRoundEndFallback = viaRoundEndFallback;

        if (_inspectionAssignedIndices.Count == 0)
        {
            _checkpointConsequenceKindThisRound = StationAuditConsequenceKind.None;
            return;
        }

        var inputs = BuildInputs();
        var results = GradeAssigned(inputs);
        var kind = StationAuditCheckpointVerdict.Determine(results.Select(r => r.Verdict).ToList());

        _checkpointConsequenceKindThisRound = kind;
        _checkpointFiredAtUtc = DateTime.UtcNow.ToString("o");

        FireConsequence(kind);
    }

    private List<StationAuditCriterionResult> GradeAssigned(StationAuditInputs inputs)
    {
        var results = new List<StationAuditCriterionResult>(_inspectionAssignedIndices.Count);
        foreach (var index in _inspectionAssignedIndices)
        {
            var criterion = StationAuditCriterionCatalog.Criteria[index];
            var verdict = StationAuditCriterionGrading.Grade(criterion, inputs);
            results.Add(new StationAuditCriterionResult(criterion.Id, verdict));
        }

        return results;
    }

    /// <summary>
    ///     Fires the mandatory consequence's real, observable side effect for one checkpoint verdict.
    ///     Commendation pays real HR Points (reusing the already-shipped, always-cumulative
    ///     <c>SeasonLedgerSystem.AwardHrPointsAsync</c> verbatim); Escalation raises the already-shipped
    ///     <see cref="SolreignScreenFxEvent"/>; QuietlyCorrectedError is PA/ledger-only by design (Pope's
    ///     test explicitly accepts a tone shift / ledger note as sufficient — this pass does not
    ///     over-build a mechanical hook for it).
    /// </summary>
    private void FireConsequence(StationAuditConsequenceKind kind)
    {
        switch (kind)
        {
            case StationAuditConsequenceKind.Commendation:
                _chat.DispatchGlobalAnnouncement(
                    Loc.GetString("solreign-station-audit-checkpoint-pa-commendation"),
                    Loc.GetString("solreign-station-audit-hr-sender"),
                    playSound: true,
                    colorOverride: HrColor);

                // Degrade rule (spec-documented): if no commendation account has resolved yet this
                // early in the shift, the tone (PA) still fires but no HR Points payout target
                // exists — never invent one. _lastCommendationGuid is refreshed by the BuildInputs
                // call FireCheckpoint just made.
                if (_lastCommendationGuid is { } guid)
                {
                    var points = _cfg.GetCVar(CCVars.SolreignStationAuditCommendationHrPoints);
                    if (points > 0)
                        AwardCommendationHrPointsAsync(guid, points);
                }

                break;

            case StationAuditConsequenceKind.Escalation:
                _chat.DispatchGlobalAnnouncement(
                    Loc.GetString("solreign-station-audit-checkpoint-pa-escalation"),
                    Loc.GetString("solreign-station-audit-hr-sender"),
                    playSound: true,
                    colorOverride: HrColor);

                RaiseNetworkEvent(new SolreignScreenFxEvent());
                break;

            case StationAuditConsequenceKind.QuietlyCorrectedError:
                _chat.DispatchGlobalAnnouncement(
                    Loc.GetString("solreign-station-audit-checkpoint-pa-quietly-corrected"),
                    Loc.GetString("solreign-station-audit-hr-sender"),
                    playSound: false,
                    colorOverride: HrColor);
                break;

            case StationAuditConsequenceKind.None:
            default:
                break;
        }
    }

    /// <summary>async void + try/catch — same house threading contract as the base system's own
    /// PersistAsync: a storage failure costs one HR Points payout, never the tick.</summary>
    private async void AwardCommendationHrPointsAsync(Guid user, int points)
    {
        try
        {
            await _ledger.AwardHrPointsAsync(user, points);
        }
        catch (Exception e)
        {
            Log.Error($"Station Audit: failed to award checkpoint commendation HR points to {user}: {e}");
        }
    }

    /// <summary>
    ///     nyanopark-019 fold ("borrowed, not built" — no new stats system): fires a short PROVIDENCE
    ///     follow-up citing two lifetime numbers, both already obtainable from the existing Ledger
    ///     surface. Deliberately NOT part of the synchronous round-end render
    ///     (<c>RenderLines</c>/printed paper) — both reads are async Ledger calls, and this system's
    ///     round-end path is a hard "never block the tick" async-void continuation
    ///     (<c>PersistAsync</c>'s own doc comment states this law); the established idiom for citing an
    ///     async-fetched lifetime number in this fork is a delayed follow-up beat (see
    ///     <c>ProvidenceWelcomeSystem.LoadWelcome</c>), not synchronous inclusion in text that has
    ///     already rendered. Graceful degrade on a storage hiccup: the citation simply doesn't fire,
    ///     the already-printed report and ledger row are unaffected.
    /// </summary>
    private async System.Threading.Tasks.Task FireLifetimeCitationAsync(StationAuditReport report, Guid? commendationGuid)
    {
        try
        {
            var count = await _ledger.GetStationAuditLifetimeCountAsync();
            var text = Loc.GetString("solreign-station-audit-lifetime-count", ("count", count));

            if (report.HasCommendation && commendationGuid is { } guid)
            {
                var career = await _ledger.GetCareerStatsAsync(guid);
                text += " " + Loc.GetString("solreign-station-audit-lifetime-commendation-tours",
                    ("name", report.CommendationName!), ("tours", career.Tours));
            }

            _chat.DispatchGlobalAnnouncement(
                text,
                Loc.GetString("solreign-station-audit-hr-sender"),
                playSound: false,
                colorOverride: HrColor);
        }
        catch (Exception e)
        {
            Log.Error($"Station Audit: lifetime citation failed for round {report.RoundId}: {e}");
        }
    }

    /// <summary>Renders the "[PROVIDENCE INSPECTION — ASSIGNED THIS SHIFT]" section and checkpoint
    /// reaction note (spec §4.3) — called additively from the base system's <c>RenderLines</c>.
    /// Returns an empty list when the inspection layer produced nothing this shift (disabled, or a
    /// misconfigured zero-count), so the base report's shape is completely unchanged when dormant.</summary>
    private List<string> RenderInspectionLines(StationAuditReport r)
    {
        var lines = new List<string>();

        if (r.AssignedCriteria.Count == 0)
            return lines;

        lines.Add(Loc.GetString("solreign-station-audit-inspection-header"));

        foreach (var result in r.AssignedCriteria)
        {
            var criterion = StationAuditCriterionCatalog.Criteria.FirstOrDefault(c => c.Id == result.CriterionId);
            var name = criterion is null ? result.CriterionId : Loc.GetString(criterion.NameLocKey);

            lines.Add(result.Verdict switch
            {
                StationAuditCriterionVerdict.Pass => Loc.GetString("solreign-station-audit-inspection-verdict-pass", ("name", name)),
                StationAuditCriterionVerdict.Fail => Loc.GetString("solreign-station-audit-inspection-verdict-fail", ("name", name)),
                _ => Loc.GetString("solreign-station-audit-inspection-verdict-na", ("name", name)),
            });
        }

        var note = CheckpointNoteLocString(r.CheckpointConsequenceKind);
        if (note is not null)
            lines.Add(note);

        return lines;
    }

    private string? CheckpointNoteLocString(StationAuditConsequenceKind kind)
    {
        if (kind == StationAuditConsequenceKind.None)
            return null;

        if (_checkpointFiredViaRoundEndFallback)
            return Loc.GetString("solreign-station-audit-checkpoint-concluded-at-round-end");

        var minutes = _cfg.GetCVar(CCVars.SolreignStationAuditCheckpointMinutes);
        return kind switch
        {
            StationAuditConsequenceKind.Commendation => Loc.GetString("solreign-station-audit-checkpoint-commendation", ("minutes", minutes)),
            StationAuditConsequenceKind.Escalation => Loc.GetString("solreign-station-audit-checkpoint-escalation", ("minutes", minutes)),
            StationAuditConsequenceKind.QuietlyCorrectedError => Loc.GetString("solreign-station-audit-checkpoint-quietly-corrected", ("minutes", minutes)),
            _ => null,
        };
    }

    /// <summary>Serializes the assigned-criteria verdicts to the compact JSON array persisted in
    /// <c>station_audit_log.criteria_json</c>. Hand-rolled (no new JSON-library dependency) — the
    /// shape is trivial and fixed.</summary>
    private static string SerializeCriteriaJson(IReadOnlyList<StationAuditCriterionResult> results)
    {
        if (results.Count == 0)
            return "[]";

        var entries = results.Select(r =>
            $$"""{"criterionId":"{{r.CriterionId}}","verdict":"{{r.Verdict}}"}""");
        return "[" + string.Join(",", entries) + "]";
    }

    private static string ConsequenceKindToDbString(StationAuditConsequenceKind kind) => kind switch
    {
        StationAuditConsequenceKind.Commendation => "commendation",
        StationAuditConsequenceKind.Escalation => "escalation",
        StationAuditConsequenceKind.QuietlyCorrectedError => "quietly_corrected_error",
        _ => "none",
    };

    // --- Test seams --------------------------------------------------------------------------------

    /// <summary>Test-only: invokes the round-start assignment logic directly rather than broadcasting
    /// a synthetic <c>RoundStartingEvent</c> on the real event bus — a synthetic re-fire of that event
    /// on an already-live pooled server collides with unrelated systems that key their own state by
    /// round id (e.g. <c>AdminLogManager.CacheNewRound</c> throws "same key already added" on a
    /// duplicate/synthetic round-start for the round id already live). This seam exercises exactly
    /// what <see cref="StationAuditSystem"/>'s own <c>OnRoundStarting</c> handler additively calls,
    /// without touching any other system's subscription.</summary>
    internal void SimulateRoundStartForTests()
    {
        // 🔴 The reset is load-bearing, not tidiness. The real RoundStartingEvent handler
        // (StationAuditSystem.OnRoundStarting) calls ClearRoundState() — which includes
        // ClearInspectionRoundState() — BEFORE it calls OnRoundStartingInspection(). A seam that
        // skipped the clear was not simulating a round start at all: OnRoundStartingInspection
        // returns early when the layer is disabled, so the PREVIOUS round's _checkpointScheduled
        // survived into the "new" round and a round that began with the layer OFF still reported a
        // scheduled checkpoint. That is what made
        // InspectionDisabled_RoundStart_SchedulesNoCheckpoint fail deterministically against a
        // pooled server (which has already had a real round start under the CVar's true default).
        // The product was always correct here; only this seam was unfaithful.
        ClearInspectionRoundState();
        OnRoundStartingInspection();
    }

    /// <summary>Test-only: forces the checkpoint to fire immediately regardless of the scheduled fire
    /// time, so integration tests don't need to wait out a real 15-minute timer.</summary>
    internal void ForceCheckpointFireForTests()
    {
        if (_checkpointScheduled && !_checkpointFired)
            FireCheckpoint(viaRoundEndFallback: false);
    }

    /// <summary>Test-only: exercises the real <see cref="FireConsequence"/> dispatch for a specific
    /// kind directly, bypassing the (already exhaustively pure-unit-tested — see
    /// <c>StationAuditCriterionGradingTests</c>) grading/selection decision of WHICH kind fires this
    /// shift. Lets integration tests deterministically verify each kind's real ECS side effect (PA
    /// announcement, HR Points ledger delta, <see cref="Content.Shared._Solreign.FX.SolreignScreenFxEvent"/>)
    /// without depending on which criteria a real round-id-driven assignment happened to pick.</summary>
    internal void FireConsequenceForTests(StationAuditConsequenceKind kind) => FireConsequence(kind);

    /// <summary>Test-only: sets the commendation account <see cref="FireConsequence"/> pays HR Points
    /// to, mirroring what a real <c>BuildInputs</c> call would have snapshotted from a live kill
    /// attribution.</summary>
    internal void SetCommendationGuidForTests(Guid? guid) => _lastCommendationGuid = guid;

    internal StationAuditConsequenceKind CheckpointConsequenceKindForTests => _checkpointConsequenceKindThisRound;
    internal bool CheckpointScheduledForTests => _checkpointScheduled;
    internal bool CheckpointFiredForTests => _checkpointFired;
    internal bool CheckpointFiredViaRoundEndFallbackForTests => _checkpointFiredViaRoundEndFallback;
    internal int AssignedCriteriaCountForTests => _inspectionAssignedIndices.Count;
}
