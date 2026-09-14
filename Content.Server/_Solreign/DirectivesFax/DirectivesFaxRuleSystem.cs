using Content.Server._Solreign.DirectivesFax.Components;
using Content.Server._Solreign.SeasonLedger;
using Content.Server._Solreign.StationDirective;
using Content.Server.Cargo.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Chat.Managers;
using Content.Server.Fax;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.Station.Systems;
using Content.Shared.Cargo.Components;
using Content.Shared.GameTicking.Components;
using Content.Shared.Mind;
using Content.Shared.Popups;
using Content.Shared.Station.Components;
using Robust.Server.Player;
using Robust.Shared.Timing;

namespace Content.Server._Solreign.DirectivesFax;

/// <summary>
///     "Directives Fax" (v14 wave-1 item #1, council C1 einstein-001 "ship first"). Clean-room build
///     from Einstein Engines' public description of Station Goals — no EE code was read; every idiom
///     below (game-rule split, per-round Dictionary trackers, write-before-dispatch ledger writes) is
///     copied from SOLREIGN's own existing features (StationDirective, the Corporate Ladder, Social
///     Firsts, Early Death). See the receipt (<c>docs/receipts/DIRECTIVES-FAX-2026-07-17.md</c>) for
///     the full clean-room provenance note and design rationale.
///
///     A layerable, additive game rule (started by <see cref="DirectivesFaxLayerSystem"/>, same
///     zero-core-edit idiom as every other Solreign game rule) that, at round start:
///       1. Independently replays <c>StationDirectiveSelection.SelectDirectiveIndex(GameTicker.RoundId,
///          StationDirectiveCatalog.Directives.Count)</c> — the SAME pure, deterministic function the
///          live <c>StationDirectiveRuleSystem</c> uses — to learn which Corporate Directive is live
///          this shift, WITHOUT reading that system's <c>Access</c>-restricted component and WITHOUT
///          any cross-system start-order dependency (both rules are started independently off
///          <c>RoundStartingEvent</c> by their own layer systems; two separate subscribers to the same
///          event have no guaranteed relative order in RobustToolbox, so a live-component read would be
///          a race — replaying the pure selection function sidesteps that entirely, since it depends on
///          nothing but the round id and the fixed catalog count, both available immediately).
///       2. Looks up 1-3 completable clauses for that directive (<see cref="DirectivesFaxClauseCatalog"/>).
///       3. Snapshots the station's current Cargo revenue and supply-order count (round-start baseline
///          for the delta clauses — see <see cref="DirectivesFaxRuleComponent"/>).
///       4. Prints a physical fax with the directive text + clause list (see
///          <c>DirectivesFaxRuleSystem.Print.cs</c> for the print-point decision).
///     At round end (<see cref="AppendRoundEndText"/>) it evaluates every clause against the shift's
///     tracked state (cheap: a handful of already-tracked counters, no per-tick scan — see
///     <c>DirectivesFaxRuleSystem.Tracking.cs</c>), appends a compliance-report block to the round-end
///     summary, and — for every account present at least <see cref="PresenceThreshold"/> this shift —
///     updates that account's persistent compliance streak in the Season Ledger (write-before-dispatch:
///     the ledger write is awaited before any milestone popup/chat fires).
///
///     Entirely dormant while <c>CCVars.SolreignDirectivesFaxEnabled</c> is false (the default — ships
///     off per council precondition): <see cref="DirectivesFaxLayerSystem"/> simply never starts this
///     rule, so nothing in this file ever runs.
/// </summary>
public sealed partial class DirectivesFaxRuleSystem : GameRuleSystem<DirectivesFaxRuleComponent>
{
    [Dependency] private SeasonLedgerSystem _ledger = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IChatManager _chatManager = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private CargoSystem _cargo = default!;
    [Dependency] private FaxSystem _fax = default!;

    public override void Initialize()
    {
        base.Initialize();

        InitializeTracking();
        InitializeReport();
    }

    protected override void Started(EntityUid uid, DirectivesFaxRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        var directiveIndex = StationDirectiveSelection.SelectDirectiveIndex(GameTicker.RoundId, StationDirectiveCatalog.Directives.Count);
        var directive = StationDirectiveCatalog.Directives[directiveIndex];
        component.DirectiveId = directive.Id;
        component.FaxPrinted = false;

        SnapshotRoundStartEconomy(component);
        PrintDirectiveFax(component, directive);
    }

    /// <summary>Reads the station's current Cargo balance + supply-order count as this shift's baseline
    /// for the two economy-delta clause kinds. Zero if no station/bank/order-database resolves (e.g. a
    /// content-integrity test map) — the delta clauses then simply compare against 0, never crash.</summary>
    private void SnapshotRoundStartEconomy(DirectivesFaxRuleComponent component)
    {
        if (!TryGetPrimaryStation(out var station))
            return;

        if (TryComp<StationBankAccountComponent>(station, out var bank))
            component.RoundStartCargoBalance = _cargo.GetBalanceFromAccount((station, bank), "Cargo");

        if (TryComp<StationCargoOrderDatabaseComponent>(station, out var orderDb))
            component.RoundStartSupplyOrders = orderDb.NumOrdersCreated;
    }

