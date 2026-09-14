# NOTICEBOARDS build receipt — `crew-noticeboards` (2026-07-17)

**Lane:** NOTICEBOARDS builder (foreground). **v14 wave-1 #2** (docs/council/2026-07-17-
v14-content-adjudication.md's synthesis ruling: Directives-fax -> Noticeboards -> Station
Audits/Report). **Branch:** `feat/noticeboards` off `origin/master` @ `dcb87e1ace`. **Spec:**
`orch-ops docs/council/2026-07-17-player-text-safety.md` — the C2 posture memo, BINDING LAW for
this build (five hard rules below). CLEAN-ROOM RAIL: idea drawn only from Goonstation's public
wiki description of cross-round noticeboards; their code (CC-BY-NC-SA) was never read; every file
here is built fresh from OUR house idioms (Bounty Board / Mark Garden / DirectorChannel / Season
Ledger). No merges, no pushes — intake is the orchestrator's.

## Spec-compliance checklist (the memo's five hard rules)

| # | Rule | How implemented |
|---|---|---|
| 1 | Every note passes the daemon's fail-closed `moderate_output()` at WRITE time under a new "noticeboard" surface budget before ever becoming visible. | Game side never writes a row before the daemon approves (`SolreignNoticeboardSystem.FirePost`): the signed `POST /api/noticeboard/post` round trip runs FIRST; only on `allowed: true` does the game call `SeasonLedgerStore.TryPostNoteAsync`. Daemon side (`solreign-director` `orchestrator/moderation.py`): added `"noticeboard": 260` to `MAX_CHARS_BY_SURFACE` and a `_FALLBACKS` entry, additively — no existing surface touched. The route (`orchestrator/server.py` `POST /api/noticeboard/post`) calls `sanitize_player_input()` then `moderate_output(text, "noticeboard")` and returns `{"allowed": bool, "text": str}`; a rejection is `200 {"allowed": false, ...}`, never a 500/503, because `moderate_output()` fails closed by RETURNING `allowed=False`, never raising (proven in `tests/test_noticeboard.py`). |
| 2 | One active note per author, 24h cooldown, <=260 chars, <=12 active slots per board incl. PROVIDENCE. | All four numbers live in `NoticeboardRules` (`Content.Server/_Solreign/Noticeboards/NoticeboardRules.cs`: `MaxNoteLength=260`, `BoardCapacity=12`, `ProvidenceCapacity=2`, `CooldownHours=24`) and are enforced ATOMICALLY inside one `BEGIN IMMEDIATE` transaction, `SeasonLedgerStore.TryPostNoteAsync` — the exact `TryClaimMarkAsync` fold-the-checks-into-the-insert discipline. A durable, separate `noticeboard_authors` table tracks the cooldown clock so it survives the note row's own physical expiry-sweep deletion (`NoticeboardStoreTests.Sweep_NeverTouchesTheAuthorCooldownClock`). A classifier approval is advisory only until this call returns `Posted: true`. |
| 3 | One-tap report/moderator action instantly reversibly HIDES a note (containment, not sanction). | Any player's one-tap report (`SolreignNoticeboardReportMessage` -> `SolreignNoticeboardSystem.OnReportMessage`) calls `SeasonLedgerStore.HideNoteAsync` directly — no daemon round trip, no independent review, idempotent. A hidden note is fully absent from the next `GetActiveNotesAsync` projection (never a visible "hidden" placeholder). Reversal is a moderator-only `noticeboardunhide <id>` console command (`[AdminCommand(AdminFlags.Moderator)]`), which refuses to revive a note that expired while hidden (`UnhideNoteAsync`'s `expires_utc > $now` guard — "silence always resolves to the safe outcome"). The memo's ONE non-routine path — an early, affirmative PERMANENT purge ahead of normal expiry, which the memo requires to follow the same independent-review-or-hold pattern as other final decisions — is deliberately NOT built: this codebase has no reviewer-queue primitive to hang it on yet, and shipping an unreviewed destructive command would violate the memo's own condition. Documented as an intentional gap in `NoticeboardUnhideCommand`'s doc comment. |
| 4 | Flat 72h hard expiry, no renewal; CVar-tunable number. | `CCVars.SolreignNoticeboardExpiryHours` (`solreign.noticeboards.expiry_hours`, default `72`) — a plain int CVar, its doc comment recording the 2-1 council vote and the textualist seat's preserved 7-day dissent. Re-posting is always a brand-new submission through the full write path/quota; there is no renew action anywhere in the code. `SeasonLedgerStore.SweepExpiredNotesAsync` physically deletes expired rows (storage hygiene only — `GetActiveNotesAsync`'s `expires_utc > $now` filter already guarantees an expired note is invisible regardless of sweep timing), run at `RoundStartingEvent`. |
| 5 | PROVIDENCE notices: system-labeled, own 2-notice quota, same gate, never reference a specific player/report/sanction/hidden note. | Every PROVIDENCE row carries `AuthorDisplay = Loc.GetString("solreign-noticeboards-providence-author")` ("PROVIDENCE — SYSTEM NOTICE"); the client (`SolreignNoticeboardEntry`) additionally renders it in a distinct color. PROVIDENCE shares the SAME `TryPostNoteAsync`/daemon-classifier gate as any player note, with its own `ProvidenceCapacity=2` check inside the same atomic transaction (`NoticeboardStoreTests.ProvidenceQuota_TwoActive_ThirdRefused`) and NO per-author cooldown (quota-gated only, per the memo). The two static seed lines (`noticeboards.ftl`) are content-checked against a denylist (`report`/`hidden`/`moderator`/`sanction`/`player `) in `NoticeboardCopyTests.ProvidenceSeedLines_NeverReferenceAPlayerReportOrHiddenNote`, reading the REAL locale file off disk. |

