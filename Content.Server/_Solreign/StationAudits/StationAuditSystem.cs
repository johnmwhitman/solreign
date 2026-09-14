using System;
using System.Collections.Generic;
using Content.Server._Solreign.Bounties;
using Content.Server._Solreign.Providence;
using Content.Server._Solreign.SeasonLedger;
using Content.Server._Solreign.StationDirective;
using Content.Server._Solreign.StationDirective.Components;
using Content.Server.Communications;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.IdentityManagement;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Paper;
using Content.Shared.Projectiles;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Solreign.StationAudits;

/// <summary>
///     "Station Audit" (v14 wave-1 #3, council C1 deltav-008 lineage) — PROVIDENCE compiles a deadpan
///     end-of-shift corporate assessment from real, locally-observed round state: shift duration,
///     crew count, a Corporate Directive on file (if any — reads the real, already-shipped
///     <see cref="StationDirectiveRuleComponent"/>), deaths (+ whether the shift's first death was
///     commemorated), stipend/bounty activity (if those systems are enabled), a notable-event count,
///     an auto-selected "Commendation of the Shift" (a real handle-named account from tracked kill
///     attribution), and a deliberately fictional "Item of Concern". Two outputs: the text is
///     appended to the round-end summary screen, and a keepsake paper is printed at a station comms
///     console. A compact summary row is also appended to the <c>station_audit_log</c> Season Ledger
///     table (<see cref="SeasonLedgerSystem.AppendStationAuditAsync"/>).
///
///     CLEAN-ROOM NOTE: inspired only by Delta-V's PUBLIC description of a station report/audit paper
///     reaching the round-end screen. No Delta-V code was read, copied, or referenced while building
///     this — every section, table, event, and template here is built fresh from this fork's own
///     idioms (<c>SeasonLedgerSystem</c>'s round-end handoff pattern, <c>SolreignCorporateRuleSystem</c>'s
///     <c>AppendRoundEndText</c> idiom, <c>SolreignFinalBalanceSheetRule</c>'s paper-spawn idiom,
///     <c>FirstDeathEpitaphPicker</c>'s "pure selection, Loc-resolved separately" idiom).
///
///     Gated end-to-end on <see cref="CCVars.SolreignStationAuditEnabled"/>, re-checked at fire time —
///     SHIPS FALSE (council precondition: dormant on land). Zero daemon dependency: every input this
///     system reads is local ECS/CVar state, never a Director round-trip. Cheap: all assembly happens
///     once, at shift end, from state already tracked continuously through the round (no per-tick
///     scans).
///
///     Directive-outcome seam: this system does NOT yet know whether a directive was "fulfilled" —
///     no local system computes that today (Station Directive is documented as flavor-only, "no new
///     metric tracking"). <see cref="SolreignDirectiveOutcomeQueryEvent"/> is the loose seam a future
///     directives-fax lane can answer; absent an answer, the audit honestly reports "not on record"
///     rather than fabricating a verdict.
///
///     Print-point note (reconciliation): this lane chose the station's (first found)
///     <see cref="CommunicationsConsoleComponent"/> as the paper's spawn point, since feat/directives-fax
///     had not reached a receipt (still at the master tip) at build time and so had made no print-point
///     decision to mirror. If that lane later lands its own print-point choice for a directive/fax
///     artifact, reconcile the two so a shift doesn't end with keepsakes materializing at two different
///     "the station's paperwork appears here" locations.
/// </summary>
public sealed partial class StationAuditSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private PaperSystem _paper = default!;
    [Dependency] private SeasonLedgerSystem _ledger = default!;
    [Dependency] private ProvidenceFirstDeathSystem _firstDeath = default!;
    [Dependency] private SolreignBountySystem _bounties = default!;

    private const string AuditPaperPrototype = "SolreignPaperStationAudit";

    // Per-round trackers — self-contained (mirrors SeasonLedgerSystem.EarlyDeath's discipline) so
    // this system never depends on RoundEndMessageEvent's roster arriving before RoundEndTextAppendEvent.
    private TimeSpan? _roundStart;
    private readonly HashSet<Guid> _crewThisRound = new();
    private readonly HashSet<Guid> _deathsThisRound = new();
    private readonly Dictionary<Guid, int> _commendationTally = new();
    private readonly Dictionary<Guid, string> _commendationNames = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<RoundEndTextAppendEvent>(OnRoundEndTextAppend);
    }

    private void OnRoundStarting(RoundStartingEvent ev)
    {
        _roundStart = _timing.CurTime;
        ClearRoundState();

        // Inspection layer (gap-closure pass): assigns this shift's criteria + schedules the
        // mid-shift checkpoint. Own CVar gate, re-checked inside — a strict no-op while
        // CCVars.SolreignStationAuditInspectionEnabled is off. ⚠️ That CVar is no longer
        // off-by-default: the 2026-07-25 activation pass shipped it TRUE. This comment claimed
        // "(default)" and was false from that day; corrected 2026-08-02.
        // NOTE the ordering below is load-bearing: ClearRoundState() above resets the inspection
        // layer's per-round flags BEFORE this call, so a round that starts with the layer disabled
        // does not inherit the previous round's scheduled checkpoint.
        OnRoundStartingInspection();
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _roundStart = null;
        ClearRoundState();
    }

    private void ClearRoundState()
    {
        _crewThisRound.Clear();
        _deathsThisRound.Clear();
        _commendationTally.Clear();
        _commendationNames.Clear();

        // Inspection layer (gap-closure pass) — additive wiring edit only.
        ClearInspectionRoundState();
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        _crewThisRound.Add(ev.Player.UserId.UserId);
    }

    /// <summary>
    ///     Tracks two independent things off the same broadcast: which accounts died this round
    ///     (deaths), and which account gets credit for the kill (commendation tally) — same
    ///     resolution DeathAttribution uses (direct melee/interaction origin, or one hop through a
    ///     projectile's shooter), duplicated locally rather than widening DeathAttribution's own
    ///     contract, so a self-kill never credits its own victim.
    /// </summary>
    private void OnMobStateChanged(MobStateChangedEvent ev)
    {
        if (ev.NewMobState != MobState.Dead)
            return;

        Guid? targetUser = null;
        if (_mind.TryGetMind(ev.Target, out _, out var mind) && mind.UserId is { } deadUser)
        {
            targetUser = deadUser.UserId;
            _deathsThisRound.Add(deadUser.UserId);
        }

        if (TryResolveAttacker(ev.Origin, out var attackerGuid, out var namedEntity) && attackerGuid != targetUser)
        {
            _commendationTally.TryGetValue(attackerGuid, out var score);
            _commendationTally[attackerGuid] = score + 1;
            _commendationNames[attackerGuid] = Identity.Name(namedEntity, EntityManager);
        }
    }

    /// <summary>Mirrors <c>Content.Server._Solreign.Director.DeathAttribution.TryResolveAttackerGuid</c>'s
    /// resolution exactly, but also returns the resolved entity (for <see cref="Identity.Name"/>) —
    /// duplicated locally rather than widening that helper's public contract for one extra caller.</summary>
    private bool TryResolveAttacker(EntityUid? origin, out Guid guid, out EntityUid namedEntity)
    {
        guid = default;
        namedEntity = default;

        if (origin is not { } originEnt || !Exists(originEnt))
            return false;

        if (TryComp<ActorComponent>(originEnt, out var actor))
        {
            guid = actor.PlayerSession.UserId.UserId;
            namedEntity = originEnt;
            return true;
        }

        if (TryComp<ProjectileComponent>(originEnt, out var projectile)
            && projectile.Shooter is { } shooter
            && Exists(shooter)
            && TryComp<ActorComponent>(shooter, out var shooterActor))
        {
            guid = shooterActor.PlayerSession.UserId.UserId;
            namedEntity = shooter;
            return true;
        }

        return false;
    }

    private void OnRoundEndTextAppend(RoundEndTextAppendEvent ev)
    {
        // Inspection layer round-end fallback (gap-closure pass): fires the mandatory PROVIDENCE
        // consequence inline, using final-state inputs, if the round ended before the mid-shift
        // checkpoint got a chance to. Deliberately called AHEAD of the base CVar gate below — the
        // mandatory consequence must not silently depend on whether the base report itself renders.
        // Own CVar gate re-checked inside; a strict no-op while the inspection layer is off.
        EnsureCheckpointFiredForRoundEnd();

        // Re-checked at fire time — the universal mid-flight-CVar-off guarantee every Solreign
        // feature honors.
        if (!_cfg.GetCVar(CCVars.SolreignStationAuditEnabled))
            return;

        var inputs = BuildInputs();
        var report = StationAuditComposer.Compose(inputs);
        var lines = RenderLines(report);

        foreach (var line in lines)
            ev.AddLine(line);

        SpawnAuditPaper(string.Join("\n", lines));
        PersistAsync(report);
    }

    private StationAuditInputs BuildInputs()
    {
        var duration = _roundStart is { } start && _timing.CurTime > start
            ? _timing.CurTime - start
            : TimeSpan.Zero;

        var outcomeQuery = new SolreignDirectiveOutcomeQueryEvent();
        RaiseLocalEvent(outcomeQuery);

        var commendation = PickCommendation();
        // Inspection layer (gap-closure pass): snapshotted synchronously here, read synchronously
        // (before any await) at the top of PersistAsync — see that field's doc comment.
        _lastCommendationGuid = commendation?.Guid;

        var salaryEnabled = _cfg.GetCVar(CCVars.SolreignSalaryEnabled);
        var bountiesEnabled = _cfg.GetCVar(CCVars.SolreignBountiesEnabled);

        var inputs = new StationAuditInputs(
            RoundId: _ticker.RoundId,
            ShiftDuration: duration,
            CrewCount: _crewThisRound.Count,
            DeathCount: _deathsThisRound.Count,
            FirstDeathCommemorated: _firstDeath.FirstDeathCommemoratedThisRound,
            DirectiveTitle: ResolveDirectiveTitle(),
            DirectiveOutcomeReported: outcomeQuery.Reported,
            DirectiveOutcomeFulfilled: outcomeQuery.Fulfilled,
            StipendsEnabled: salaryEnabled,
            // Every account that spawned crew this shift IS salary-eligible per SalaryRosterPayload's
            // own definition ("has an account guid and never observer-only") — reusing our own
            // self-tracked crew set avoids depending on RoundEndMessageEvent's roster, which arrives
            // AFTER RoundEndTextAppendEvent (see class doc comment).
            StipendsProcessed: _crewThisRound.Count,
            BountiesEnabled: bountiesEnabled,
            BountyVerdicts: _bounties.ClaimVerdictsThisRoundForAudit,
            NotableEventCount: _ticker.AllPreviousGameRules.Count,
            CommendationName: commendation?.Name,
            CommendationScore: commendation?.Score ?? 0,
            ItemOfConcernId: ItemOfConcernPicker.Pick(_ticker.RoundId));

        // Inspection layer (gap-closure pass): fold in a FRESH self-grading pass against whatever
        // state is true right now (mid-shift at the checkpoint, or final at round end — BuildInputs
        // is called from both places) plus the checkpoint's actually-fired consequence kind. A no-op
        // (both fields keep their StationAuditInputs defaults) whenever nothing is assigned this
        // shift, so the base report's shape is unchanged while the layer is dormant.
        if (_inspectionAssignedIndices.Count > 0)
        {
            inputs = inputs with
            {
                AssignedCriteria = GradeAssigned(inputs),
                CheckpointConsequenceKind = _checkpointConsequenceKindThisRound,
            };
        }

        return inputs;
    }

    /// <summary>Reads whichever Corporate Directive was selected this round, if the (already-shipped,
    /// default-on) Station Directive layer started. Read-only Access grant — see
    /// <see cref="StationDirectiveRuleComponent"/>'s doc comment.</summary>
    private string? ResolveDirectiveTitle()
    {
        var query = EntityQueryEnumerator<StationDirectiveRuleComponent>();
        while (query.MoveNext(out _, out var comp))
        {
            if (comp.DirectiveIndex < 0 || comp.DirectiveIndex >= StationDirectiveCatalog.Directives.Count)
                continue;

            var id = StationDirectiveCatalog.Directives[comp.DirectiveIndex].Id;
            return StationAuditDirectiveTitles.Resolve(id);
        }

        return null;
    }

    /// <summary>Also returns the winner's account Guid (extended for the inspection layer's HR-Points
    /// payout and the round-end lifetime-tours citation — both need a real account to pay/query, not
    /// just the display name) — additive, its one pre-existing call site (<see cref="BuildInputs"/>)
    /// still reads <c>.Name</c>/<c>.Score</c> unmodified.</summary>
    private (Guid Guid, string Name, int Score)? PickCommendation()
    {
        (Guid Guid, string Name, int Score)? best = null;
        foreach (var (guid, score) in _commendationTally)
        {
            if (score <= 0)
                continue;

            if (best is null || score > best.Value.Score)
                best = (guid, _commendationNames.TryGetValue(guid, out var name) ? name : guid.ToString(), score);
        }

        return best;
    }

    private List<string> RenderLines(StationAuditReport r)
    {
        var lines = new List<string>
        {
            Loc.GetString("solreign-station-audit-header", ("round", r.RoundId)),
            Loc.GetString("solreign-station-audit-shift-duration", ("minutes", r.ShiftDurationMinutes)),
            Loc.GetString("solreign-station-audit-crew-count", ("count", r.CrewCount)),
        };

        // Inspection layer (gap-closure pass): [PROVIDENCE INSPECTION] section, inserted after the
        // header/duration/crew lines and before the directive section (spec §4.3). Renders nothing
        // when the layer produced nothing this shift — additive, zero footprint while dormant.
        lines.AddRange(RenderInspectionLines(r));

        lines.Add(r.HasDirective
            ? Loc.GetString("solreign-station-audit-directive-present", ("title", r.DirectiveTitle!))
            : Loc.GetString("solreign-station-audit-directive-absent"));

        if (r.HasDirective)
        {
            lines.Add(!r.DirectiveOutcomeReported
                ? Loc.GetString("solreign-station-audit-directive-outcome-unreported")
                : r.DirectiveOutcomeFulfilled
                    ? Loc.GetString("solreign-station-audit-directive-outcome-fulfilled")
                    : Loc.GetString("solreign-station-audit-directive-outcome-unfulfilled"));
        }

        lines.Add(r.HasDeaths
            ? Loc.GetString("solreign-station-audit-deaths-some", ("count", r.DeathCount))
            : Loc.GetString("solreign-station-audit-deaths-none"));

        if (r.HasDeaths)
        {
            lines.Add(r.FirstDeathCommemorated
                ? Loc.GetString("solreign-station-audit-deaths-commemorated")
                : Loc.GetString("solreign-station-audit-deaths-uncommemorated"));
        }

        lines.Add(r.StipendsEnabled
            ? Loc.GetString("solreign-station-audit-stipends-processed", ("count", r.StipendsProcessed))
            : Loc.GetString("solreign-station-audit-stipends-disabled"));

        lines.Add(r.BountiesEnabled
            ? Loc.GetString("solreign-station-audit-bounties-count", ("count", r.BountyVerdicts))
            : Loc.GetString("solreign-station-audit-bounties-disabled"));

        lines.Add(Loc.GetString("solreign-station-audit-notable-events", ("count", r.NotableEventCount)));

        lines.Add(r.HasCommendation
            ? Loc.GetString("solreign-station-audit-commendation-present", ("name", r.CommendationName!))
            : Loc.GetString("solreign-station-audit-commendation-absent"));

        lines.Add(Loc.GetString("solreign-station-audit-item-of-concern-header"));
        lines.Add(Loc.GetString($"solreign-station-audit-item-of-concern-{r.ItemOfConcernId}"));

        lines.Add(Loc.GetString("solreign-station-audit-footer"));

        return lines;
    }

    /// <summary>Prints the keepsake at the station's (first found) comms console. No console found
    /// (e.g. a test/dev map with none) → skipped entirely; the round-end text and ledger row are
    /// unaffected. See the class doc comment's print-point reconciliation note.</summary>
    private void SpawnAuditPaper(string content)
    {
        if (!TryFindAuditSpawnPoint(out var coords))
        {
            Log.Warning("Station Audit: no CommunicationsConsole found to spawn the audit paper near; "
                        + "skipping the physical keepsake this shift.");
            return;
        }

        var paper = Spawn(AuditPaperPrototype, coords);
        _paper.SetContent(paper, content);
    }

    private bool TryFindAuditSpawnPoint(out EntityCoordinates coords)
    {
        var query = EntityQueryEnumerator<CommunicationsConsoleComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var xform))
        {
            coords = xform.Coordinates;
            return true;
        }

        coords = default;
        return false;
    }

    /// <summary>async void + try/catch (house threading contract, SeasonLedgerSystem.OnRoundEnd
    /// idiom): a storage failure costs one ledger row, never the tick.</summary>
    private async void PersistAsync(StationAuditReport r)
    {
        // Inspection layer (gap-closure pass): snapshotted synchronously, before any await, so the
        // async continuation below never risks reading a Guid that ClearRoundState/round-restart has
        // since reset — see the field's doc comment.
        var commendationGuidForCitation = _lastCommendationGuid;

        try
        {
            var record = new StationAuditRecord(
                RoundId: r.RoundId,
                EndedAtUtc: DateTime.UtcNow.ToString("o"),
                ShiftDurationMinutes: r.ShiftDurationMinutes,
                CrewCount: r.CrewCount,
                DeathCount: r.DeathCount,
                FirstDeathCommemorated: r.FirstDeathCommemorated,
                DirectiveTitle: r.HasDirective ? r.DirectiveTitle : null,
                DirectiveOutcomeReported: r.DirectiveOutcomeReported,
                DirectiveOutcomeFulfilled: r.DirectiveOutcomeFulfilled,
                StipendsProcessed: r.StipendsProcessed,
                BountyVerdicts: r.BountyVerdicts,
                NotableEventCount: r.NotableEventCount,
                CommendationName: r.HasCommendation ? r.CommendationName : null,
                CommendationScore: r.CommendationScore,
                ItemOfConcernId: r.ItemOfConcernId,
                // Inspection layer (gap-closure pass) — "[]" / "none" / null when the layer produced
                // nothing this shift, same additive-ledger-trace idiom as every other optional column.
                CriteriaJson: SerializeCriteriaJson(r.AssignedCriteria),
                CheckpointConsequenceKind: ConsequenceKindToDbString(r.CheckpointConsequenceKind),
                CheckpointFiredUtc: _checkpointFiredAtUtc);

            var inserted = await _ledger.AppendStationAuditAsync(record);

            // nyanopark-019 fold: only cite lifetime numbers for the write that actually landed a new
            // row — never on a retried/duplicate call for a round already persisted.
            if (inserted)
                await FireLifetimeCitationAsync(r, commendationGuidForCitation);
        }
        catch (Exception e)
        {
            Log.Error($"Station Audit: failed to append ledger row for round {r.RoundId}: {e}");
        }
    }

    // --- Test seams --------------------------------------------------------------------------------

    /// <summary>Exposes the composed report for a synthetic round-end without needing to parse the
    /// rendered text back apart. Test-only.</summary>
    internal StationAuditReport ComposeForTests() => StationAuditComposer.Compose(BuildInputs());
}