    /// <summary>Resolves the single station SOLREIGN rounds run on. Mirrors
    /// <c>StationSystem</c>'s own all-stations enumerator — used instead of
    /// <c>StationSystem.GetOwningStation</c> because the game-rule entity itself has no grid/transform
    /// to anchor from.</summary>
    private bool TryGetPrimaryStation(out EntityUid station)
    {
        var query = EntityQueryEnumerator<StationDataComponent>();
        if (query.MoveNext(out var uid, out _))
        {
            station = uid;
            return true;
        }

        station = default;
        return false;
    }

    protected override void AppendRoundEndText(EntityUid uid, DirectivesFaxRuleComponent component, GameRuleComponent gameRule, ref RoundEndTextAppendEvent args)
    {
        base.AppendRoundEndText(uid, component, gameRule, ref args);

        // RoundEndReportPrinted (not OutcomeComputed) is the guard here: OutcomeComputed may already be
        // true by the time this runs, if Station Audits' own RoundEndTextAppendEvent subscriber fired
        // first and raised SolreignDirectiveOutcomeQueryEvent (DirectivesFaxRuleSystem.Report.cs) —
        // that only caches the outcome bool, it never produces this round's text/paper. See
        // DirectivesFaxRuleComponent.RoundEndReportPrinted's doc comment.
        if (component.RoundEndReportPrinted || component.DirectiveId is not { } directiveId)
            return;

        component.RoundEndReportPrinted = true;

        var met = ComputeOrGetOutcome(component, directiveId);
        var clauses = DirectivesFaxClauseCatalog.GetClauses(directiveId);
        var state = GatherShiftState(component);
        var displayName = DirectivesFaxClauseCatalog.GetDisplayName(directiveId);

        args.AddLine(Loc.GetString("solreign-directives-fax-round-end-header"));
        args.AddLine(Loc.GetString(
            met ? "solreign-directives-fax-round-end-met" : "solreign-directives-fax-round-end-unmet",
            ("directive", displayName)));

        foreach (var clause in clauses)
        {
            var passed = DirectivesFaxClauseEvaluation.EvaluateClause(clause, state);
            var (locKey, locArgs) = DirectivesFaxClauseFormatting.Describe(clause);
            var clauseText = Loc.GetString(locKey, locArgs);
            args.AddLine(Loc.GetString(
                passed ? "solreign-directives-fax-round-end-clause-met" : "solreign-directives-fax-round-end-clause-unmet",
                ("clause", clauseText)));
        }

        args.AddLine("");

        // Async streak persistence + milestone notification (DirectivesFaxRuleSystem.Print.cs). Kicked
        // off, not awaited — AppendRoundEndText cannot be async (ref struct event arg). The eligible-
        // account snapshot inside is taken synchronously, before any await (the SolreignSocialFirstsSystem
        // "mark/snapshot before the first await" idiom).
        UpdateStreaksAndNotify(directiveId, met, GameTicker.RoundId);

        // Stamped/shareable end-of-shift report paper (DirectivesFaxRuleSystem.Report.cs) — the gap
        // this file's existing round-end text block alone did not close. Skipped entirely if no station
        // resolves at all (e.g. a bare content-integrity test map) — same defensive posture as
        // PrintDirectiveFax's own station-resolution guard.
        if (TryGetPrimaryStation(out var station))
            SpawnComplianceReportPaper(station, directiveId, displayName, clauses, state);
    }

    /// <summary>Gathers the cheap, already-tracked shift state clauses evaluate against. One station
    /// lookup + two per-round Dictionary/HashSet reads — no per-tick scan, no per-entity sweep.</summary>
    private DirectivesFaxShiftState GatherShiftState(DirectivesFaxRuleComponent component)
    {
        var cargoDelta = 0;
        var ordersDelta = 0;

        if (TryGetPrimaryStation(out var station))
        {
            if (TryComp<StationBankAccountComponent>(station, out var bank))
                cargoDelta = _cargo.GetBalanceFromAccount((station, bank), "Cargo") - component.RoundStartCargoBalance;

            if (TryComp<StationCargoOrderDatabaseComponent>(station, out var orderDb))
                ordersDelta = orderDb.NumOrdersCreated - component.RoundStartSupplyOrders;
        }

        return new DirectivesFaxShiftState(CrewDeaths: DeadThisShiftCount, CargoRevenueDelta: cargoDelta, SupplyOrdersDelta: ordersDelta);
    }
}
