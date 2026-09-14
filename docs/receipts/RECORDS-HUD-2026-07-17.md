# RECORDS-HUD — Personnel Records Terminal build receipt (wave-2 item einstein-016)

**Date:** 2026-07-17 · **Lane:** RECORDS-HUD (wave-2 builder) · **Branch:** `feat/records-hud` off `origin/master` @ `dcb87e1ace`
**Item:** einstein-016, global mine rank #8 — surface the Season Ledger in-world.
**Posture:** content restriction lifted tonight; built freely, **ships DORMANT** behind
`solreign.records_terminal.enabled` (default **FALSE**). Zero daemon dependency — every read is the
local Season Ledger SQLite file.

## Clean-room provenance (the note this receipt carries)

The idea came from **Einstein Engines' public description** of a records computer with HUD status
icons. **No Einstein Engines source was read** — the feature was designed and built fresh from
SOLREIGN's own shipped idioms (the Contracts Board BUI wiring, the FirstShift private-snapshot
event, the SeasonLedger thin-delegation pattern, the MarkCopy closed-template discipline). Any
resemblance to the Einstein implementation is convergent, not derived.

**The "HUD status icons" half of the einstein idea is explicitly DEFERRED, not built.** A custom
client HUD overlay is engine-adjacent and heavier than a wave-2 cut warrants; the terminal IS the
wave-2 cut. If a later wave wants the icons, it starts from scratch under its own receipt.

## What the terminal renders (the section list)

A player interacts with the "**PROVIDENCE Personnel Records**" wall console
(`SolreignRecordsTerminal`) and sees THEIR OWN career record, six sections, closed `.ftl`
templates, house deadpan voice:

1. **LEDGER TITLE** — the current display title (`TitleRules.Compute`, with an admin grant masking
   the earned title exactly as ID cards/examine already do — `TitleGrantRules.ResolveDisplayTitle`).
2. **TOURS SERVED** — career tour count.
3. **CAREER STANDING** — the standing flavor word (`PersonnelFileRules.DescribeCareerStanding`
   over the career rank index, `RankProgression.ComputeCareerRank`) — same word the personnel-file
   examine uses, so the two surfaces can never disagree.
4. **SOCIAL FIRSTS** — milestone names for every claimed celebratory `social_firsts` flag, fixed
   order (chirp answered / healed by another / item received), reusing the shipped `-reason` keys
   from `social-cheap-adds.ftl` verbatim. `wingmate_prompt` deliberately excluded — it's an
   internal once-ever prompt marker with no ceremony copy, not a milestone. Empty state:
   "None recorded yet."
5. **FIRST DEATH** — commemoration status only, respectfully: "Statement of Record: on file." /
   "None. Keep it that way." Never the cause, character, or epitaph — this is a status read, not
   the crypt.
6. **CONTINUITY GARDEN MARK** — kind + freshly-computed wall-clock growth stage if planted,
   reusing `MarkCopy.KindWordKeyFor`/`StageKeyFor` and `MarkAgeRules.StageAt` so the terminal can
   never contradict the physical object's own examine line. Empty state: "No mark on file. The
   garden remembers who plants."

Directive-compliance streak was scoped out honestly: the shipped `StationDirective` feature is a
round-local announcement layer with **no per-player compliance data in the ledger** — rendering a
"streak" would mean fabricating a number, which the honesty rail forbids. If directives ever
persist per-player compliance, the section slots in then.

## Privacy: own record only — structural, not gated

- No message type in `RecordsTerminalUiMessages.cs` carries a target account. The server derives
  the account exclusively from the interacting session (`BoundUIOpenedEvent.Actor` /
  `RecordsTerminalRefreshMessage` sender). "Browse another player's file" is not a disabled code
  path; it does not exist. A moderator-facing variant is OUT of scope (matches the
  pseudonym/minors posture).
- The response is a **private, session-targeted network event**
  (`RecordsTerminalSnapshotEvent`, the shipped `FirstShiftPrivateSnapshotEvent` idiom) — never a
  shared `BoundUserInterfaceState`, which is networked to every client observing the entity and
  would leak player A's record to player B standing at the same console.

## The ID-card decision (feature 2): SKIPPED — already shipped, extension would be redundant

The brief asked to extend the ID-card examine surface with the Ledger title line "if cheap and
additive; skip if it risks the existing behavior." Verified how it works first:
`SeasonLedgerSystem.IdCardStanding.cs` **already stamps BOTH the title and the standing word** onto
the card (`SeasonStandingIdCardComponent.Title`/`.Standing`), and the examine line
(`solreign-id-card-standing-examine` in `season-ledger.ftl`) **already renders the title**:
"Employee badge: '{$title}' — {$standing} standing on file." There is no missing title line to
add. The only candidates left were re-orderings or duplicate lines inside a subtle, shipped
mechanism (the fresh-account un-stamp path, the PDA relay, the foreign-card guard) — risk with no
new information. Decision: **no change to the ID-card surface; nothing was cheap AND additive
because the feature is already complete.**

