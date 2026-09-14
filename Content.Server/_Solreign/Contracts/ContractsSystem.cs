using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._Solreign.Corporate;
using Content.Server._Solreign.SeasonLedger;
using Content.Server.NameIdentifier;
using Content.Server.Station.Events;
using Content.Server.Station.Systems;
using Content.Shared._Solreign.Contracts;
using Content.Shared._Solreign.SeasonLedger;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.CCVar;
using Content.Shared.Examine;
using Content.Shared.Mind;
using Content.Shared.NameIdentifier;
using Content.Shared.Paper;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Solreign.Contracts;

/// <summary>
///     Solreign Contracts (spec Milestone 1, "The Board Works"): issues cheerfully menacing corporate work
///     orders onto every station, lets players claim them at a Contracts Board, verifies deliveries at a
///     Fulfillment Dropbox (partial: <c>ContractsSystem.Dropbox.cs</c>) and pays round-local Corporate
///     Standing on the spot. Completions are handed to the Season Ledger, which persists them at round end.
///
///     Template: the MIT upstream cargo bounty system (<c>CargoSystem.Bounty.cs</c>) — pool component per
///     station, prototype-driven contracts, whitelist matching, access-gated cooldown skip, printed work
///     orders. Deliberate divergence (spec §2.1): contracts verify on dropbox DEPOSIT, not cargo sale.
///
///     Milestone 1 surfaces are server-side only (examine + alt-click verbs; BUI messages are wired for the
///     Milestone 2 client window). Milestone 1 also gates out content the machinery can't fulfil yet:
///     Department-scope contracts (M2 contribution credit) and contracts with sabotage spawns (M2 spawner)
///     stay dormant in the prototype pool.
///
///     NEW in this fork beyond the spec's four flavors: the SALVAGE RAID category (John's mandate) —
///     group-scaled contracts where players register on a roster, and at launch both the objective size and
///     the per-head reward scale with the registered participant count (see <see cref="ContractRules"/>).
///     Teaming up always beats soloing, by construction.
/// </summary>
public sealed partial class ContractsSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private AccessReaderSystem _accessReader = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private NameIdentifierSystem _nameIdentifier = default!;
    [Dependency] private PaperSystem _paper = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private SolreignCorporateRuleSystem _corporate = default!;
    [Dependency] private SeasonLedgerSystem _ledger = default!;

    private static readonly ProtoId<NameIdentifierGroupPrototype> WorkOrderNameIdentifierGroup = "SolreignWorkOrder";

    /// <summary>Cached mirror of <see cref="CCVars.SolreignContractsQuestBoardEnabled"/> (v14 low-pop extension, quest-board spec §6). Off restores today's Contracts behavior exactly — see <see cref="EnsureLowPopEasyContract"/>.</summary>
    private bool _lowPopExtensionEnabled;

    /// <summary>Cached mirror of <see cref="CCVars.SolreignContractsLowPopThreshold"/>.</summary>
    private int _lowPopThreshold;

    public override void Initialize()
    {
        base.Initialize();

        // Threshold first so an immediate/live enable callback always reconciles against the real value.
        Subs.CVar(_cfg, CCVars.SolreignContractsLowPopThreshold, OnLowPopThresholdChanged, invokeImmediately: true);
        Subs.CVar(_cfg, CCVars.SolreignContractsQuestBoardEnabled, OnLowPopExtensionChanged, invokeImmediately: true);

        // Pop crossing the low-pop threshold downward must re-check the easy-contract invariant.
        _players.PlayerStatusChanged += OnPlayerStatusChanged;

        SubscribeLocalEvent<StationPostInitEvent>(OnStationPostInit);

        SubscribeLocalEvent<SolreignContractsBoardComponent, ExaminedEvent>(OnBoardExamined);
        SubscribeLocalEvent<SolreignContractsBoardComponent, GetVerbsEvent<AlternativeVerb>>(OnBoardGetVerbs);

        // BUI messages — no client window ships in M1, but the protocol is live for the M2 cartridge/board UI.
        SubscribeLocalEvent<SolreignContractsBoardComponent, SolreignContractClaimMessage>(OnClaimMessage);
        SubscribeLocalEvent<SolreignContractsBoardComponent, SolreignContractSkipMessage>(OnSkipMessage);
        SubscribeLocalEvent<SolreignContractsBoardComponent, SolreignRaidJoinMessage>(OnRaidJoinMessage);
        SubscribeLocalEvent<SolreignContractsBoardComponent, SolreignRaidLaunchMessage>(OnRaidLaunchMessage);

        // Deposit matching (partial: ContractsSystem.Dropbox.cs).
        InitializeDropbox();

        // Board window state pushes (partial: ContractsSystem.BoardUi.cs — the M2 client BUI).
        InitializeBoardUi();
    }

    public override void Shutdown()
    {
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
        base.Shutdown();
    }

    /// <summary>
    ///     Additive-fork idiom (mirrors <c>SolreignCorporateLayerSystem</c>): rather than editing upstream
    ///     station prototypes, ensure our pool component onto every station as it finishes initializing.
    /// </summary>
    private void OnStationPostInit(ref StationPostInitEvent ev)
    {
        var comp = EnsureComp<StationSolreignContractsComponent>(ev.Station);
        FillContracts(ev.Station, comp);
    }

    // ---------------------------------------------------------------- issuing

    /// <summary>
    ///     Contracts the Milestone 1 machinery can actually fulfil: department scope (M2 contribution
    ///     credit) and sabotage spawns (M2 spawner) stay dormant so no unfulfillable work order is issued.
    /// </summary>
    private bool IsIssuableInM1(SolreignContractPrototype proto)
    {
        return proto.Scope != SolreignContractScope.Department && proto.SabotageSpawns.Count == 0;
    }

    /// <summary>Tops the pool up to its caps, bounty <c>FillBountyDatabase</c> idiom.</summary>
    public void FillContracts(EntityUid station, StationSolreignContractsComponent? component = null)
    {
        if (!Resolve(station, ref component))
            return;

        FillScope(station, component, SolreignContractScope.Personal, component.MaxPersonal);
        FillScope(station, component, SolreignContractScope.SalvageRaid, component.MaxSalvage);

        // v14 low-pop extension (quest-board spec §2) — re-checked on every FillContracts call (station
        // post-init, and after every skip/complete, since this method is already called from all those
        // sites). No-op entirely while the extension CVar is off (EnsureLowPopEasyContract's own guard).
        EnsureLowPopEasyContract(station, component);
    }

    private void FillScope(EntityUid station, StationSolreignContractsComponent component, SolreignContractScope scope, int cap)
    {
        while (component.Contracts.Count(c => c.Scope == scope) < cap)
        {
            if (!TryIssueContract(station, component, scope))
                break;
        }
    }

    private bool TryIssueContract(EntityUid station, StationSolreignContractsComponent component, SolreignContractScope scope)
    {
        var all = _proto.EnumeratePrototypes<SolreignContractPrototype>()
            .Where(p => p.Scope == scope && IsIssuableInM1(p))
            .ToList();

        if (all.Count == 0)
            return false;

        // Prefer contracts not already on the board; allow duplicates only when everything is taken
        // (bounty pool idiom).
        var fresh = all.Where(p => component.Contracts.All(c => c.Prototype != p.ID)).ToList();
        var pool = fresh.Count == 0 ? all : fresh;
        var proto = _random.Pick(pool);

        return IssueContract(station, component, proto);
    }

    /// <summary>
    ///     Builds and appends one active contract from <paramref name="proto"/> — the shared tail end of
    ///     both the normal pool top-up (<see cref="TryIssueContract"/>) and the v14 low-pop guarantee
    ///     (<see cref="EnsureLowPopEasyContract"/>), split out so the id-generation/append logic isn't
    ///     duplicated between the two callers.
    /// </summary>
    private bool IssueContract(EntityUid station, StationSolreignContractsComponent component, SolreignContractPrototype proto)
    {
        _nameIdentifier.GenerateUniqueNameModifier(WorkOrderNameIdentifierGroup, out var number);
        var id = $"{proto.IdPrefix}{number:D3}";
        if (component.Contracts.Any(c => c.Id == id))
        {
            Log.Error($"Failed to issue contract {proto.ID}: work-order id {id} already exists.");
            return false;
        }

        var required = proto.Entries.Select(e => Math.Max(1, e.Amount)).ToArray();
        component.Contracts.Add(new SolreignActiveContract
        {
            Id = id,
            Prototype = proto.ID,
            Scope = proto.Scope,
            Required = required,
            Progress = new int[required.Length],
            StandingPerHead = Math.Max(0, proto.Standing),
        });
        component.TotalIssued++;
        return true;
    }

    /// <summary>
    ///     v14 low-pop extension (quest-board spec §2): at/under <see cref="_lowPopThreshold"/> connected
    ///     players, keeps one OPEN (unclaimed) EasyTier contract available while the bounded Personal
    ///     pool has room. At MaxPersonal+2 it deliberately waits for an active claim to resolve.
    ///     Force-issues past <see cref="StationSolreignContractsComponent.MaxPersonal"/> if the cap is
    ///     already full of non-easy content. One claimed easy plus its open replacement may occupy the
    ///     two bounded exception slots; the hard ceiling is MaxPersonal+2.
    ///
    ///     When re-arming at MaxPersonal+1, the insertion-oldest open non-easy is retired only after
    ///     replacement issue succeeds. That maintenance removal deliberately bypasses payout, Standing,
    ///     streak, Ledger, and skip-cooldown paths. No verification-path change: guaranteed work still
    ///     resolves through the same <see cref="TryClaim"/>/dropbox path.
    /// </summary>
    private bool EnsureLowPopEasyContract(EntityUid station, StationSolreignContractsComponent component)
    {
        if (!_lowPopExtensionEnabled)
            return false;

        var personalCount = 0;
        var claimedEasyCount = 0;
        var poolHasEasyTierOpen = false;
        foreach (var c in component.Contracts)
        {
            if (c.Scope != SolreignContractScope.Personal)
                continue;

            // ACTIVE count: claimed + open. Claimed still occupy the bounded MaxPersonal+2 budget.
            personalCount++;
            if (!_proto.TryIndex(c.Prototype, out var contractProto) || !contractProto.EasyTier)
                continue;

            if (c.Claimant is null)
                poolHasEasyTierOpen = true;
            else
                claimedEasyCount++;
        }

        if (!ContractRules.NeedsGuaranteedEasyContract(
                _players.PlayerCount,
                _lowPopThreshold,
                poolHasEasyTierOpen,
                personalCount,
                component.MaxPersonal))
            return false;

        var easyPool = _proto.EnumeratePrototypes<SolreignContractPrototype>()
            .Where(p => p.Scope == SolreignContractScope.Personal && p.EasyTier && IsIssuableInM1(p))
            .ToList();

        if (easyPool.Count == 0)
        {
            if (ContractRules.ShouldLogMissingEasyContent(component.LowPopEasyContentGapLogged))
            {
                component.LowPopEasyContentGapLogged = true;
                Log.Error($"Low-pop contract guarantee on station {ToPrettyString(station)} has no issuable EasyTier content.");
            }

            return false;
        }

        // Prefer the normal MaxPersonal+1 shape for the first unresolved easy claim. A guarantee chain
        // may retire at most one normal row; its next re-arm uses the explicit MaxPersonal+2 slot so
        // repeated claims cannot silently drain the normal pool. Claimed rows are never retired.
        SolreignActiveContract? replace = null;
        if (personalCount >= component.MaxPersonal + 1 && claimedEasyCount == 1)
        {
            replace = component.Contracts.FirstOrDefault(c =>
                c.Scope == SolreignContractScope.Personal
                && c.Claimant is null
                && _proto.TryIndex(c.Prototype, out var candidateProto)
                && !candidateProto.EasyTier);
        }

        // Issue first so a collision/failure leaves the existing board rows intact. Name generation
        // may consume an identifier before append, so this atomicity claim is deliberately board-scoped.
        // Remove the candidate only after successful issue.
        if (!IssueContract(station, component, _random.Pick(easyPool)))
            return false;

        if (replace is not null)
            component.Contracts.Remove(replace);

        return true;
    }

    private void OnLowPopExtensionChanged(bool enabled)
    {
        _lowPopExtensionEnabled = enabled;
        if (enabled)
            ReconcileLowPopStations();
    }

    private void OnLowPopThresholdChanged(int threshold)
    {
        _lowPopThreshold = threshold;
        if (_lowPopExtensionEnabled)
            ReconcileLowPopStations();
    }

    private void ReconcileLowPopStations()
    {
        if (!_lowPopExtensionEnabled || _players.PlayerCount > _lowPopThreshold)
            return;

        var query = EntityQueryEnumerator<StationSolreignContractsComponent>();
        while (query.MoveNext(out var station, out var comp))
        {
            if (EnsureLowPopEasyContract(station, comp))
                UpdateBoards(station, comp);
        }
    }

    /// <summary>
    ///     When connected pop crosses at/under the low-pop threshold (join or leave), re-run the easy
    ///     guarantee on every station so the open-EasyTier invariant is restored without waiting for a
    ///     skip/complete cycle.
    /// </summary>
    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        ReconcileLowPopStations();
    }

    // ---------------------------------------------------------------- board surfaces (M1: examine + verbs)

    private void OnBoardExamined(EntityUid uid, SolreignContractsBoardComponent component, ExaminedEvent args)
    {
        if (_station.GetOwningStation(uid) is not { } station ||
            !TryComp<StationSolreignContractsComponent>(station, out var db))
            return;

        args.PushMarkup(Loc.GetString("solreign-contracts-board-examine-header"));

        if (db.Contracts.Count == 0)
        {
            args.PushMarkup(Loc.GetString("solreign-contracts-board-examine-empty"));
            return;
        }

        foreach (var contract in db.Contracts)
        {
            if (!_proto.TryIndex(contract.Prototype, out var proto))
                continue;

            var status = ContractStatusLine(contract);
            args.PushMarkup(Loc.GetString("solreign-contracts-board-examine-entry",
                ("id", contract.Id),
                ("name", Loc.GetString(proto.Name)),
                ("standing", contract.StandingPerHead),
                ("status", status)));
        }
    }

    private string ContractStatusLine(SolreignActiveContract contract)
    {
        return contract.Scope switch
        {
            SolreignContractScope.SalvageRaid when !contract.Launched =>
                Loc.GetString("solreign-contracts-status-recruiting", ("count", contract.Participants.Count)),
            SolreignContractScope.SalvageRaid =>
                Loc.GetString("solreign-contracts-status-raid-launched",
                    ("count", contract.Participants.Count),
                    ("done", contract.Progress.Sum()),
                    ("total", contract.Required.Sum())),
            _ when contract.Claimant is null =>
                Loc.GetString("solreign-contracts-status-open"),
            _ =>
                Loc.GetString("solreign-contracts-status-claimed",
                    ("claimant", contract.ClaimantName ?? string.Empty),
                    ("done", contract.Progress.Sum()),
                    ("total", contract.Required.Sum())),
        };
    }

    private void OnBoardGetVerbs(EntityUid uid, SolreignContractsBoardComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        if (_station.GetOwningStation(uid) is not { } station ||
            !TryComp<StationSolreignContractsComponent>(station, out var db))
            return;

        var user = args.User;
        var priority = 10;

        foreach (var contract in db.Contracts)
        {
            if (!_proto.TryIndex(contract.Prototype, out var proto))
                continue;

            var contractId = contract.Id;
            var name = Loc.GetString(proto.Name);

            switch (contract.Scope)
            {
                case SolreignContractScope.Personal when contract.Claimant is null:
                    args.Verbs.Add(new AlternativeVerb
                    {
                        Text = Loc.GetString("solreign-contracts-verb-claim", ("name", name), ("id", contractId)),
                        Priority = priority,
                        Act = () => TryClaim(uid, component, station, contractId, user),
                    });
                    break;

                case SolreignContractScope.SalvageRaid when !contract.Launched:
                    args.Verbs.Add(new AlternativeVerb
                    {
                        Text = Loc.GetString("solreign-contracts-verb-join", ("name", name), ("id", contractId)),
                        Priority = priority,
                        Act = () => TryJoinRaid(uid, component, station, contractId, user),
                    });
                    args.Verbs.Add(new AlternativeVerb
                    {
                        Text = Loc.GetString("solreign-contracts-verb-launch", ("name", name), ("id", contractId)),
                        Priority = priority - 1,
                        Act = () => TryLaunchRaid(uid, component, station, contractId, user),
                    });
                    break;
            }

            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("solreign-contracts-verb-skip", ("name", name), ("id", contractId)),
                Priority = priority - 2,
                Act = () => TrySkip(uid, component, station, contractId, user),
            });

            priority -= 3;
        }
    }

    // ---------------------------------------------------------------- BUI message handlers (M2 protocol, live now)

    private void OnClaimMessage(EntityUid uid, SolreignContractsBoardComponent component, SolreignContractClaimMessage args)
    {
        if (args.Actor is { Valid: true } actor && _station.GetOwningStation(uid) is { } station)
            TryClaim(uid, component, station, args.ContractId, actor);
    }

    private void OnSkipMessage(EntityUid uid, SolreignContractsBoardComponent component, SolreignContractSkipMessage args)
    {
        if (args.Actor is { Valid: true } actor && _station.GetOwningStation(uid) is { } station)
            TrySkip(uid, component, station, args.ContractId, actor);
    }

    private void OnRaidJoinMessage(EntityUid uid, SolreignContractsBoardComponent component, SolreignRaidJoinMessage args)
    {
        if (args.Actor is { Valid: true } actor && _station.GetOwningStation(uid) is { } station)
            TryJoinRaid(uid, component, station, args.ContractId, actor);
    }

    private void OnRaidLaunchMessage(EntityUid uid, SolreignContractsBoardComponent component, SolreignRaidLaunchMessage args)
    {
        if (args.Actor is { Valid: true } actor && _station.GetOwningStation(uid) is { } station)
            TryLaunchRaid(uid, component, station, args.ContractId, actor);
    }

    // ---------------------------------------------------------------- claim / join / launch / skip

    private void TryClaim(EntityUid board, SolreignContractsBoardComponent boardComp, EntityUid station, string contractId, EntityUid user)
    {
        if (!TryComp<StationSolreignContractsComponent>(station, out var db) ||
            !TryGetContract(db, contractId, out var contract))
            return;

        if (!_proto.TryIndex(contract.Prototype, out var proto))
        {
            Deny(board, boardComp, user, "DATABASE ERROR: FILE CORRUPTED.");
            return;
        }

        if (contract.Scope != SolreignContractScope.Personal || contract.Claimant != null)
        {
            Deny(board, boardComp, user, Loc.GetString("solreign-contracts-popup-claim-taken"));
            return;
        }

        if (!TryGetUser(user, out var guid, out var name))
        {
            // P3.2 OTHER SILENT DROPS (audit fix 3 of 4) — no-mind fall-through on the Claim path.
            // The Deny() helper covers ~10 popup paths across claim/join/raid, but the TryGetUser
            // branch fell through to bare return — a player who pressed Claim while mid-ghost got
            // nothing. Same fallback key used by TryJoinRaid and TrySkip below; matches the
            // sinisters-utility tone the rest of the contracts voice already uses.
            _popup.PopupEntity(Loc.GetString("solreign-contracts-popup-no-mind"), board, user);
            return;
        }

        // Rank gate (spec §3.1/§3.4, M3): some contracts only open at/above a career rank. The rank rides
        // the mob on SeasonTitleComponent, stamped at spawn by the Season Ledger. Pure check lives in
        // ContractRules so it is unit-testable and shared with the salvage-raid join gate below.
        var rankIndex = TryComp<SeasonTitleComponent>(user, out var title) ? title.RankIndex : 0;
        if (!ContractRules.MeetsRankGate(rankIndex, proto.MinRankIndex))
        {
            Deny(board, boardComp, user, Loc.GetString("solreign-contracts-popup-claim-rank"));
            return;
        }

        // Anti-grief rule 7: max 3 claimed personal contracts per player.
        var claims = db.Contracts.Count(c => c.Scope == SolreignContractScope.Personal && c.Claimant == guid);
        if (!ContractRules.CanClaimAnother(claims))
        {
            Deny(board, boardComp, user, Loc.GetString("solreign-contracts-popup-claim-cap",
                ("cap", ContractRules.MaxClaimedPerPlayer)));
            return;
        }

        contract.Claimant = guid;
        contract.ClaimantName = name;

        // Claim of the sole open easy must re-check the low-pop invariant immediately (not only on
        // skip/complete). CVar-gated; re-arms only within the MaxPersonal+2 hard bound, and at most
        // one unresolved guarantee claim may retire an open non-easy row.
        EnsureLowPopEasyContract(station, db);

        PrintWorkOrder(board, boardComp, contract, proto);
        _popup.PopupEntity(Loc.GetString("solreign-contracts-popup-claimed", ("id", contract.Id)), board, user);
        UpdateBoards(station, db);
    }

    private void TryJoinRaid(EntityUid board, SolreignContractsBoardComponent boardComp, EntityUid station, string contractId, EntityUid user)
    {
        if (!TryComp<StationSolreignContractsComponent>(station, out var db) ||
            !TryGetContract(db, contractId, out var contract))
            return;

        if (!_proto.TryIndex(contract.Prototype, out var proto))
        {
            Deny(board, boardComp, user, "DATABASE ERROR: FILE CORRUPTED.");
            return;
        }

        if (contract.Scope != SolreignContractScope.SalvageRaid || contract.Launched)
        {
            Deny(board, boardComp, user, Loc.GetString("solreign-contracts-popup-raid-closed"));
            return;
        }

        if (!TryGetUser(user, out var guid, out var name))
        {
            // P3.2 OTHER SILENT DROPS (audit fix 3 of 4) — no-mind fall-through on the JoinRaid
            // path. Same fallback as the Claim path; a single shared key covers the three sites
            // (Claim/JoinRaid/Skip) the audit flagged.
            _popup.PopupEntity(Loc.GetString("solreign-contracts-popup-no-mind"), board, user);
            return;
        }

        // Rank gate (spec §3.1/§3.4, M3): a rank-gated raid (minRankIndex > 0) is gated on JOIN, not just
        // launch — a Probationary Asset should never be able to register on a Manager-tier raid roster in
        // the first place. Same pure check as the personal-claim gate above (ContractRules.MeetsRankGate).
        var rankIndex = TryComp<SeasonTitleComponent>(user, out var title) ? title.RankIndex : 0;
        if (!ContractRules.MeetsRankGate(rankIndex, proto.MinRankIndex))
        {
            Deny(board, boardComp, user, Loc.GetString("solreign-contracts-popup-claim-rank"));
            return;
        }

        if (contract.Participants.ContainsKey(guid))
        {
            _popup.PopupEntity(Loc.GetString("solreign-contracts-popup-raid-already"), board, user);
            return;
        }

        if (contract.Participants.Count >= Math.Max(1, proto.MaxParticipants))
        {
            Deny(board, boardComp, user, Loc.GetString("solreign-contracts-popup-raid-full"));
            return;
        }

        contract.Participants[guid] = name;
        _popup.PopupEntity(Loc.GetString("solreign-contracts-popup-raid-joined",
            ("id", contract.Id),
            ("count", contract.Participants.Count)), board, user);
        UpdateBoards(station, db);
    }

    private void TryLaunchRaid(EntityUid board, SolreignContractsBoardComponent boardComp, EntityUid station, string contractId, EntityUid user)
    {
        if (!TryComp<StationSolreignContractsComponent>(station, out var db) ||
            !TryGetContract(db, contractId, out var contract))
            return;

        if (!_proto.TryIndex(contract.Prototype, out var proto))
        {
            Deny(board, boardComp, user, "DATABASE ERROR: FILE CORRUPTED.");
            return;
        }

        if (contract.Scope != SolreignContractScope.SalvageRaid || contract.Launched)
            return;

        if (!TryGetUser(user, out var guid, out _))
            return;

        if (!contract.Participants.ContainsKey(guid))
        {
            Deny(board, boardComp, user, Loc.GetString("solreign-contracts-popup-raid-not-member"));
            return;
        }

        // Accept-time group scaling (John's salvage-raid mandate): objectives AND per-head reward lock to
        // the roster registered right now. Linear objectives keep per-head workload flat; the per-head
        // standing bonus makes every extra teammate pure upside. Pure math in ContractRules (unit-tested).
        var crew = contract.Participants.Count;
        for (var i = 0; i < contract.Required.Length; i++)
        {
            contract.Required[i] = ContractRules.SalvageScaledAmount(
                proto.Entries[i].Amount, crew, proto.MaxParticipants);
        }

        contract.StandingPerHead = ContractRules.SalvageStanding(proto.Standing, crew, proto.MaxParticipants);
        contract.Launched = true;

        PrintWorkOrder(board, boardComp, contract, proto);
        _popup.PopupEntity(Loc.GetString("solreign-contracts-popup-raid-launched",
            ("id", contract.Id),
            ("count", crew),
            ("standing", contract.StandingPerHead)), board, user);
        UpdateBoards(station, db);
    }

    private void TrySkip(EntityUid board, SolreignContractsBoardComponent boardComp, EntityUid station, string contractId, EntityUid user)
    {
        if (!TryComp<StationSolreignContractsComponent>(station, out var db) ||
            !TryGetContract(db, contractId, out var contract))
            return;

        // Anti-grief rule 6: skip is free but station-wide cooldown-gated.
        if (_timing.CurTime < db.NextSkipTime)
        {
            Deny(board, boardComp, user, Loc.GetString("solreign-contracts-popup-skip-cooldown"));
            return;
        }

        // A claimed contract belongs to its claimant; a launched raid belongs to its roster. Nobody else
        // can skip someone's work out from under them (anti-grief: no denial via skip).
        if (!TryGetUser(user, out var guid, out _))
        {
            // P3.2 OTHER SILENT DROPS (audit fix 3 of 4) — no-mind fall-through on the Skip path.
            // Same fallback as the Claim and JoinRaid paths; shared key, no behavior split.
            _popup.PopupEntity(Loc.GetString("solreign-contracts-popup-no-mind"), board, user);
            return;
        }

        var owned = contract.Claimant != null || contract.Participants.Count > 0;
        if (owned && contract.Claimant != guid && !contract.Participants.ContainsKey(guid))
        {
            Deny(board, boardComp, user, Loc.GetString("solreign-contracts-popup-skip-not-yours"));
            return;
        }

        // Unclaimed contracts skip through the board's access reader, bounty idiom (dept-gated boards).
        if (!owned &&
            TryComp<AccessReaderComponent>(board, out var accessReader) &&
            !_accessReader.IsAllowed(user, board, accessReader))
        {
            Deny(board, boardComp, user, Loc.GetString("solreign-contracts-popup-skip-access"));
            return;
        }

        db.Contracts.Remove(contract);
        db.NextSkipTime = _timing.CurTime + db.SkipDelay;
        FillContracts(station, db);

        _audio.PlayPvs(boardComp.SkipSound, board);
        _popup.PopupEntity(Loc.GetString("solreign-contracts-popup-skipped", ("id", contract.Id)), board, user);
        UpdateBoards(station, db);
    }

    // ---------------------------------------------------------------- completion / payout

    /// <summary>
    ///     Pays a completed contract out on the spot (spec §3.3 "instant ka-ching"): Corporate Standing per
    ///     head through the existing Corporate Ladder pipeline, plus a completion record to the Season
    ///     Ledger (persisted at round end). Spesos stay dormant in M1 (spec §10.5). Removes the contract
    ///     and refills the pool. Chain auto-issue is Milestone 2; a chain link still pays out normally.
    ///
    ///     <paramref name="depositor"/> — the account that physically deposited the completing item —
    ///     already gets TryDeposit's own "contract-complete" popup at the dropbox, so it is skipped here
    ///     to avoid double-messaging. Every OTHER salvage-raid teammate currently has NO deposit of their
    ///     own to react to: without this, they bank Corporate Standing in total silence (Phase2 A4
    ///     game-feel sweep) — this pings each of them individually wherever they currently stand.
    /// </summary>
    private void CompleteContract(EntityUid station, StationSolreignContractsComponent db, SolreignActiveContract contract, Guid? depositor = null)
    {
        if (!_proto.TryIndex(contract.Prototype, out var proto))
            return;

        var chainFinal = ContractRules.IsChainFinal(proto.NextContract != null, IsChainTarget(proto.ID));
        var score = ContractRules.CompletionScore(contract.Scope, chainFinal);
        var scopeLabel = ScopeLabel(contract.Scope);

        foreach (var (guid, name) in CompletionBeneficiaries(contract))
        {
            _corporate.AwardStanding(new NetUserId(guid), contract.StandingPerHead, name);
            _ledger.SubmitContractCompletion(guid, proto.ID, scopeLabel, score);
            _ledger.AwardHrPoints(guid, contract.StandingPerHead);

            if (guid == depositor)
                continue;

            if (_players.TryGetSessionById(new NetUserId(guid), out var session) && session.AttachedEntity is { } mob)
            {
                // UX-SIMPLE FIX 4: routed through the shared award-popup helper so this reads in
                // the same "+N Standing — [reason]" voice as every other Standing confirmation
                // across the Solreign feature set, rather than a one-off bespoke string.
                Content.Server._Solreign.Notifications.SolreignAwardPopup.Show(_popup, mob, contract.StandingPerHead,
                    Loc.GetString("solreign-contracts-award-reason-raid", ("id", contract.Id)));
            }
        }

        db.Contracts.Remove(contract);
        FillContracts(station, db);
        UpdateBoards(station, db);
    }

    /// <summary>Whose completion is this? Personal: the claimant. Salvage raid: every registered participant.</summary>
    private static IEnumerable<(Guid Guid, string Name)> CompletionBeneficiaries(SolreignActiveContract contract)
    {
        if (contract.Scope == SolreignContractScope.SalvageRaid)
        {
            foreach (var (guid, name) in contract.Participants)
                yield return (guid, name);
            yield break;
        }

        if (contract.Claimant is { } claimant)
            yield return (claimant, contract.ClaimantName ?? string.Empty);
    }

    /// <summary>The contract_log scope label (spec §4.2: 'personal' | 'department', extended with 'salvage-raid').</summary>
    private static string ScopeLabel(SolreignContractScope scope) => scope switch
    {
        SolreignContractScope.Department => "department",
        SolreignContractScope.SalvageRaid => "salvage-raid",
        _ => "personal",
    };

    /// <summary>Is any contract prototype chaining INTO this one? (Chain-final scoring, spec §4.1.)</summary>
    private bool IsChainTarget(string protoId)
    {
        foreach (var p in _proto.EnumeratePrototypes<SolreignContractPrototype>())
        {
            if (p.NextContract is { } next && next.Id == protoId)
                return true;
        }

        return false;
    }

    // ---------------------------------------------------------------- helpers

    private static bool TryGetContract(StationSolreignContractsComponent db, string contractId,
        out SolreignActiveContract contract)
    {
        foreach (var c in db.Contracts)
        {
            if (c.Id != contractId)
                continue;
            contract = c;
            return true;
        }

        contract = default!;
        return false;
    }

    /// <summary>Resolves a mob to its account guid + display name via its mind (the ledger's key idiom).</summary>
    private bool TryGetUser(EntityUid mob, out Guid guid, out string name)
    {
        guid = default;
        name = string.Empty;

        if (!_mind.TryGetMind(mob, out _, out var mind) || mind.UserId is not { } userId)
            return false;

        guid = userId.UserId;
        name = MetaData(mob).EntityName;
        return true;
    }

    /// <summary>Deny feedback: popup + cooldown-gated buzz (bounty console deny idiom).</summary>
    private void Deny(EntityUid board, SolreignContractsBoardComponent boardComp, EntityUid user, string message)
    {
        _popup.PopupEntity(message, board, user);

        if (_timing.CurTime < boardComp.NextDenySoundTime)
            return;

        boardComp.NextDenySoundTime = _timing.CurTime + boardComp.DenySoundDelay;
        _audio.PlayPvs(boardComp.DenySound, board);
    }

    /// <summary>Prints a work-order paper at the board (bounty <c>SetupBountyLabel</c> manifest idiom).</summary>
    private void PrintWorkOrder(EntityUid board, SolreignContractsBoardComponent boardComp,
        SolreignActiveContract contract, SolreignContractPrototype proto)
    {
        if (_timing.CurTime < boardComp.NextPrintTime)
            return;

        var paper = Spawn(boardComp.WorkOrderPaperId, Transform(board).Coordinates);
        boardComp.NextPrintTime = _timing.CurTime + boardComp.PrintDelay;

        if (TryComp<PaperComponent>(paper, out var paperComp))
        {
            var msg = new FormattedMessage();
            msg.AddMarkupOrThrow(Loc.GetString("solreign-contracts-work-order-header", ("id", contract.Id)));
            msg.PushNewline();
            msg.AddMarkupOrThrow(Loc.GetString("solreign-contracts-work-order-title",
                ("name", Loc.GetString(proto.Name))));
            msg.PushNewline();
            if (!string.IsNullOrEmpty(proto.Description))
            {
                msg.AddMarkupOrThrow(Loc.GetString(proto.Description));
                msg.PushNewline();
            }
            msg.AddMarkupOrThrow(Loc.GetString("solreign-contracts-work-order-list-start"));
            msg.PushNewline();
            for (var i = 0; i < proto.Entries.Count && i < contract.Required.Length; i++)
            {
                msg.AddMarkupOrThrow($"- {Loc.GetString("solreign-contracts-work-order-entry",
                    ("amount", contract.Required[i]),
                    ("item", Loc.GetString(proto.Entries[i].Name)))}");
                msg.PushNewline();
            }
            msg.AddMarkupOrThrow(Loc.GetString("solreign-contracts-work-order-reward",
                ("standing", contract.StandingPerHead)));
            msg.PushNewline();
            msg.AddMarkupOrThrow(Loc.GetString("solreign-contracts-work-order-footer"));

            _paper.SetContent((paper, paperComp), msg.ToMarkup());
        }

        _audio.PlayPvs(boardComp.PrintSound, board);
    }
}
