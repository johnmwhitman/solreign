using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.Shared._Solreign.SeasonLedger;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Robust.Shared.ContentPack;

namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     ECS glue for the Season Ledger — the fork's cross-round memory. Persists per-account results at
///     round end and stamps an earned title (from <see cref="TitleRules"/>) on players at spawn, surfaced
///     on examine.
///
///     Threading contract (mirrors <c>GameTicker.SendRoundEndDiscordMessage</c>): all SQLite I/O runs on
///     <see cref="async"/> handlers with try/catch + <c>Log.Error</c> so it never blocks the tick, and
///     entities are only ever mutated on the main thread — async loads enqueue results that
///     <see cref="Update"/> drains.
/// </summary>
public sealed partial class SeasonLedgerSystem : EntitySystem
{
    [Dependency] private IResourceManager _res = default!;

    private SeasonLedgerStore _store = default!;

    // Async title loads push here; drained on the main thread in Update so we never touch entities off-thread.
    private readonly ConcurrentQueue<PendingTitle> _pending = new();
    private readonly ConcurrentQueue<LedgerRecoveryHealthSnapshot> _pendingRecoveryHealth = new();
    private readonly SeasonLedgerRecoveryScheduler _recoveryScheduler = new();

    // Title is what examine displays (an admin-granted title masks the earned one — see
    // SeasonLedgerSystem.TitleGrants.cs); CeremonyTitle is always the EARNED title, so an HR bulletin
    // never announces an admin mask as though it were earned.
    private readonly record struct PendingTitle(EntityUid Mob, Guid User, long Generation, string Title, string CeremonyTitle, int Tours, string Rank, int RankIndex, int HrPoints, bool Ceremony, bool HasAdminGrant);

    // Monotonic per-account load generation. Bumped at the START of every LoadTitle (on the main thread,
    // before its first await) so Update can discard results of loads that were superseded while in flight.
    private readonly ConcurrentDictionary<Guid, long> _titleLoadGeneration = new();

    public override void Initialize()
    {
        base.Initialize();

        _store = new SeasonLedgerStore(SeasonLedgerDbPath.Resolve(_cfg, _res));

        SubscribeLocalEvent<RoundEndMessageEvent>(OnRoundEnd);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<SeasonTitleComponent, ExaminedEvent>(OnExamined);

        // Per-round early-death tracking (partial: SeasonLedgerSystem.EarlyDeath.cs).
        InitializeEarlyDeath();

        // ID-card/PDA standing badge (partial: SeasonLedgerSystem.IdCardStanding.cs).
        InitializeIdCardStanding();

        // v14 quest-board extension: persistent Contracts streak (partial: SeasonLedgerSystem.ContractsStreak.cs).
        InitializeContractsStreak();

        // Verified, cosmetic Director-backed perks (partial: SeasonLedgerSystem.Perks.cs).
        InitializePerks();
    }

    /// <summary>Exposed for the season-reset admin command. Bumps the season and returns the new id.</summary>
    public Task<string> ResetSeasonAsync()
    {
        return _store.BumpSeasonAsync();
    }

    /// <summary>
    ///     Exposed for <c>ProvidenceWelcomeSystem</c> (player-delight lane: "this station remembers
    ///     you"). Thin delegation to the store's career-cumulative read, same access pattern other
    ///     systems already use to reach into the ledger (e.g. <see cref="SubmitContractCompletion"/>,
    ///     <see cref="SubmitRoundStanding"/>) rather than a second system standing up its own
    ///     <see cref="SeasonLedgerStore"/> against the same SQLite file.
    /// </summary>
    /// <summary>Accounts whose most recent antag round is at or after <paramref name="sinceRound"/>.</summary>
    public Task<HashSet<Guid>> GetRecentAntagsAsync(int sinceRound)
    {
        return _store.GetRecentAntagsAsync(sinceRound);
    }

    public Task<PlayerStats> GetCareerStatsAsync(Guid guid)
    {
        return _store.GetCareerStatsAsync(guid);
    }

