using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     URL of the Discord webhook that in-game "bugreport" console command submissions are
    ///     relayed to. If left empty (default), the Discord relay is disabled and reports are only
    ///     written to data/bug_reports.jsonl.
    /// </summary>
    public static readonly CVarDef<string> SolreignBugReportWebhook =
        CVarDef.Create("solreign.bugreport_webhook", string.Empty, CVar.SERVERONLY | CVar.CONFIDENTIAL);

    /// <summary>
    ///     Whether "Providence", Solreign's station voice, plays its voice-pack lines (shift
    ///     start/end, title ceremonies, event stingers, idle musings, etc — see
    ///     <c>Content.Server._Solreign.Providence.ProvidenceVoiceSystem</c>). Defaults on; same
    ///     server-only feature-toggle idiom as <c>CCVars.HolidaysEnabled</c>. Flip off to fall back
    ///     to the pre-Providence stock stingers/silence everywhere Providence has a fallback branch.
    /// </summary>
    public static readonly CVarDef<bool> SolreignProvidenceEnabled =
        CVarDef.Create("solreign.providence_enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Shared-secret token used to HMAC-SHA256 sign every outbound Solreign Director-channel
    ///     request (Oracle/Rivalry/LiveMap/Perks &#8594; <see cref="SolreignDirectorUrl"/>) and to verify every inbound
    ///     response, via <c>Content.Server._Solreign.Director.DirectorChannel</c>. Empty (the
    ///     default) disables the entire Director channel: every consuming system fails CLOSED —
    ///     no unsigned outbound call is sent and no unverified inbound response is trusted. Set
    ///     this via server config/environment, never via source/commit. NEVER hardcode or
    ///     generate a real secret in code.
    /// </summary>
    public static readonly CVarDef<string> SolreignDirectorToken =
        CVarDef.Create("solreign.director.token", string.Empty, CVar.SERVERONLY | CVar.CONFIDENTIAL);

    /// <summary>
    ///     Base URL of the Solreign Director daemon that the Oracle/Rivalry/LiveMap/Perks systems
    ///     call. Defaults to the historical local sidecar address for back-compat; set it to the
    ///     deployed daemon (e.g. <c>https://solreign-director.fly.dev</c>) on the live box. A
    ///     trailing slash is tolerated (stripped by <c>DirectorChannel.GetBaseUrl</c>). Server-only;
    ///     set via server config/environment.
    /// </summary>
    public static readonly CVarDef<string> SolreignDirectorUrl =
        CVarDef.Create("solreign.director.url", "http://127.0.0.1:3000", CVar.SERVERONLY);

    /// <summary>
    ///     MASTER kill switch for the entire Director channel (v11 backbone #2). Gates
    ///     <c>DirectorChannel.TryGetReadyToken</c>/<c>IsReady</c> themselves, so EVERY consumer
    ///     (Oracle, Rivalry, LiveMap, Perks, poll delivery) is inert while this is off, regardless
    ///     of its own per-feature CVar. The <c>/director/poll</c> delivery loop in
    ///     <c>SolreignOracleSystem</c> is gated on this alone — it carries ALL daemon&#8594;game events
    ///     (broadcasts, crypt, bounty results, market spawns), not just Oracle traffic — while
    ///     <see cref="SolreignOracleEnabled"/> narrows to just the Oracle chat/action-pipe.
    ///     Enabled 2026-07-25 (activation pass); requires a well-formed <see cref="SolreignDirectorToken"/> on top, same as
    ///     every per-feature flag. The master ALONE delivers no player-facing content: every
    ///     player-visible poll side effect additionally requires its feature switch
    ///     (<see cref="SolreignDirectorBroadcastsEnabled"/> for station text/spawns,
    ///     <see cref="SolreignOracleEnabled"/> for Oracle chat, etc), so master+token is safe to
    ///     run for telemetry (crypt, rivalry, round orchestration) only.
    /// </summary>
    public static readonly CVarDef<bool> SolreignDirectorEnabled =
        CVarDef.Create("solreign.director.enabled", false, CVar.SERVERONLY);

    /// <summary>
    ///     Kill switch for the PLAYER-FACING Director poll side effects: station-wide popup/meteor
    ///     text, <c>spawn_paper</c>, and <c>spawn_decal</c> (grk v11 review P0: these must never
    ///     ride the master switch alone — an operator enabling the master for telemetry backbone
    ///     must not thereby arm daemon&#8594;player content). Enabled 2026-07-25 (activation pass) and HOLD: even though the
    ///     daemon moderates its outbound text (Phase 2 gate), this is the game-side
    ///     defense-in-depth boundary per Living-Universe guardrail 1, and flipping it on live
    ///     needs John's explicit sign-off. Rechecked PER EVENT during the drain, same as the
    ///     master.
    /// </summary>
    public static readonly CVarDef<bool> SolreignDirectorBroadcastsEnabled =
        CVarDef.Create("solreign.director.broadcasts.enabled", false, CVar.SERVERONLY);

    /// <summary>
    ///     Kill switch for the Oracle integration: the in-game LLM chat (prayer &#8594; daemon &#8594;
    ///     dialogue popup) and its action pipe (flash_lights, generate_item — the latter a hard
    ///     no-op behind an intentionally empty cosmetic allowlist). Station-wide popup/meteor and
    ///     entity spawns from the poll are NOT gated here — they're
    ///     <see cref="SolreignDirectorBroadcastsEnabled"/> (grk v11 review finding 3: comments
    ///     must match the actual gates); audience_purchase/drop_weapon were removed from the
    ///     event validator entirely. Enabled 2026-07-25 (activation pass). Per AGX-INTEGRATION-CONTRACT.md clause 6 and
    ///     AGX-MERGE-PLAN.md, the player-facing chat remains HOLD pending a content-safety design
    ///     pass and John's explicit sign-off — this CVar being on is necessary but not sufficient;
    ///     it only permits the (still auth-gated) wiring to run at all.
    /// </summary>
    public static readonly CVarDef<bool> SolreignOracleEnabled =
        CVarDef.Create("solreign.oracle.enabled", false, CVar.SERVERONLY);

    /// <summary>
    ///     Kill switch for the Rivalry combat-telemetry webhook (SolreignRivalrySystem). Default
    ///     OFF. Telemetry-only, no LLM/player-facing content — the GREEN-cleared AGX item.
    /// </summary>
    public static readonly CVarDef<bool> SolreignRivalryEnabled =
        CVarDef.Create("solreign.rivalry.enabled", false, CVar.SERVERONLY);

    /// <summary>
    ///     Kill switch for the Crypt death-report webhook (SolreignCryptSystem, v11 backbone #3):
    ///     POSTs victim/attacker player GUIDs to the Director daemon's
    ///     <c>/api/crypt/death</c> on player deaths. The DAEMON decides whether a death is
    ///     "legendary" (high standing or a nemesis kill) and generates the obituary/relic — the
    ///     game only reports the raw fact. Telemetry-only, no player-facing content. Enabled 2026-07-25 (activation pass);
    ///     like every Director consumer it also requires the master
    ///     <see cref="SolreignDirectorEnabled"/> + a well-formed <see cref="SolreignDirectorToken"/>.
    /// </summary>
    public static readonly CVarDef<bool> SolreignCryptEnabled =
        CVarDef.Create("solreign.crypt.enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Kill switch for the LiveMap occupancy stream. Every ten seconds the server publishes
    ///     only bounded eight-tile cell counts for living player mobs on the station's main map:
    ///     never user/session/entity IDs, names, roles, rotation, or exact positions. The signed
    ///     Director ingest applies delayed k-anonymous public projection. This also requires the
    ///     master switch and a well-formed <see cref="SolreignDirectorToken"/>.
    /// </summary>
    public static readonly CVarDef<bool> SolreignLivemapEnabled =
        CVarDef.Create("solreign.livemap.enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Kill switch for the Director-driven cosmetic perk fetch (SeasonLedgerSystem.Perks.cs).
    ///     Only the GoldenName cosmetic-title path remains behind this flag; the StartingGear
    ///     contraband-loadout branch was removed entirely (preserved for a future clause-6 PR at
    ///     docs/agx-hold/StartingGear-perk.patch). Enabled 2026-07-25 (activation pass).
    /// </summary>
    public static readonly CVarDef<bool> SolreignPerksEnabled =
        CVarDef.Create("solreign.perks.enabled", false, CVar.SERVERONLY);

    /// <summary>
    ///     Kill switch for "Requisitions Anonymous", the black-market console (v11 front door #3,
    ///     <c>SolreignMarketSystem</c>): browsing the Director daemon's curated market inventory
    ///     (<c>GET /api/public/market</c>), spending Corporate Standing on a listing
    ///     (<c>POST /api/market/buy</c> — the daemon deducts Standing atomically; this system never
    ///     touches Standing itself), AND the poll-delivered <c>spawn_entity</c> event that
    ///     physically materializes a purchase on the buyer. Like every Director consumer this also
    ///     requires the master <see cref="SolreignDirectorEnabled"/> plus a well-formed
    ///     <see cref="SolreignDirectorToken"/>. Enabled 2026-07-25 (activation pass).
    ///
    ///     Unlike the station-wide popup/meteor/spawn_paper/spawn_decal effects, which are gated on
    ///     <see cref="SolreignDirectorBroadcastsEnabled"/>, <c>spawn_entity</c> delivery is gated on
    ///     THIS flag alone — it is a per-purchase, per-buyer delivery the player explicitly paid
    ///     Standing for, not an unsolicited station-wide broadcast, so it does not need the
    ///     broadcasts flag on top (see <c>SolreignOracleSystem.Update</c>'s Director-event drain).
    /// </summary>
    public static readonly CVarDef<bool> SolreignMarketEnabled =
        CVarDef.Create("solreign.market.enabled", false, CVar.SERVERONLY);

    /// <summary>
    ///     Kill switch for the Liability Board (SolreignBountySystem, v11 front-door #3): a
    ///     player-reachable wallmount that lists the Director daemon's active bounties
    ///     (<c>GET /api/public/bounties</c>) and lets a player submit a short claim
    ///     (<c>POST /api/bounty/claim</c>) that the daemon's LLM judges against the pool. Same
    ///     idiom as <see cref="SolreignOracleEnabled"/>: default OFF, and even when true still
    ///     requires the master <see cref="SolreignDirectorEnabled"/> plus a well-formed
    ///     <see cref="SolreignDirectorToken"/> (both channel calls route through
    ///     <c>DirectorChannel</c>). Appended at the end of this class rather than inline near the
    ///     other feature flags to minimize merge conflicts with concurrent Director-channel work.
    /// </summary>
    public static readonly CVarDef<bool> SolreignBountiesEnabled =
        CVarDef.Create("solreign.bounties.enabled", false, CVar.SERVERONLY);

    /// <summary>
    ///     Override for the on-disk path of the Season Ledger SQLite database that
    ///     <c>SeasonLedgerSystem</c> and <c>WingmateSystem</c> open. Empty (the default) keeps the
    ///     production resolution: <c>IResourceManager.UserData.RootDir</c> (falling back to CWD)
    ///     + <c>solreign_season_ledger.db</c>. This exists as a TEST seam: the ledger is a
    ///     persistent cross-round store whose round-envelope replay protection is keyed by round
    ///     id, and pooled integration-test server instances all number their rounds from 1 — so a
    ///     shared on-disk file makes any two round-ending tests (or two back-to-back suite runs)
    ///     collide with a replay-conflict StorageFailure. The test pool points each server
    ///     instance at its own unique temp file instead (see
    ///     <c>Content.IntegrationTests.Pair.TestPair.ServerOptions</c>). Never set on a live box.
    /// </summary>
    public static readonly CVarDef<string> SolreignSeasonLedgerDbPath =
        CVarDef.Create("solreign.season_ledger_db_path", string.Empty, CVar.SERVERONLY);

    /// <summary>
    ///     Kill switch for SR-W-082, the round-end salary roster: when true, the existing
    ///     <c>POST /api/director/round-end</c> body (<c>SolreignRoundOrchestrationSystem</c>)
    ///     gains an additional <c>roster</c> array — one entry per player who both has a
    ///     resolvable account GUID (<c>Robust.Shared.Network.NetUserId</c>, NEVER an entity uid —
    ///     see <c>SalaryRosterPayload</c>'s doc comment for why that distinction matters) and
    ///     actually played this round, so the Director daemon can award a modest career-rank
    ///     salary. Enabled 2026-07-25 (activation pass): with this off, the round-end payload is byte-identical to the
    ///     pre-SR-W-082 body — no <c>roster</c> key is emitted at all. Like every Director
    ///     consumer, still requires the master <see cref="SolreignDirectorEnabled"/> plus a
    ///     well-formed <see cref="SolreignDirectorToken"/> underneath it.
    /// </summary>
    public static readonly CVarDef<bool> SolreignSalaryEnabled =
        CVarDef.Create("solreign.economy.salary.enabled", false, CVar.SERVERONLY);

    /// <summary>
    ///     Kill switch for "The Authored First Death" — PROVIDENCE's once-per-account-EVER authored
    ///     death scene (<c>ProvidenceFirstDeathSystem</c>; spec
    ///     docs/specs/FIRST-DEATH-SPEC-2026-07-16-DRAFT.md): station-wide eulogy by name + voice
    ///     sting + private ghost line at death, and the private "Reinstatement Processing" beat on
    ///     the player's next spawn. Default ON: the scene is fully scripted (zero LLM anywhere),
    ///     PG-13-screened, once-per-account, and offline-complete — the same risk profile as the
    ///     cosmetic Providence beats that already default on (welcome, commiseration). Flipping
    ///     this off is the universal rollback and restores today's death behavior exactly.
    ///     Appended at the end of this class per the append-at-end idiom (see
    ///     <see cref="SolreignBountiesEnabled"/>'s own note) to minimize merge conflicts.
    /// </summary>
    public static readonly CVarDef<bool> SolreignFirstDeathEnabled =
        CVarDef.Create("solreign.first_death.enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     URL of the Discord webhook the first-death obituary embed is posted to (FD-W4 lane;
    ///     <c>BugReportSystem</c>'s <see cref="SolreignBugReportWebhook"/> idiom). Empty (the
    ///     default) = obituary posting is OFF; the in-game scene is unaffected. DEFINED in the
    ///     FD-W2 lane but deliberately UNCONSUMED by it — no webhook-sending code exists behind
    ///     this CVar yet, and setting it on the live box is a John go-live decision (spec §6.2).
    /// </summary>
    public static readonly CVarDef<string> SolreignFirstDeathWebhook =
        CVarDef.Create("solreign.first_death.webhook", string.Empty, CVar.SERVERONLY | CVar.CONFIDENTIAL);

    /// <summary>
    ///     Player gate for the Discord obituary post (FD-W4): the embed only posts when
    ///     <c>IPlayerManager.PlayerCount</c> is at least this at obituary time — a solo player's
    ///     first death still gets the full in-game scene (a personal keepsake), but the Discord
    ///     post (an advertisement surface) requires witnesses (the council's recap-law analogue,
    ///     spec §6.2). Like the webhook CVar above, defined here but unconsumed by FD-W2.
    /// </summary>
    public static readonly CVarDef<int> SolreignFirstDeathMinPlayers =
        CVarDef.Create("solreign.first_death.min_players", 2, CVar.SERVERONLY);

    /// <summary>
    ///     Kill switch for the crew Noticeboards (v14 wave-1 #2; spec
    ///     docs/council/2026-07-17-player-text-safety.md, the C2 posture memo — that document is
    ///     LAW for this feature's behavior). A wall-mounted, admin-spawnable board
    ///     (<c>SolreignNoticeboardSystem</c>) where players pin a short, classifier-gated,
    ///     auto-expiring note; PROVIDENCE seeds each board so it never ships looking empty. Same
    ///     idiom as every other Director-adjacent Solreign feature: default OFF.
    ///
    ///     <b>THIS FLAG ALONE IS NOT A PRODUCTION GO.</b> The C2 memo's 3-0 vote holds that
    ///     activating a brand-new persistent player-authored text surface on a live server ahead
    ///     of the Moderation Constitution's own Section 8 rollout gate (the staff tabletop drill +
    ///     dated ACKNOWLEDGMENTS.md record — see docs/MODERATION-CONSTITUTION.md §8 in the
    ///     orch-ops repo) would use a documentation gap against the Constitution's evident
    ///     purpose. Design/implementation/staging-dev testing may proceed with this CVar true in a
    ///     non-production config; flipping it true on the live box is a HUMAN gate (Constitution
    ///     §8 drill completion, recorded and dated) — never an automated or Claude-initiated flip.
    ///     Posting still additionally requires the master <see cref="SolreignDirectorEnabled"/>
    ///     plus a well-formed <see cref="SolreignDirectorToken"/> (the write path's classifier
    ///     round-trip routes through <c>DirectorChannel</c> exactly like Bounties); board READS are
    ///     local Season Ledger queries and need neither.
    /// </summary>
    public static readonly CVarDef<bool> SolreignNoticeboardsEnabled =
        CVarDef.Create("solreign.noticeboards.enabled", false, CVar.SERVERONLY);

    /// <summary>
    ///     Hard cross-round retention window (hours) for every noticeboard note, player- or
    ///     PROVIDENCE-authored alike: flat expiry, no renewal (spec rule 4). The C2 council voted
    ///     72h 2-1 (the abuse-vector and youth-safety seats converged on it after cross-feed,
    ///     preferring a short non-renewable window that narrows how long a note can greet a
    ///     returning player with stale hostility or let an in-group callout harden into standing
    ///     lore); the textualist seat's dissent — preserved verbatim in the memo — argued for 7
    ///     days (168h) on the view that 72h may be short for an ordinary flavor note, reasoning the
    ///     hide tool already covers most of the residual harassment risk a longer window reopens.
    ///     This CVar exists specifically so that number is operator-turnable without a code change,
    ///     per the memo's own instruction ("implement 72h as a CVar default"). Default 72 (the
    ///     majority's number); changing it on a live box is an ordinary operator config change, NOT
    ///     the Section 8 production-activation gate above — the two are independent knobs.
    /// </summary>
    public static readonly CVarDef<int> SolreignNoticeboardExpiryHours =
        CVarDef.Create("solreign.noticeboards.expiry_hours", 72, CVar.SERVERONLY);

    /// <summary>
    ///     Kill switch for SR-W-044 Multi-Station Fleet Operation Night logic and preview round
    ///     presets. Enabled 2026-07-25 (activation pass): when false, multi-station fleet round options are disabled and
    ///     cross-grid expedition transit gates remain locked in safety posture.
    /// </summary>
    public static readonly CVarDef<bool> SolreignFleetOperationsEnabled =
        CVarDef.Create("solreign.fleet_operations_enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum simultaneous active station/vessel grids permitted in a Fleet Operation night round
    ///     (default 3: Station Alpha, Station Beta, RV Expedition).
    /// </summary>
    public static readonly CVarDef<int> SolreignFleetOperationsMaxGrids =
        CVarDef.Create("solreign.fleet_operations.max_grids", 3, CVar.SERVERONLY);
}