## Files

| File | Change |
|---|---|
| `Content.Shared/CCVar/CCVars.SolreignRecordsTerminal.cs` | NEW — `solreign.records_terminal.enabled`, SERVERONLY, **default false** (own partial file, the D0 collision-control idiom) |
| `Content.Shared/_Solreign/Records/RecordsTerminalUiMessages.cs` | NEW — UI key, wire snapshot (pre-rendered text only), private session-targeted snapshot event, refresh message |
| `Content.Server/_Solreign/Records/SolreignRecordsTerminalComponent.cs` | NEW — server-only marker component (the SolreignOracle precedent) |
| `Content.Server/_Solreign/Records/RecordsTerminalRenderer.cs` | NEW — pure plan builder: closed celebrated-flag order, honest empty states, corrupt-mark-row fallback to "no mark", injected clock |
| `Content.Server/_Solreign/Records/SolreignRecordsTerminalSystem.cs` | NEW — ECS glue: CVar gate (off = force-close + zero ledger reads), async read → main-thread-safe private push, the ONLY `Loc.GetString` site |
| `Content.Server/_Solreign/SeasonLedger/SeasonLedgerSystem.RecordsTerminal.cs` | NEW partial — one bundled ledger read (career stats + admin title + social firsts + first death + mark) through the single shared store, the thin-delegation law |
| `Content.Client/_Solreign/Records/RecordsTerminalClientSystem.cs` | NEW — receives only session-targeted snapshots (FirstShiftClientSystem idiom) |
| `Content.Client/_Solreign/Records/RecordsTerminalBoundUserInterface.cs` | NEW — dumb window host; Refresh → message |
| `Content.Client/_Solreign/Records/UI/RecordsTerminalWindow.xaml(+.cs)` | NEW — FancyWindow, six labeled sections + Refresh (Contracts Board layout idiom) |
| `Resources/Prototypes/_Solreign/Entities/records_terminal.yml` | NEW — `SolreignRecordsTerminal` wallmount (screen.rsi, the DirectiveTerminal fixture); **not map-placed this wave** |
| `Resources/Locale/en-US/_solreign/records-terminal.ftl` | NEW — closed copy pack; reuses shipped social-first/Mark keys rather than duplicating |
| `Resources/_Solreign/orphan_allowlist.yml` | +`SolreignRecordsTerminal` entry (honest reason: dormant + map placement deferred; remove when mapped) |
| `Content.Tests/_Solreign/RecordsTerminalRendererTests.cs` | NEW — 15 unit tests: fresh / decorated-veteran / dead-once / marked permutations, flag ordering + exclusion + unknown-flag safety, stage math at 0/10/30 days, corrupt kind/timestamp → honest no-mark, fully-decorated combined |
| `Content.IntegrationTests/Tests/_Solreign/RecordsTerminalIntegrationTest.cs` | NEW — 2 e2e tests: dormant ship-posture (CVar defaults false; open force-closes; kill switch) and enabled-path (window stays open; the real ledger read completes for a real account) |

## Verify (Release, blocking)

| Suite | Result |
|---|---|
| Build (`SpaceStation14.slnx`, Release) | clean — 0 errors |
| Content.Tests (full) | **2470 passed, 0 failed, 3 skipped** (2473 total; skips are pre-existing Windows/alert-manager env skips) |
| Content.IntegrationTests `~Solreign` (all 47 fixtures, run in 4 blocking chunks under batch-battery machine load — chunk union verified equal to the `~Solreign` filter, no Solreign-FQN fixture exists outside `Tests/_Solreign/`) | **240 passed, 0 failed, 1 skipped** (241 total; 49+57+63+72) |
| YAML linter (`Content.YAMLLinter`) | **No errors** |

One mid-verify finding, fixed in-lane: the new entity tripped the Wave-22 orphan-reachability gate
(`SolreignOrphanReachabilityTest`) — resolved with an honest allowlist entry (dormant, map
placement deferred), the exact posture the gate exists to force into the open.

## Dormant-wake checklist (for John, later)

1. Flip `solreign.records_terminal.enabled true` in live config.
2. Map-place `SolreignRecordsTerminal` on a wall (or admin-spawn to trial) — then remove the
   orphan-allowlist entry.
3. Optional follow-ups, each its own lane: guidebook entry; directive-compliance section (needs
   per-player persistence first); the deferred HUD-icons half.