    /// <summary>
    ///     Exposed for systems that need to award HR points instantly (e.g. <c>ContractsSystem</c>).
    ///     Fire-and-forget: does not block the main thread; the store's semaphore protects the DB.
    /// </summary>
    public async void AwardHrPoints(Guid guid, int points)
    {
        try
        {
            await _store.AwardHrPointsAsync(guid, points);
        }
        catch (Exception e)
        {
            Log.Error($"Error awarding HR points for {guid}:\n{e}");
        }
    }

    internal Task<IReadOnlyList<LedgerRecoverySnapshotItem>> GetRecoverySnapshotAsync() =>
        _store.GetRecoverySnapshotAsync();

    internal Task<RoundEndPersistenceResult> RetryRecoveryAsync(string token) =>
        _store.RecoverPendingRoundEndAsync(token);

    internal Task ArchiveRecoveryAsync(string token, string reasonCode) =>
        _store.ArchivePendingRoundEndAsync(token, reasonCode);

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Apply any completed async title loads on the main thread.
        while (_pending.TryDequeue(out var pending))
        {
            if (string.IsNullOrEmpty(pending.Title) || Deleted(pending.Mob))
                continue;

            // Drop stale loads: two async LoadTitle calls for the same account (e.g. a spawn load racing
            // an admin grant/revoke refresh) complete in arbitrary order, and last-writer-wins here would
            // let the OLDER read overwrite the newer state until next spawn. Only the newest generation
            // for the account may apply.
            if (_titleLoadGeneration.TryGetValue(pending.User, out var latest) && pending.Generation != latest)
                continue;

            var comp = EnsureComp<SeasonTitleComponent>(pending.Mob);
            var previousTitle = comp.Title;
            comp.Title = SolreignPerkRules.ApplyGoldenName(
                pending.Title,
                comp.HasGoldenNamePerk);
            comp.Tours = pending.Tours;
            comp.Rank = pending.Rank;
            comp.RankIndex = pending.RankIndex;
            comp.HrPoints = pending.HrPoints;
            comp.HasAdminGrant = pending.HasAdminGrant;

            // Mirror the fresh standing onto whatever ID card this mob is carrying (partial:
            // SeasonLedgerSystem.IdCardStanding.cs) — the surface OTHER players actually glance at.
            StampIdCardStanding(pending.Mob, comp, previousTitle);

            // NEW grant this season → station-wide HR ceremony (partial: SeasonLedgerSystem.Ceremony.cs).
            if (pending.Ceremony)
                AnnounceTitleCeremony(pending.Mob, pending.CeremonyTitle);
        }

        // The scheduler owns no ECS state and never attaches continuations. This call both starts bounded
        // recovery work and observes completion on the main thread, at initialization and no more than once
        // every thirty seconds thereafter.
        try
        {
            var recovery = _recoveryScheduler.Tick(
                TimeSpan.FromSeconds(frameTime),
                RecoverAndSummarizeAsync);
            if (recovery is not null && _pendingRecoveryHealth.TryDequeue(out var health))
                SeasonLedgerRecoveryMetrics.PublishSnapshot(health);
            if (recovery is { Pending: > 0, FirstPending: { } unresolved })
                LogRecoveryPending(unresolved);
            // Integration-review P1-1: a poison entry no longer aborts the sweep; it is counted
            // instead. Surface it distinctly (sanitized — no tokens/paths/exception content).
            if (recovery is { Faulted: > 0 })
                Log.Warning($"round=none token=none attempts=0 category=RecoverySweepFaulted count={recovery.Faulted}");
        }
        catch
        {
            Log.Warning("round=none token=none attempts=0 category=StorageFailure");
        }

