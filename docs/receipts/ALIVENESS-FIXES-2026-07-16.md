# ALIVENESS FIXES — receipt — 2026-07-16

**Branch:** `feat/aliveness-fixes` (worktree off GAME master `e29185f6a8`).
**Spec:** `orch-ops/docs/ALIVENESS-AUDIT-2026-07-16.md` §B, items #1-#9 in order. Additive-only, PG-13,
all new player-facing strings through `_Solreign` .ftl files in the house voice.
**Verification battery (Release, this branch):** counts in §Verification below.

---

## P0 #1 — Bounty claim silent dead-air

**What changed:** every player-path failure after "Submit Claim" now produces exactly one in-fiction
failure popup, mirroring the `SolreignOracleSystem` failure idiom verbatim:

- `Content.Server/_Solreign/Bounties/SolreignBountySystem.cs` — `FireClaim`'s four failure branches
  (non-2xx, unverified ack, null/unparseable ack, transport exception) each log server-side AND
  enqueue the actor into a new `_pendingClaimFailures` `ConcurrentQueue`, drained on the game thread
  in `Update()` **unconditionally, ahead of the kill-switch gate** (a failure popup reports our own
  already-sent POST, not daemon content — the Oracle's documented drain discipline). One HTTP attempt
  per claim, so the queue is bounded; never a retry.
- Rate-limit deny in `OnClaimMessage` (also player-visible dead air before this fix) popups directly
  on the game thread with a distinct "desk is busy" line — a local refusal must not read as an outage.
- `Content.Server/_Solreign/Bounties/BountyClaimRules.cs` — pure seams `ClaimFailurePopupLocKeys` (3
  variants) + `PickClaimFailureLocKey(int)` (total for any int, negative rolls included).
- `Resources/Locale/en-US/_solreign/bounties.ftl` — 3 failure variants + busy line, corporate-sinister
  deadpan, PG-13, no HTTP/daemon detail ever player-facing.

**Test evidence:** `BountyClaimRulesTests` — `ClaimFailurePopupLocKeys_AreDistinctNonBlankAndPlural`,
`PickClaimFailureLocKey_IsTotalForAnyRollIncludingNegatives`, `PickClaimFailureLocKey_CoversEveryVariant`
(pure-seam convention; the repo deliberately has no fake-daemon HTTP harness — see the UX-SIMPLE receipt).

## P0 #2 — Bounty board offline ≠ empty

**What changed:** a failed listing fetch now pushes an explicit OFFLINE board state; the true-empty
copy only renders on a successful empty response — the Market "dealer is not answering" idiom.

- `Content.Shared/_Solreign/Bounties/SharedSolreignBounties.cs` — `SolreignBountyUiState.Offline`
  (additive, defaulted false; every pre-existing constructor call keeps its meaning).
- `SolreignBountySystem.cs` — `_pendingListings` payload is now nullable (`null` = fetch failed);
  all three fetch failure shapes (non-2xx, null body, thrown exception) enqueue it. The drain maps it
  through the pure `BountyClaimRules.ShapeBoardListings` seam; the listing cache is never touched on
  failure, so a later claim verdict still refreshes with the last honest listing.
- `Content.Client/_Solreign/Bounties/UI/SolreignBountyBoardWindow.xaml(.cs)` — new `OfflineLabel`;
  `NoBountiesLabel` hidden while offline. BUI passes the flag through.
- `bounties.ftl` — `solreign-bounties-window-offline`.

**Test evidence:** `BountyClaimRulesTests` — `ShapeBoardListings_FailedFetch_IsOfflineWithZeroRows`,
`ShapeBoardListings_SuccessfulEmptyFetch_IsTrueEmptyNeverOffline`,
`ShapeBoardListings_SuccessfulFetch_PassesRowsThroughUnchanged`.

## P0 #3 — Wingmates pop-1 messaging

**What changed:** a solo seeker no longer sees the open-ended "Your request is open…" framing.

- `Content.Shared/_Solreign/PlayerDelight/Wingmates/WingmateUiMessages.cs` —
  `WingmateUiState.PeerAvailable` (additive, default true): a bare presence bit, never who/how
  many/where.
- `Content.Server/_Solreign/PlayerDelight/Wingmates/WingmateSystem.cs` — `BuildPrivateState` computes
  it only for the Seeking view via new `HasEligiblePeer` (any OTHER connected session with an
  attached, alive, non-ghost entity — the same basic floor guides are held to; tenure/approval
  deliberately not applied since approval can arrive mid-round). Pure seam
  `WingmateSystem.IsEligiblePeer(isSelf, hasAttached, isGhost, isDead)`. Fails closed (no peers)
  without a player manager, the rules-harness null-guard idiom.
- `Content.Client/.../WingmateWindow.xaml(.cs)` — WaitingPanel labels named and toggled: with a peer,
  the original three lines; without, `wingmates-waiting-no-peers` + a suggestion line pointing at
  First Assignment / Contracts Board / Directive Terminal. Cancel stays available; the request itself
  stays open server-side, so a joining player restores the normal view on the next snapshot. Normal
  seek with a 2nd eligible player is untouched.
- `wingmates.ftl` — 2 new keys in the tab's plain consent-first voice.

**Test evidence:** `WingmateSystemRulesTests.PureEligiblePeerSeamRequiresAnotherAttachedLivingNonGhostPlayer`;
`WingmateUiStatePrivacyTests` updated (`PeerAvailable` added to the privacy-reviewed property list —
reflection test enforces the exact set; carries no identity, no `PrivateTerms` collision).

## P0 #4 — First-shift completion acknowledgment

**What changed:** a genuinely-applied Complete (reducer reason `"completed"`, all existing
generation/stage guards intact) now fires:

- an `"Orientation logged."` popup at the player, and
- a private chat handoff line pointing at the Contracts Board / fulfillment dropbox (chat persists
  after the popup fades — the `LowPopLobbyReminderSystem` delivery idiom).

`Content.Server/_Solreign/PlayerDelight/FirstShift/FirstShiftSystem.cs` — `ApplyGenerationIntent` now
returns the transition result (additive; other callers unchanged); `OnComplete` acts on it.
`first-shift.ftl` — `first-shift-complete-popup`, `first-shift-complete-handoff`. **Observation-only
card safety text untouched** (no card/prototype content changed).

**Test evidence:** the completion transition itself is already pinned by `FirstShiftRoundStateTests`
(Complete at Debrief → `"completed"`); the popup/chat call site is ECS delivery on that exact seam.
Whole-suite regression green.

## P1 #5 — Salary award surface (ADAPTED — see note)

**What changed:** when the round-end roster POST is actually DELIVERED (2xx), every emitted roster
account gets a private chat payroll notice (persists into the lobby) plus an in-world popup while
still attached. `Content.Server/Administration/Systems/SolreignRoundOrchestrationSystem.cs`:
`BuildRoundEndBodyAsync` returns the roster accounts alongside the body; `SendSignedPostAsync`
returns delivery success; async continuation enqueues scalar account ids only, drained in a new
`Update()` with master+salary kill-switch discipline. `award-popup.ftl` — 2 new keys.

**ADAPTATION vs the audit's literal "+N Standing — shift stipend":** the wire contract sends only
`{guid, rank_tier, salary_eligible}`; the daemon computes the amount from career rank AFTER receiving
the roster and there is no response echo — the game never learns N (confirmed in
`docs/receipts/ux-simple/UX-SIMPLE-2026-07-16.md`'s scoping note). Fabricating a number would violate
the claims-vs-prod honesty rule, so the surface is deliberately number-free ("stipend credited,
scaled to your career rank"). Wiring a real N needs a daemon-side contract change — out of scope for
this game-only worktree.

**Test evidence:** eligibility/payload logic unchanged and still covered by `SalaryRosterPayloadTests`;
notification set = exactly the emitted roster entries (same `IsSalaryEligible` gate). Suite green.

## P1 #6 — Wingmate guide cold-start default

`Content.Shared/CCVar/CCVars.PlayerDelight.cs` — `solreign.wingmates_minimum_shifts` code DEFAULT
10 → **1**, with the launch-weeks rationale and the re-raise plan documented on the CVar (raise back
toward 10 in live config once a veteran bench exists; live override wins, no cut needed). Live config
untouched, per spec. No test pinned the old default (integration tests set their own values —
verified by grep).

## P1 #7 — PROVIDENCE low-pop cadence clamp

- New CVar `solreign.providence.lowpop_cadence_threshold` (default **3**, SERVERONLY) in its own
  partial file `CCVars.SolreignProvidenceLowPopCadence.cs` (D0 collision-control convention).
- `Content.Server/_Solreign/Providence/ProvidenceVoiceSystem.cs` — `ScheduleIdle` picks the window via
  the pure `IdleWindow(playerCount, threshold)` seam: **8-15 min** at or below the threshold, the
  stock **20-40 min** above it. Evaluated per scheduling (round start + after each musing), so cadence
  returns to default within one cycle of the population rising. Same `PlayerCount`-vs-threshold idiom
  as `LowPopLobbyReminderSystem.cs:95`; `PeriodicEffectTiming` reused untouched; no new VO.

**Test evidence:** new `ProvidenceIdleCadenceTests` (6 tests): band selection at/below/above,
inclusive boundary, threshold-0 behavior, low-pop band strictly tighter AND shorter than default
(inversion guard), and end-to-end `NextFireTime` interpolation with the clamp values.

## P1 #8 — Acid Storm organic path

`Resources/Prototypes/GameRules/events.yml` — `SolreignAcidStorm` added to `BasicCalmEventsTable`
(consumed by the Basic, Dynamic and Ramping schedulers via `BasicGameRulesTable`/`DynamicGameRulesTable`).
Weight stays its own low 3 (below both siblings); no `minimumPlayers`, so it can fire on a solo shift.
**CVar-off guard verified for the scheduler path by reading the rule:** `SolreignAcidStormRule`
overrides `Added` and `Ended` to skip the base announcements entirely while
`solreign.events.acid_storm` is off, and `Started` `ForceEndSelf`s immediately — a disabled,
scheduler-rolled storm produces zero announcements/FX/VO and no orphan all-clear. Doc comments in the
rule + `game_rules_weather.yml` updated to the new posture (siblings remain admin-only).

## P2 #9 — S07 lore markers on the remaining 5 maps

One `RandomSpawner` marker per map (matching Meridian/Leviathan's one-spawner, one-fragment-per-round
cadence exactly), the two fragment pools alternated so both circulate in rotation:

| Map | Marker | uid | Tile (anchor) |
|---|---|---|---|
| Oasis | `SpawnSolreignS07PersonnelWing` | 900900 | 8.5,20.5 parent 31 (SpawnPointHeadOfPersonnel) |
| Nocturne | `SpawnSolreignS07RecordsWing` | 900901 | 22.5,2.5 parent 2 (SpawnPointHeadOfSecurity) |
| Perihelion | `SpawnSolreignS07PersonnelWing` | 900902 | 19.5,-4.5 parent 2 (SpawnPointHeadOfPersonnel) |
| Verdant | `SpawnSolreignS07RecordsWing` | 900903 | 58.5,38.5 parent 2 (SpawnPointDetective) |
| Terminus | `SpawnSolreignS07PersonnelWing` | 900904 | 23.5,13.5 parent 2 (SpawnPointHeadOfPersonnel) |

**MAP LAW compliance:** all uids in the orchestrator-assigned **900900-900999** range; each uid
grep-verified to appear exactly once in its map file; no 9009xx duplicates anywhere. Anchors are
verified-walkable per-job SpawnPoint tiles (the lore-trail lane's anchor idiom) with no existing
900xxx lore entity on them (grep-verified). Per-map result: **7/7 rotation maps** now carry ≥1
`SpawnSolreignS07*` marker. Covered by `GameMapsLoadableTest`.

**Interpretation note:** the audit's "copy the two markers onto the 5 maps" was implemented as one
marker per map with alternating pools — placing BOTH on each new map would have doubled their
fragment cadence relative to Meridian/Leviathan and broken the arc's deliberate slow-burn parity.

---

## Verification (Release, this branch, foreground)

| Suite | Result |
|---|---|
| `dotnet build -c Release` | 0 errors (50 projects; warnings pre-existing) |
| `dotnet test Content.Tests` | **2233 passed / 0 failed / 3 known skips** (baseline ~2216; +17 new tests from this lane) |
| `dotnet test Content.IntegrationTests --filter FullyQualifiedName~Solreign` | **302 passed / 0 failed / 1 known skip** (14m41s) |
| `dotnet test Content.IntegrationTests --filter FullyQualifiedName~GameMapsLoadableTest` | **246 passed / 0 failed** (2m05s) |
| `dotnet run --project Content.YAMLLinter` | **No errors found** |

## Commits

1. `fix(bounties): claim-failure popups + offline-vs-empty board state (ALIVENESS P0 #1+#2)`
2. `fix(wingmates): honest no-peer state for a solo seeker (ALIVENESS P0 #3)`
3. `fix(first-shift): completion acknowledgment + Contracts Board handoff (ALIVENESS P0 #4)`
4. `feat(salary): visible round-end shift-stipend notice (ALIVENESS P1 #5)`
5. `fix(wingmates): guide cold-start — minimum_shifts code default 10 -> 1 (ALIVENESS P1 #6)`
6. `feat(providence): low-pop idle-musings cadence clamp (ALIVENESS P1 #7)`
7. `feat(events): Acid Storm organic path via BasicCalmEventsTable (ALIVENESS P1 #8)`
8. `feat(maps): S07 'Open Bunk' fragment spawners on the remaining 5 maps (ALIVENESS P2 #9)`
9. this receipt
