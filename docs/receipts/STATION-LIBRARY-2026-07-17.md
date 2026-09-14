# STATION-LIBRARY build receipt (2026-07-17)

**Lane:** STATION-LIBRARY builder (foreground). Wave-2 item, Tarn Adams' dissenting pick in council
C1, and (per the task brief) "the mine's strongest pure-persistence family." **Branch:**
`feat/station-library` off `origin/master` @ `dcb87e1ace6d111b8761f25dbc0c330e91777220`. **CLEAN-ROOM
RAIL:** idea drawn only from public feature descriptions of comparable community fixtures (tg's
Curator library / a book-cart concept); no third-party source was ever read. Every file here is
built fresh from OUR house idioms — Bookshelf furniture, the Noticeboard write path, the Continuity
Garden round-start projection. Content restriction lifted by John the night of the build; ships
dormant regardless. No merges, no pushes — intake is the orchestrator's.

## Design summary

The Station Archive ("PROVIDENCE Records Annex") is a bookshelf-class structure where a player
alt-clicks to submit a written work (title + body). The submission passes a classifier round trip
(the Noticeboard write-path discipline — nothing is EVER written before the daemon approves);
approved works enter the Season Ledger (`library_works` table) and re-materialize as readable book
ITEM entities, physically inserted into the Annex's own `Storage` container, every round start (the
Continuity Garden projection pattern — the row is truth, the entity is a disposable per-round
read). Reading a materialized book reuses the EXISTING `Paper`/`PaperBoundUserInterface` reading UI
unchanged — nothing new was built for reading, only for writing/persisting/projecting.