## Production-activation gate (Constitution §8 — a HUMAN gate, not code)

`CCVars.SolreignNoticeboardsEnabled` (`solreign.noticeboards.enabled`) **ships FALSE**. Its doc
comment records, verbatim in spirit: this flag alone is not a production go — the C2 memo's 3-0
vote holds that activating this surface ahead of the Moderation Constitution's own Section 8
rollout gate (staff tabletop drill + dated `ACKNOWLEDGMENTS.md`, both currently `NOT PERFORMED`
per `docs/MODERATION-CONSTITUTION.md` and `docs/moderation/TABLETOP-DRILL.md` in the orch-ops
repo) would use a documentation gap against the Constitution's purpose. Design, implementation,
and staging/dev testing may run with this CVar true in a non-production config; flipping it true
on the live box is John's call after that drill, never an automated or Claude-initiated flip. This
system has no code path that checks or enforces the drill itself — that gate is intentionally
outside code's authority.

## What was built (game repo, `~/AI/solreign-trees/noticeboards`, branch `feat/noticeboards`)

| Area | Files | Notes |
|---|---|---|
| CVars | `Content.Shared/CCVar/CCVars.Solreign.cs` (append) | `SolreignNoticeboardsEnabled` (bool, false), `SolreignNoticeboardExpiryHours` (int, 72). |
| Shared BUI contract | `Content.Shared/_Solreign/Noticeboards/SharedSolreignNoticeboards.cs` | `SolreignNoticeboardUiKey`, `SolreignNoticeboardNoteView` (no account identifier ever shipped to a client), `SolreignNoticeboardUiState`, `SolreignNoticeboardPostMessage`, `SolreignNoticeboardReportMessage`. |
| Server component | `Content.Server/_Solreign/Noticeboards/SolreignNoticeboardComponent.cs` | Marker + one `BoardId` DataField (default `"main"`) — the cross-round persistence key, since boards aren't map-placed this wave and entity uids reset every round. |
| Pure rules/copy | `Content.Server/_Solreign/Noticeboards/NoticeboardRules.cs`, `NoticeboardCopy.cs` | The closed spec numbers + text sanitizer (`BountyClaimRules` idiom); loc-key lookups + Providence seed-line rotation. |
| Main system | `Content.Server/_Solreign/Noticeboards/SolreignNoticeboardSystem.cs` | Board-open projection (direct `async void` ledger read, the Mark Garden idiom); posting (`Task.Run` + `ConcurrentQueue` drained in `Update()`, the Bounty/Oracle idiom — daemon call, then atomic ledger commit only on approval); report/hide (direct ledger write, no daemon); PROVIDENCE seeding on `MapInitEvent`; expiry sweep on `RoundStartingEvent`. |
| Admin tool | `Content.Server/_Solreign/Noticeboards/NoticeboardUnhideCommand.cs` | `noticeboardunhide <id>`, `[AdminCommand(AdminFlags.Moderator)]`. |
| Ledger schema+store | `Content.Server/_Solreign/SeasonLedger/SeasonLedgerStore.cs` (edit: schema doc + 2 new tables, `CurrentSchemaVersion` 4->5), `SeasonLedgerStore.Noticeboards.cs` (new) | `noticeboard_notes` + `noticeboard_authors` tables, additive `CREATE TABLE IF NOT EXISTS`. `TryPostNoteAsync`'s atomic transaction is the rule engine (see checklist row 2). |
| Ledger delegation | `Content.Server/_Solreign/SeasonLedger/SeasonLedgerSystem.Noticeboards.cs` (new) | Thin pass-through, the `SeasonLedgerSystem.Mark.cs` pattern — no second `SeasonLedgerStore` instance. |
| Client | `Content.Client/_Solreign/Noticeboards/SolreignNoticeboardBoundUserInterface.cs`, `UI/SolreignNoticeboardWindow.xaml(.cs)`, `UI/SolreignNoticeboardEntry.xaml(.cs)` | The `SolreignBountyBoardWindow` layout/char-count/clamp idiom, plus a per-row Report button and a PROVIDENCE color treatment. |
| Prototype | `Resources/Prototypes/_Solreign/Entities/noticeboard.yml` | `SolreignNoticeboard`, `parent: BaseWallmountMetallic`, reuses the upstream `Structures/Wallmounts/noticeboard.rsi` asset at a DIFFERENT state (`notice-3`, papers-pinned) than `SolreignContractsBoard`'s bare `noticeboard` state, so the two read as distinct at a glance — no new art this lane. |
| Locale | `Resources/Locale/en-US/_solreign/noticeboards.ftl` | Deliberately plain/crew-facing voice, NOT the acid-green corporate voice of bounties.ftl/contracts.ftl. PG-13 throughout. |
| Orphan allowlist | `Resources/_Solreign/orphan_allowlist.yml` (append) | `SolreignNoticeboard` — admin-spawnable only this wave, the `SolreignMarkGarden` precedent. |
| Tests | `Content.Tests/_Solreign/NoticeboardRulesTests.cs`, `NoticeboardStoreTests.cs` (new); `Content.IntegrationTests/Tests/_Solreign/NoticeboardIntegrationTest.cs` (new) | See coverage section below. |
| Regression fix | `Content.Tests/_Solreign/SeasonLedgerStoreTests.cs` (edit) | Two pre-existing tests hardcoded the old schema version literal `4`; repointed at `SeasonLedgerStore.CurrentSchemaVersion` so they track the constant instead of a stale literal. |