        UpdatePerks();
    }

    private async Task<LedgerRecoverySweepResult> RecoverAndSummarizeAsync(int maximum)
    {
        // Keep the snapshot inside the scheduler's single in-flight operation, then pass only its
        // bounded aggregate back to the main-thread observation pass for Prometheus publication.
        var observation = await _store.RecoverPendingRoundEndsWithHealthAsync(
            maximum,
            DateOnly.FromDateTime(DateTime.UtcNow));
        // Durability seam recovery: unconsumed contracts-streak fold journals (envelope committed,
        // fold TX failed or process died between commits). ROUND-9: re-check the quest-board CVar —
        // when off, leave journals durable and unconsumed for when it re-enables.
        if (_contractsQuestBoardEnabled)
            await _store.RecoverPendingContractStreakFoldsAsync();
        _pendingRecoveryHealth.Enqueue(observation.Health);
        return observation.Sweep;
    }

    /// <summary>
    ///     PRIMARY export hook. Reads the pre-assembled end-of-round roster and folds each connected
    ///     player's contribution into the ledger. async void + try/catch, never blocks the tick.
    /// </summary>
    private async void OnRoundEnd(RoundEndMessageEvent ev)
    {
        try
        {
            // A round that ends with no active preset (CurrentPreset == null — possible on a
            // misconfigured box, in sandbox/dev, and in the stock integration tests' preset-less
            // ticker) arrives with an EMPTY GamemodeTitle. RoundEndEnvelope validates gamemode
            // nonempty, so without this sentinel the whole round's ledger data would be dropped
            // (envelope ctor throws before persistence). Fail SOFT: record the round under a
            // sentinel rather than losing every player's contribution.
            var gamemode = string.IsNullOrEmpty(ev.GamemodeTitle) ? "unknown" : ev.GamemodeTitle;

            // Snapshot every contribution on the main thread BEFORE the first await, so the per-round
            // early-death map (see SeasonLedgerSystem.EarlyDeath.cs) is never read off-thread.
            var records = new List<RoundEndPlayerRecord>();
            foreach (var info in ev.AllPlayersEndInfo)
            {
                if (info.PlayerGuid is not { } netUserId)
                    continue; // observers / disconnected without an account key

                // Contract completions submitted during the round (SeasonLedgerSystem.Contracts.cs).
                // Read on the main thread, before the first await.
                var (contractsCompleted, contractScore) = RoundContractsFor(netUserId.UserId);

                var contribution = new RoundContribution(
                    WasCaptainClean: WasCaptain(info),
                    AntagWin: info.Antag,
                    // Real early-death: did this account die within EarlyDeathWindowSeconds of round start?
                    EarlyDeath: HadEarlyDeath(netUserId),
                    RoundId: ev.RoundId,
                    Gamemode: gamemode,
                    // Corporate Standing handed over by the Corporate Ladder rule during AppendRoundEndText
                    // (fires before this handler). Read on the main thread, before the first await.
                    Standing: RoundStandingFor(netUserId.UserId),
                    ContractsCompleted: contractsCompleted,
                    ContractScore: contractScore,
                    // HR Points system (Beta Feedback 01, Lane B): every completed round is itself a
                    // PG-positive action, plus a per-contract bonus for however many Solreign Contracts this
                    // account completed this round — both already known here, no new tracking needed.
                    HrPointsEarned: HrPointsRules.ForRoundCompletion(contractsCompleted));

                records.Add(new RoundEndPlayerRecord(netUserId.UserId, contribution, info.Connected));
            }

            // Also snapshot the per-completion audit rows before the first await (main-thread contract).
            var contractLog = SnapshotContractLog(ev.RoundId);
            var envelope = SeasonLedgerRoundEndCapture.Capture(
                ev.RoundId,
                gamemode,
                records,
                contractLog);

            // Build streak fold intent on the main thread (CVar-gated). Journaled inside the envelope
            // TX so a later fold failure is recoverable.
            var streakFolds = BuildContractStreakFolds(records, contractLog);

            // Canonical envelope commit first: roster, contract multiset, marker, outbox, and
            // (when non-empty) pending_contracts_streak_folds journal rows.
            var result = await _store.PersistRoundEndAsync(envelope, streakFolds);
            LogRoundEndResult(ev.RoundId, result);

            // v14 quest-board extension (spec §3): atomic streak fold AFTER a durable envelope.
            // Pending means the envelope TX did not stick — never fold (or consume a journal that
            // may not exist yet). Committed/AlreadyCommitted journaled the intents; fold marks
            // them consumed. Failures retry once; recovery replays unconsumed.
            // ROUND-9: re-check the quest-board CVar after the await — if it flipped off mid-flight,
            // skip the fold and leave journals durable for when it re-enables.
            if ((result.Status is RoundEndPersistenceStatus.Committed
                    or RoundEndPersistenceStatus.AlreadyCommitted)
                && _contractsQuestBoardEnabled)
                await UpdateContractStreaksAndNotify(streakFolds, ev.RoundId);
        }
        catch
        {
            // Never emit GUIDs, contract/gamemode content, exception details, hashes, or paths.
            SeasonLedgerRecoveryMetrics.RecordRoundEnd(null);
            Log.Error($"round={ev.RoundId} token=none attempts=0 category=StorageFailure");
        }
    }

    private void LogRoundEndResult(int roundId, RoundEndPersistenceResult result)
    {
        SeasonLedgerRecoveryMetrics.RecordRoundEnd(result.Status);
        var line = $"round={roundId} token={result.Token ?? "none"} attempts={result.Attempts} " +
                   $"category={result.ErrorCategory}";
        if (result.Status == RoundEndPersistenceStatus.Pending)
            Log.Warning(line);
        else
            Log.Info(line);
    }

    private void LogRecoveryPending(LedgerRecoverySnapshotItem item)
    {
        Log.Warning($"round={item.RoundId?.ToString() ?? "none"} token={item.Token} attempts={item.Attempts} " +
                    $"category={item.ErrorCategory}");
    }

    private static bool WasCaptain(RoundEndMessageEvent.RoundEndPlayerInfo info)
    {
        if (info.JobPrototypes is not { } jobs)
            return false;

        foreach (var job in jobs)
        {
            // Coarse: no "fired a weapon" signal reaches the central summary, so any captaincy that
            // survives to round end counts. Refine per-mode later.
            if (job.Equals("Captain", StringComparison.OrdinalIgnoreCase))
                return info.Connected;
        }

        return false;
    }

    /// <summary>
    ///     On spawn, kick off an async ledger lookup for the account and enqueue the computed title.
    ///     No entity mutation here happens off-thread — Update applies the result.
    /// </summary>
    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        var mob = ev.Mob;
        var guid = ev.Player.UserId.UserId;
        LoadTitle(mob, guid);
    }

    private async void LoadTitle(EntityUid mob, Guid guid, bool allowCeremony = true)
    {
        // Claim this load's generation BEFORE the first await (still on the main thread), so invocation
        // order defines which concurrent load for the account is authoritative — see Update's stale drop.
        var generation = _titleLoadGeneration.AddOrUpdate(guid, 1, static (_, current) => current + 1);

        try
        {
            await _store.InitializePlayerStatsAsync(guid);
            var stats = await _store.GetStatsAsync(guid);
            var (title, tours) = TitleRules.Compute(stats);

            // Ceremony gate: only a NEW earned title (differs from the last one announced for this account
            // this season) rates a station-wide HR bulletin — reloads/respawns stay silent. The grant is
            // recorded BEFORE the announcement fires so a racing double-spawn can't double-announce; worst
            // case a bulletin is swallowed by the in-round guard, never duplicated.
            //
            // allowCeremony=false is the DISPLAY-ONLY refresh used by admin title grant/revoke
            // (SeasonLedgerSystem.TitleGrants.cs): it must never touch the announced-title record or pay
            // the HR bonus, so an admin refresh racing a spawn load can't double-award either side effect.
            var ceremony = false;
            if (allowCeremony)
            {
                var announced = await _store.GetAnnouncedTitleAsync(guid);
                ceremony = TitleRules.IsNewGrant(announced, title);
            }

            if (ceremony)
            {
                await _store.SetAnnouncedTitleAsync(guid, title);

                // HR Points system (Beta Feedback 01, Lane B): earning a genuinely NEW corporate title is a
                // PG-positive milestone worth a one-time bonus. Awarded once per grant, guarded by the same
                // ceremony gate that guards the bulletin, so a respawn/reconnect never re-pays it.
                await _store.AwardHrPointsAsync(guid, HrPointsRules.TitleEarnedPoints);
            }

            // Rank AND HR Points are both the career-cumulative model: computed from all-time totals (read
            // AFTER any title-earned bonus above) so neither ever resets on a season bump, and a bonus this
            // ceremony just paid is reflected in the very same badge/rank it enqueues. RankProgression feeds
            // Solreign Contract completions into the score (spec §4.3, M3) without touching the frozen
            // RankRules.cs formula — see RankProgression's doc comment for why.
            var career = await _store.GetCareerStatsAsync(guid);
            var (_, rank, rankIndex) = RankProgression.ComputeCareerRank(career);
            var hrPoints = career.HrPoints;

            // Admin-granted title (community rewards program) masks the earned title on examine only.
            // Deliberately read AFTER the ceremony gate above: earned progression (bulletins, the
            // announced-title record, the HR bonus) keeps running underneath the mask, and the ceremony
            // still announces the EARNED title via PendingTitle.CeremonyTitle.
            var granted = await _store.GetAdminTitleAsync(guid);
            var display = TitleGrantRules.ResolveDisplayTitle(title, granted);

            _pending.Enqueue(new PendingTitle(
                mob, guid, generation, display, title, tours, rank, rankIndex, hrPoints, ceremony,
                HasAdminGrant: !string.IsNullOrEmpty(granted)));
        }
        catch (Exception e)
        {
            Log.Error($"Error while loading season title for {guid}:\n{e}");
        }
    }

    /// <summary>
    ///     Solreign personnel file — discovery-delight flourish (fork feature, cosmetic only, no gameplay
    ///     power). Every value comes straight off <see cref="SeasonTitleComponent"/>, already stamped by
    ///     <see cref="LoadTitle"/> from the same ledger stats the plain title/rank lines always drew from —
    ///     no new tracking, no DB schema change, and nothing surfaced here that the prior three-line examine
    ///     didn't already imply was public (title, tour count, rank, and lifetime HR Points were all shown
    ///     unconditionally before; this only reformats and gates them behind <see cref="PersonnelFileRules.HasRecord"/>).
    /// </summary>
    private void OnExamined(EntityUid uid, SeasonTitleComponent comp, ExaminedEvent args)
    {
        if (string.IsNullOrEmpty(comp.Title))
            return;

        // Genuinely fresh accounts (never completed a round, in any season) get a neutral, tasteful
        // one-liner instead of a personnel file with nothing on it — see PersonnelFileRules.HasRecord's
        // doc comment for why these three fields are the right "has this account done anything" signal.
        // An admin-granted title (community rewards) must surface even on a zero-history account, or the
        // advertised reward would be invisible to exactly the new players it's meant to delight.
        if (!PersonnelFileRules.ShouldDisplay(
                comp.Tours,
                comp.RankIndex,
                comp.HrPoints,
                comp.HasAdminGrant,
                comp.HasGoldenNamePerk))
        {
            args.PushMarkup(Loc.GetString("solreign-personnel-file-unfiled-examine"));
            return;
        }

        var standing = PersonnelFileRules.DescribeCareerStanding(comp.RankIndex);

        args.PushMarkup(Loc.GetString("solreign-personnel-file-examine",
            ("title", comp.Title),
            ("tours", comp.Tours),
            ("standing", standing)));

        // Rank + lifetime HR Points fold into one supplementary line — same info the old three-line
        // examine always showed unconditionally, just consolidated so the whole personnel file stays to
        // two lines (Beta Feedback 01, Lane B's "always visible, career-cumulative" HR Points contract
        // still holds: whenever this branch runs, HasRecord already guarantees there's something to show).
        if (!string.IsNullOrEmpty(comp.Rank))
            args.PushMarkup(Loc.GetString("solreign-personnel-file-detail-examine",
                ("rank", comp.Rank),
                ("points", comp.HrPoints)));
    }
}