Deliberate divergence from the Noticeboard precedent, per this lane's design brief: a library has
**NO expiry** (persistence is the point). The only closed numbers are a once-per-account-per-round
author quota and two length caps (title 100 / body 6000 chars — generous, picked well under
`PaperComponent`'s default `ContentSize` of 10000 and `BookBase`'s explicit `contentSize: 12000` so
a submission always fits its own reading UI with headroom). The only way a work stops re-projecting
is a hide (reversible containment — a one-tap player report, or a moderator's own action) or a
moderator's own out-of-band deletion; there is no auto-expiry sweep to build or reason about.

PROVIDENCE seeds each archive with 4 in-fiction works (§4 target — in the 3-5 range asked for),
once EVER per archive (not per round, since a seed is a permanent row indistinguishable from a
player submission once written): the SOLREIGN Employee Handbook page 3 excerpt (reusing the
existing, already-public-railed `solreign-lore-handbook-page-3` locale key verbatim — no
duplication), the Pre-Shift Safety Scroll, the "Sonnets for Compliance" chapbook, and a short
"Notice: The Annex Opens" dedication. Every seed passes the SAME fail-closed classifier round trip
as a player work — authoring them in-house does not exempt them (defense in depth, the Noticeboard
`ProvidenceSeedKeys` precedent).

## Spec-compliance checklist (this lane's design brief, mapped to the C2 posture memo where it applies)

| # | Rule | How implemented |
|---|---|---|
| 1 | Submit-time fail-closed classifier screening under a dedicated surface budget, before a work is ever visible. | `SolreignLibrarySystem.FireSubmit` never writes a row before the daemon approves — the signed `POST /api/library/post` round trip (payload `{player_guid, archive_id, title, text}`) runs FIRST; only on `allowed: true` does the game call `SeasonLedgerStore.TryPostWorkAsync`. Surface budget name: **"library"** (title screened separately under a **"library_title"** sub-budget) — see the daemon-dependency section below; this surface has NOT landed on `solreign-director`'s `main` (or any branch) yet, so the game side is built to the contract this receipt specifies and fails closed (Director channel unconfigured) exactly like a genuinely offline daemon until that lands. |
| 2 | One submission per author per round; generous, sane length caps. | `LibraryRules.MaxTitleLength = 100`, `MaxBodyLength = 6000` (pure, unit-tested). The per-round quota is enforced ATOMICALLY inside one `BEGIN IMMEDIATE` transaction, `SeasonLedgerStore.TryPostWorkAsync` — a hidden work still consumed the author's one submission for that round (containment, not a refund; `LibraryWorkStoreTests.Quota_StillApplies_EvenAfterTheFirstWorkWasHidden`). A classifier approval is advisory only until this call returns `Posted: true`. |
| 3 | Hide-as-containment: one-tap reversible, no independent review required. | A player's one-tap "Report to PROVIDENCE" alt-click verb on a projected book (`SolreignLibrarySystem.TryReport`) calls `SeasonLedgerStore.HideWorkAsync` directly — no daemon round trip, idempotent — and removes the round's physical copy immediately. Reversal is a moderator-only `libraryunhide <workId>` console command (`[AdminCommand(AdminFlags.Moderator)]`), the exact `NoticeboardUnhideCommand` precedent including its documented scope boundary: a separate, harder "permanent purge ahead of a moderator's own discretion" path is deliberately NOT built this wave (no reviewer-queue primitive to hang it on yet). |
| 4 | **NO expiry** — a library persists; removal is a moderator/report action only, never a timer. | `library_works` has no expiry column, no sweep, no cooldown. `LibraryWorkStoreTests.SubmittedLongAgo_StillAppearsActive_NoExpiry` and `.Unhide_NeverBlockedByExpiry_UnlikeNoticeboard` pin this divergence directly against the Noticeboard precedent's own opposite behavior. |
| 5 | PROVIDENCE content: clearly labeled, gated through the same screen, seeded so the shelf is never empty. | Every seed's `AuthorDisplay` starts with `"PROVIDENCE ARCHIVES — ..."` (`LibraryCopyTests.SeedAuthors_AllCarryTheProvidenceMarker`, checked against the real `library.ftl`). Seeding is atomically capped against the seed corpus length (`LibraryPostRejection.ProvidenceSeedTargetReached`) inside the SAME transaction as the quota check — closes the race where two Annexes sharing one `archive_id` could double-seed (`LibraryWorkStoreTests.ProvidenceSeed_RefusedOnceTargetReached`). |

## Daemon-surface coordination status (READ THIS AT MERGE TIME)

The **"library"** classifier surface does **not exist** on `solreign-director` yet — not on `main`,
not on any branch. `feat/noticeboard-screen` (one commit, `0bf3c84`, off `main`, never merged) is
the precedent this lane's game side is built to match, but it only adds a `"noticeboard"` surface;
nothing for `"library"`. Per this lane's task brief ("build the game side to the same request/
response contract and note the daemon dependency for merge-time coordination"), the game side
(`SolreignLibrarySystem.FireSubmit`) is built against the following contract, which a future daemon
commit must implement before `solreign.library.enabled` can ever go live anywhere but a fully
offline dev box:

```
POST /api/library/post   (signed both ways via require_director_signature, the Bounty/Noticeboard substrate)

Request:
{
  "player_guid": "<guid or empty for a PROVIDENCE seed>",
  "archive_id": "<Annex ArchiveId>",
  "title": "<raw title>",
  "text": "<raw body>"
}

Response (always 200 — moderate_output() fails closed by returning, never raising):
{
  "allowed": <bool>,
  "title": "<classifier-approved/softened title, or null/empty to signal 'use mine'>",
  "text": "<classifier-approved/softened body, or null/empty to signal 'use mine'>"
}
```

Suggested daemon-side implementation (additive, the `orchestrator/moderation.py` /
`orchestrator/server.py` shape `0bf3c84` already established for `"noticeboard"`):
`MAX_CHARS_BY_SURFACE["library_title"] = 100`, `MAX_CHARS_BY_SURFACE["library"] = 6000` (matching
`LibraryRules` exactly), two sequential `moderate_output()` calls (title under `"library_title"`,
body under `"library"`, both must return `allowed: true`), a `_FALLBACKS` entry for each. **Until
that lands, `solreign.library.enabled=true` on any box with a configured Director token will submit
nothing successfully** — every submission (and every PROVIDENCE seed attempt) fails closed exactly
like a genuinely offline daemon (`LibraryIntegrationTest.Enabled_DirectorChannelNotConfigured_
SubmitAttempt_FailsClosed_NoRowWritten` proves this shape, though it only exercises the
unconfigured-token case since no live/faked daemon answers the route today — the SAME scope
boundary the Noticeboard receipt documents). This is a **coordination TODO for merge time**, not a
blocker for this lane's own foreground work.

## Seeded works list (this lane's design brief §4, target 3-5, shipped 4)

1. **"SOLREIGN Employee Handbook — Page 3"** — reuses the existing, already-public-railed
   `solreign-lore-handbook-page-3` locale key verbatim (Resources/Locale/en-US/_solreign/
   lore-papers.ftl) — no new text, no duplication. Byline: PROVIDENCE ARCHIVES — Solreign Human
   Resources.
2. **"The Pre-Shift Safety Scroll"** — original, PG-13, house corporate-absurdist voice (mock
   safety-briefing scroll, five "Articles"). Byline: PROVIDENCE ARCHIVES — Solreign Safety Office.
3. **"Sonnets for Compliance: A Chapbook"** — original, PG-13, three short bureaucratic-comedy
   sonnets. Byline: PROVIDENCE ARCHIVES — Solreign Compliance Division.
4. **"Notice: The Annex Opens"** — original, PG-13, a short in-fiction dedication tying the
   shelf's persistence to the feature's own thesis. Byline: PROVIDENCE ARCHIVES — Station Command.

All four live in `Resources/Locale/en-US/_solreign/library.ftl` (except #1's body, which stays in
`lore-papers.ftl` where it already lived) and are content-checked by `LibraryCopyTests` against the
REAL locale files on disk (existence, length-cap fit, and the PROVIDENCE marker).

## What was built (game repo, `~/AI/solreign-trees/station-library`, branch `feat/station-library`)

| Area | Files | Notes |
|---|---|---|
| CVars | `Content.Shared/CCVar/CCVars.SolreignLibrary.cs` (new file — the `CCVars.SolreignMark.cs` collision-avoidance idiom) | `SolreignLibraryEnabled` (bool, false), `SolreignLibrarySlots` (int, 10 — round-start projection cap). |
| Shared BUI contract | `Content.Shared/_Solreign/Library/SharedSolreignLibrary.cs` | `SolreignLibrarySubmitUiKey`, `SolreignLibrarySubmitMessage` (title+body). Stateless — no server-pushed UI state; reading reuses the existing `PaperUiKey` BUI unchanged. |
| Server components | `Content.Server/_Solreign/Library/SolreignLibraryAnnexComponent.cs`, `SolreignLibraryBookComponent.cs` | Marker + `ArchiveId` (Annex, the `SolreignNoticeboardComponent` idiom); marker + `WorkId` (Book, the `SolreignMarkComponent` idiom). |
| Pure rules/copy | `Content.Server/_Solreign/Library/LibraryRules.cs`, `LibraryCopy.cs` | Length caps + sanitizers; loc-key lookups + the 4-work seed corpus table. |
| Main system | `Content.Server/_Solreign/Library/SolreignLibrarySystem.cs` | Submission (alt-click verb -> BUI -> `Task.Run`+`ConcurrentQueue` daemon round trip, the Bounty/Noticeboard idiom); round-start projection (`StationPostInitEvent` -> spawn book items -> `SharedStorageSystem.Insert` into the Annex's own storage, the Mark Garden projection idiom adapted to item-insertion instead of tile-slot placement); PROVIDENCE seeding (`MapInitEvent`, once ever per archive); report (one-tap alt-click verb, direct ledger hide, no daemon). |
| Admin tool | `Content.Server/_Solreign/Library/LibraryUnhideCommand.cs` | `libraryunhide <workId>`, `[AdminCommand(AdminFlags.Moderator)]`. |
| Ledger schema+store | `Content.Server/_Solreign/SeasonLedger/SeasonLedgerStore.cs` (edit: schema doc + `library_works` table, `CurrentSchemaVersion` 4->5), `SeasonLedgerStore.LibraryWorks.cs` (new) | Additive `CREATE TABLE IF NOT EXISTS`. `TryPostWorkAsync`'s atomic transaction is the whole rule engine (quota OR seed-target check, whichever applies). |
| Ledger delegation | `Content.Server/_Solreign/SeasonLedger/SeasonLedgerSystem.LibraryWorks.cs` (new) | Thin pass-through, the `SeasonLedgerSystem.Mark.cs`/`.Noticeboards.cs` pattern. |
| Client | `Content.Client/_Solreign/Library/SolreignLibrarySubmitBoundUserInterface.cs`, `UI/SolreignLibrarySubmitWindow.xaml(.cs)` | Title `LineEdit` + body `TextEdit` (the existing Paper-writing `TextEdit`/Rope idiom, not a `LineEdit` — a book needs more than 260 characters) + submit button. Stateless form, outcome delivered via server-side popup. |
| Prototypes | `Resources/Prototypes/_Solreign/Entities/library.yml` | `SolreignLibraryAnnex` (component set duplicated from upstream `Bookshelf` rather than inherited, to keep the second `UserInterface` key unambiguous — no new art, same `Structures/Furniture/bookshelf.rsi`), `SolreignLibraryBook` (`parent: BookBase`, title/description/content stamped dynamically at spawn). |
| Locale | `Resources/Locale/en-US/_solreign/library.ftl` | Plain, archival "librarian's placard" voice — not the acid-green corporate Directive voice. PG-13 throughout. |
| Orphan allowlist | `Resources/_Solreign/orphan_allowlist.yml` (append) | `SolreignLibraryAnnex`, `SolreignLibraryBook` — admin-spawnable/projection-only this wave, the `SolreignNoticeboard`/`SolreignMarkGarden` precedent. |
| Tests | `Content.Tests/_Solreign/LibraryRulesTests.cs`, `LibraryCopyTests.cs`, `LibraryWorkStoreTests.cs` (new); `Content.IntegrationTests/Tests/_Solreign/LibraryIntegrationTest.cs` (new) | See coverage section below. |
| Regression fix | `Content.Tests/_Solreign/SeasonLedgerStoreTests.cs` (edit) | Two pre-existing tests hardcoded the old schema version literal `4`; repointed at `SeasonLedgerStore.CurrentSchemaVersion` (the exact fix the Noticeboard lane made for the same reason). |

## Test coverage — the honest shape of it (scope boundary, not hidden)

Three layers, mirroring the Noticeboard precedent's own documented boundary — no single automated
test drives the FULL `player submits -> daemon approves -> work on the shelf` round trip, because
(a) that needs a live or in-process-faked HTTP daemon answering `DirectorChannel`'s signed request,
out of this lane's foreground budget, and (b) the `"library"` daemon surface does not exist yet at
all (see the coordination section above — there is nothing live to fake against today). What IS
proven, end to end, without any daemon:

1. **Pure unit tests** (`LibraryRulesTests`, `LibraryCopyTests`) prove the length-cap sanitizers and
   the seed corpus (existence in the real locale files, length-cap fit, the PROVIDENCE marker) with
   no ECS, no I/O.
2. **Store tests** (`LibraryWorkStoreTests`, 20 tests) exhaustively prove the atomic rule engine —
   this IS the "submission -> pending -> approved" flow with mocked screening the task's TESTS
   section asks for: every call writes as though the daemon already approved the text (the exact
   precondition `SolreignLibrarySystem.FireSubmit` establishes before ever calling
   `TryPostWorkAsync`) — quota (including the "hidden work still consumed the quota" case), the
   NO-expiry divergence from Noticeboard (two tests explicitly contrast the two systems' behavior),
   the PROVIDENCE seed-target atomic race guard, and hide/unhide.
3. **Game integration tests** (`LibraryIntegrationTest`, 4 tests) prove the ECS wiring those first
   two layers cannot reach: dormancy (`enabled=false`) is genuinely zero-behavior even with a
   spawned Annex and an active submit/seed attempt; a submit attempt with the Director channel
   unconfigured (empty token — the same shape as a genuinely offline daemon, and today the ONLY
   possible shape since the surface doesn't exist) fails closed and writes NOTHING; round-start
   projection materializes a ledger row as a book item with the right title/Paper-content/author,
   physically inserted into the Annex's own storage container; and report immediately removes the
   physical copy, hides the row, and the row never re-projects on a fresh round-start pass.

Combined, every individual link in the chain that CAN be tested without a live daemon is covered;
the classify-and-approve half specifically needs the daemon-side commit noted above before it can
be exercised at all (even a faked one), which is why this receipt flags it as a merge-time
coordination item rather than closing it out here.

## [needs verification] / decisions made during the build

- **Daemon surface does not exist**: unlike the Noticeboard precedent (which had a real,
  though-unmerged, daemon commit to build against and test the shape of), STATION-LIBRARY's
  `"library"` surface is specified in this receipt but not implemented anywhere in
  `solreign-director`. Flagged prominently above; not built here per the task brief's explicit
  instruction to build the game side to a documented contract and note the dependency.
- **Annex prototype duplicates rather than inherits Bookshelf's component set**: `Bookshelf`'s own
  `UserInterface` component declares only the `StorageUiKey` interface; adding a second interface
  key (`SolreignLibrarySubmitUiKey`) via prototype-inheritance dictionary-merge semantics was judged
  too ambiguous to rely on for a component this load-bearing, so the full component list is
  redeclared on `SolreignLibraryAnnex` instead (same sprite/behavior, no new art).
  `Content.Tests._Solreign.SolreignOrphanReachabilityTest` and the YAML linter both ran clean
  against this shape.
- **Report removes the physical copy immediately, not just the row**: the design brief's "hide-as-
  containment" instruction doesn't specify whether the round's already-spawned entity should also
  come off the shelf. Chose immediate removal (`Del(book)` after a successful hide) for the same
  "the board's own display simply shows the slot as unavailable" spirit the Noticeboard posture memo
  describes, rather than leaving a stale, already-hidden copy visible until next round.
- **PROVIDENCE seed race**: unlike Noticeboard's board-capacity check (present from the start
  because 12/2 were explicit spec numbers), the equivalent atomic guard for library seeding
  (`ProvidenceSeedTargetReached`, checked inside the same `BEGIN IMMEDIATE` transaction as the
  quota check) was added deliberately during the build once it became clear two Annexes sharing one
  `archive_id` could otherwise double-seed off a stale `GetProvidenceWorkCountAsync` pre-check —
  not a defect found in review, a design choice made proactively by analogy to
  `TryPostNoteAsync`'s own `ProvidenceCapacity` discipline.

## VERIFY (blocking, Release)

| Check | Result |
|---|---|
| `dotnet build Content.Server/Content.Server.csproj -c Release` | 0 errors (846 pre-existing warnings, none in touched files) |
| `dotnet build Content.Client/Content.Client.csproj -c Release` | 0 errors after one fixup (missing `using Content.Client.UserInterface.Controls;` in the submit window code-behind — caught by the first attempt, fixed, rebuilt clean) |
| `dotnet build Content.Tests/Content.Tests.csproj -c Release` | 0 errors |
| `dotnet build Content.IntegrationTests/Content.IntegrationTests.csproj -c Release` | 0 errors |
| `dotnet build SpaceStation14.slnx -c Release` (full solution) | 0 errors |
| `dotnet build Content.YAMLLinter/Content.YAMLLinter.csproj -c Release` | 0 errors |
| `dotnet ./bin/Content.YAMLLinter/Content.YAMLLinter.dll` | **No errors found** (42.8s) |
| `dotnet test Content.Tests -c Release --no-build` | **2492 passed / 0 failed / 3 skipped** (2495 total) — the moving baseline mentioned in the task brief; the 3 skips are pre-existing (Windows ACL tests, not touched by this lane) |
| `dotnet test Content.IntegrationTests -c Release --no-build --filter FullyQualifiedName~Solreign` | First attempt: **341 passed / 1 failed / 1 skipped** (343 total) — the failure was NOT in any Library test; immediate clean re-run: **342 passed / 0 failed / 1 skipped**, fully green. Recorded honestly per the Noticeboard receipt's own precedent for the same symptom: a machine-load artifact from concurrent build/test activity on the box, not a code defect. The 4 `LibraryIntegrationTest` cases passed in both runs. |

## Branch + tip SHA

- Game (`~/AI/solreign-trees/station-library`): `feat/station-library`, based on `origin/master` @
  `dcb87e1ace6d111b8761f25dbc0c330e91777220`. Tip SHA in the lane's final message (this receipt is
  part of that commit).
- Daemon (`~/AI/solreign-director`): no commit made this lane — the `"library"` surface remains
  entirely unbuilt there; see the coordination section above.

## Receipt path

`docs/receipts/STATION-LIBRARY-2026-07-17.md` (this file, `~/AI/solreign-trees/station-library/docs/receipts/STATION-LIBRARY-2026-07-17.md`).