## What was built (daemon repo, `~/AI/solreign-director`, branch `feat/noticeboard-screen` off `main`, never committed to `main`)

| Area | Files | Notes |
|---|---|---|
| Surface budget | `orchestrator/moderation.py` (edit) | `MAX_CHARS_BY_SURFACE["noticeboard"] = 260`, `_FALLBACKS["noticeboard"]` — both additive, zero risk to any existing surface (verified: full 539-test suite green, no regression). |
| Route | `orchestrator/server.py` (edit) | `POST /api/noticeboard/post`, `dependencies=[Depends(require_director_signature)]` — same signed-request/signed-response substrate every other Director route uses. Pure classification (no LLM generation), so no `LLMUnavailableError` path exists here; `moderate_output()`'s internal fail-closed-by-returning contract IS the failure handling. `player_guid` accepted empty for a PROVIDENCE seed post (no identity role in the safety decision). |
| Tests | `tests/test_noticeboard.py` (new, 14 tests) | Surface budget/fallback pinning, unconfigured-classifier fail-closed, safe/flagged classifier outcomes, blocklist/too-long prefilter, route auth (401 unsigned), route never 503s on an unconfigured classifier (only genuine crashes would 503 — none exist in this path), signed response, empty-guid Providence path, rejection never echoes the submitted text. |

## Test coverage — the honest shape of it (scope boundary, not hidden)

Three layers, each proving what it can reach; no single automated test drives the FULL
`player posts -> daemon approves -> note visible` round trip, because that needs a live or
in-process-faked HTTP daemon answering `DirectorChannel`'s signed request/response inside the SS14
integration harness — out of this lane's foreground budget. What IS proven, end to end, without
any daemon:

1. **Daemon pytest** (`tests/test_noticeboard.py`) proves the classifier gate itself: budget,
   fallback, fail-closed-as-200 (not 503), auth, signed response, rejection never echoes text.
2. **Game unit tests** (`Content.Tests/_Solreign/NoticeboardRulesTests.cs`,
   `NoticeboardStoreTests.cs`) exhaustively prove the atomic rule engine — every quota/capacity/
   cooldown branch, hide/unhide (including the "never revive a note that expired while hidden"
   guard), the expiry sweep, and that the cooldown clock survives the swept note's own deletion —
   directly against `SeasonLedgerStore`, no ECS, no daemon.
3. **Game integration tests** (`Content.IntegrationTests/Tests/_Solreign/NoticeboardIntegrationTest.cs`)
   prove the ECS wiring those first two layers cannot reach: dormancy (`enabled=false`) is
   genuinely zero-behavior even with a spawned board and an active post/seed attempt; a post
   attempt with the Director channel unconfigured (empty token — the same shape as a genuinely
   offline daemon) fails closed and writes NOTHING (the "pending forever" outcome the memo
   describes for a dead daemon — no garbage row, ever, because nothing is written before
   approval); and the board-open PROJECTION and report/HIDE paths (neither of which touches the
   daemon) work end-to-end against rows written through the exact same atomic store API the real
   post-approval commit uses.

Combined, every individual link in the chain — classify, atomically-authorize-and-write, read,
hide — is covered; only the full four-hop wire is not exercised by one test. If this lane's
scope-verdict later requires wire-level coverage before John's Section 8 gate closes, the fake
daemon would be built as an in-process `HttpListener` inside the integration test signing its
responses via `DirectorChannel`'s own `internal` canonical-envelope helpers (already
`InternalsVisibleTo`-exposed to `Content.IntegrationTests`) — scoped out here as a deliberate,
documented boundary, not an oversight.

## [needs verification] resolutions found during the build

- **The task brief's referenced docs** ("Moderation Constitution v3", "Role Matrix",
  "Appeals-and-Retention doc", `deploy/validate_moderation_policy.py`) do not exist in
  `solreign-director` — they live in the **orch-ops** repo
  (`docs/MODERATION-CONSTITUTION.md`, `docs/moderation/ROLE-MATRIX.md`,
  `docs/moderation/APPEALS-AND-RETENTION.md`, `deploy/validate_moderation_policy.py`). Read in
  full before building; their content shaped the report/hide retention posture (routine hide/
  report records are logged, never reused for the Season Ledger/leaderboards/personalization —
  the note CONTENT itself, which the C2 memo explicitly does put in the Season Ledger, is a
  separate thing from the moderation-CASE record, and the two are not conflated here).
- **Orphan-reachability gate**: initially assumed (wrongly, by analogy to the Bounty/Contracts
  boards appearing allowlist-free) that a plain admin-spawnable entity needs no
  `orphan_allowlist.yml` entry. `Content.Tests._Solreign.SolreignOrphanReachabilityTest` caught
  `SolreignNoticeboard` as unreachable; resolved with an allowlist entry mirroring the
  `SolreignMarkGarden` precedent exactly (see table above).
- **Cross-test board-id collision**: the first integration-test run showed a false failure — a
  note fixture from one dirty test leaking into another dirty test's read, because both defaulted
  to the same `BoardId = "main"` against what turned out to be a shared Season Ledger SQLite file
  within that test run. Fixed by giving every integration test its own `FreshBoardId()` (a random
  `test-<guid>` id) rather than relying on pool/file isolation assumptions that didn't hold up —
  the same isolation discipline `NoticeboardStoreTests.GetActiveNotes_ScopedToBoardId` already
  pins at the store level.
- **`SeasonLedgerStoreTests`** had two pre-existing assertions hardcoding the schema version
  literal `4`; the schema bump to `5` (new tables) broke them until repointed at
  `SeasonLedgerStore.CurrentSchemaVersion`.

## VERIFY (blocking, Release)

| Check | Result |
|---|---|
| `dotnet build -c Release` (full solution) | 49 projects, **0 errors** (44 pre-existing warnings, none in touched files) |
| `dotnet build Content.Tests -c Release` | 0 errors |
| `dotnet build Content.IntegrationTests -c Release` | 0 errors |
| `dotnet build Content.YAMLLinter -c Release` | 0 errors |
| `dotnet ./bin/Content.YAMLLinter/Content.YAMLLinter.dll` | **No errors found** (75.2s) |
| `dotnet test Content.Tests -c Release --no-build` | **2491 passed / 3 skipped (known)** — baseline ~2455/3 + 36 new unit tests |
| `dotnet test Content.IntegrationTests -c Release --no-build --filter FullyQualifiedName~Solreign` | **342 passed / 1 skipped (known)** — baseline 338/1 + the 4 new integration tests. (One earlier attempt of this same suite aborted mid-run at ~4.3 GB allocated with 140 spurious in-flight "failures" while three other sessions' dotnet builds saturated the box — a machine-load artifact, re-run clean immediately after; recorded here for honesty, not as a code event.) |
| `.venv/bin/python -m pytest tests/ -q` (solreign-director) | **539 passed** — baseline 525 + 14 new |

## Branch + tip SHAs

- Game (`~/AI/solreign-trees/noticeboards`): `feat/noticeboards`, based on `origin/master` @
  `dcb87e1ace6d111b8761f25dbc0c330e91777220`. Tip = the single commit carrying this whole lane
  (this receipt included); SHA in the lane's final message.
- Daemon (`~/AI/solreign-director`): `feat/noticeboard-screen`, based on `main`, tip
  `0bf3c84e592686b3259917e1f32d2c8437eb419e`. Never committed to `main`.

## Receipt paths

- Game: `docs/receipts/NOTICEBOARDS-2026-07-17.md` (this file, `~/AI/solreign-trees/noticeboards/docs/receipts/NOTICEBOARDS-2026-07-17.md`).
- Daemon: no established `docs/receipts/` convention in `solreign-director`; the equivalent
  provenance lives in `tests/test_noticeboard.py`'s own module docstring plus this game-repo
  receipt's daemon-side table above (the two repos are reviewed together as one lane).
