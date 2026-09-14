# Lore Mystery Arc — "OPEN BUNK" (Subject 07 / S-07)

**Branch:** `feat/lore-mystery` (worktree `~/AI/solreign-trees/lore-mystery`, off `master`).
**Date:** 2026-07-16. **Status:** content complete, verified, committed on branch. No push/merge
(per instruction) — this is the owner's read.

---

## SPOILER-SAFE SUMMARY (safe to relay/publish a pitch from this section only)

**Arc name / OOC handle:** **Open Bunk** (formal HR designation: **Subject 07 / S-07**).

**One-line pitch:** A crew berth that's been permanently reserved and permanently vacant since
before anyone can remember, an asset that shows up on the Season Ledger with zero recorded
shifts and a climbing Standing, and a station that keeps re-filing the paperwork under a
different theory of what "it" actually is — a lost employee, a bureaucratic classification, or
something the books wrote down about themselves.

**Fragment count:** 10 findable objects, reachable in-game now via 2 map-placed spawner markers
(see Placement below). Delivered as: 7 Paper documents + 3 "recovered log" props (same
Paper-entity idiom this fork already uses for its Season 1 clues — melted clipboard, ghost
cabinet folder — reskinned as machine printouts/transcripts rather than new terminal UI).

**Mechanism hooks:** Station Directives (round-robin timing), the Season Ledger, the Crypt,
Wingmates, and the Oracle (via a found petition transcript, not a live terminal change) all get
a fragment tied to them — the mystery is meant to make those systems feel inhabited, not just
decorative.

**No resolution shipped, by design.** The arc leaves 2-3 live, mutually-exclusive theories in
players' heads and is built to extend across future seasons. The full solution (there isn't
one) and the fragment-by-fragment map are below, under SPOILERS — this section is the only part
safe to share publicly; the rest is the owner's read, never the public's.

---

## SPOILERS BELOW — arc bible, full fragment map

### Design process

Story-structure pass delegated to the Grok fleet (`~/AI/Tools/grk`) per the orchestration
instruction — given the full canon-facts brief (setting, existing systems, the exact
already-public "Subject 07" tease, and the already-queued-but-unposted lore-drip facts I must
not contradict) and asked for mystery-plotting craft only (fragment ordering, false-trail
placement), not new canon. Grok's blueprint proposed the "Open Bunk" handle, a person-vs-slot
vs-self-entry ambiguity, and a 10-fragment structure with a named false-trail character
("Korr," placeholder). I own final canon-consistency: renamed the false-trail character
(checked for collisions — none found), replaced the blueprint's "Oracle terminal rare
deflection line" hook with a found *petition transcript* Paper prop instead (the live Oracle
terminal's replies are LLM-generated at runtime via `SolreignOracleSystem`/the orchestrator —
not a static line pool — so a hard-coded "rare response" isn't achievable without touching that
system's C#, which is out of this lane's scope; a transcript found as a physical object
achieves the same narrative hook with zero engine risk). Declined the blueprint's optional
"warm mid-word metal plate" and "41-ring index" bonus fragments as separate objects — folded
their spirit into two existing fragments instead (F08, F09) rather than adding standalone props,
to keep the total at a clean 10 and avoid diluting the arc's own identity into being *only* a
bridge to the Discord drip queue's ongoing thread.

### Continuity check against existing canon (what this arc must not contradict)

- **Public site tease** (`website-v2/website/src/app/lore/page.tsx`, `#season-1` section):
  "It has questions about [Subject 07]... it may have questions about you, too" — this arc is
  built as the in-game answer to "what is that redacted name," without ever actually answering
  it.
- **Lore-drip queue** (`~/AI/Kolton-SS14/deploy/lore-queue/`, posts 03-27 queued + 01-02 already
  posted): the recurring "THE ENTRIES ARE NOT BALANCED" clipboard phrase (queued post 15), the
  ring of 41 unmatched ID cards at Leviathan (queued post 20), the Meridian atrium mass /
  A.U.D.I.T. self-redaction (queued posts 26-27), and the "fused metal, warm, letter mid-word"
  recurring find (queued posts 6, 27) are all **referenced in spirit, never contradicted, never
  resolved** — F08 (Subject-class designations are a liability queue, plural) and F09
  (pre-commission Ledger entry) are written so they're consistent with "there are 41 such
  designations" without asserting it outright; that stays the drip queue's reveal to make, not
  mine.
- **Directives are round-robin, not random** (already corrected in-fiction by queued post 16,
  and true of the live `StationDirectiveRuleSystem`): F07 states this explicitly and hangs the
  mystery hook (slot #7's hidden footnote) on the *fixed* rotation, never implying
  randomization.
- **The 20-page Employee Handbook set is closed** ("Twenty pages... survive," public copy; all
  20 numbered pages 3-200 already have locale text in `lore-papers.ftl`). F08 is explicitly
  stamped "Addendum 7-C... NOT FOR FILING WITH EMPLOYEE HANDBOOK. Separate volume." specifically
  so it cannot be mistaken for an undiscovered 21st handbook page.
- **No existing "Subject NN" numbering, no "Atwater" name collision** anywhere else in the
  Solreign content tree — checked via repo-wide grep before writing.

### The false trail

**Planted:** F01 (transfer denial), F02 (badge photo failure), F03 (berth card) all name a
specific person, **ATWATER, N.**, as Subject 07 — an emotionally legible "ghost employee" story
players will run with. F06's Crypt entry keeps that name alive alongside "Subject 07" as a
second, competing obituary.

**Undercut, never closed:** F08 (Subject-class = a bureaucratic liability/liquidation-queue
designation, not a legal identity — personnel can be temporarily *bound* to it) and F09 (the
Ledger's very first entry, predating the station's own commissioning, already carries "Subject
field: 07" with no name attached) both complicate the person theory without disproving it. F05's
impossible mentorship timeline (certification postdates the shift it certifies) does the same.
The arc never states which is true. F10 (Oracle transcript) refuses to answer either way on
purpose ("I do not speak the seventh... I gave you a chore").

**Live theories left standing:** (1) Atwater was a real person, later erased/liquidated, whose
Standing/berth/obituary linger; (2) "Subject 07" is a permanent administrative slot that people
get temporarily bound to — Atwater was just its most recent occupant; (3) the Ledger or the
Exchange wrote the entry about itself, and everything after is retrofitted paperwork. Nothing in
this arc picks a winner.

### Fragment-by-fragment map

| # | Entity id | Find | Reveals | System hook |
|---|---|---|---|---|
| F01 | `SolreignPaperS07TransferDenial` | Transfer denial slip | Names Atwater as Subject 07; "active review" has no end date | Handbook-adjacent (HR paperwork tone) |
| F02 | `SolreignPaperS07BadgePhotoMemo` | Badge photo exception memo | ID photo won't develop — "already occupied" | Security/Ledger-adjacent |
| F03 | `SolreignPaperS07BerthCard` | Berth assignment card | Berth 07 reserved, vacant, reassignment forbidden | Wingmate/housing adjacency (false-trail peak) |
| F04 | `SolreignPaperS07LedgerPrintout` | Ledger anomaly printout (thermal, "recovered log") | S-07 has Standing + 0 shifts + future timestamp; echoes "entries are not balanced" | **Season Ledger** (primary) |
| F05 | `SolreignPaperS07WingmateRoster` | Wingmate pairing log, torn page | Mentor certification postdates the mentee's first shift | **Wingmates** (primary) |
| F06 | `SolreignPaperS07CryptObituary` | Uncut obituary proof | Two obituaries, same death-index, one for Atwater, one for Subject 07 | **The Crypt** (primary) |
| F07 | `SolreignPaperS07DirectiveScrap` | Compliance scheduling note | States the Directive rotation is fixed, not drawn; slot 7 hides a standing "do not reassign Berth 07" instruction | **Station Directives** (primary, reinforces round-robin canon) |
| F08 | `SolreignPaperS07PolicyAddendum` | HR policy addendum, uncatalogued | "Subject" = liability classification, not identity; explicitly a separate volume from the Handbook | Handbook-adjacent (the undercut) |
| F09 | `SolreignPaperS07ArchiveSlip` | Archive slip, pre-commission ("recovered log") | Ledger's record 000001 predates the station; Subject field 07, name field "—SYSTEM—" | **Season Ledger** (origin-myth undercut) |
| F10 | `SolreignPaperS07PetitionTranscript` | Petition transcript, unlogged ("recovered log") | Oracle deflects identity questions; transcript has no matching entry in the terminal's own log | **The Oracle** (primary, via found transcript not live terminal) |

### Build

- **Prototypes:** `Resources/Prototypes/_Solreign/Entities/lore_mystery_subject07.yml` — 10
  fragment entities (all `parent: PaperOffice`, matching the existing Season 1 clue-paper
  idiom) + 2 `parent: MarkerBase` placement markers using the vanilla `RandomSpawner` component
  (same idiom as `SpawnMobSolreignUnicorn` in `Markers/verdant_markers.yml`) — each marker holds
  a 5-fragment weighted pool, spawns exactly one, then deletes itself, giving the same
  cross-round-accumulation feel as the existing Auditor's Memo trail / Handbook page hunt.
- **Locale:** `Resources/Locale/en-US/_solreign/lore-mystery-subject07.ftl` — 10 content keys.

### Placement (both are the only map edits in this branch)

Per the brief's placement guidance (prototype/spawn-table injection preferred; 1-entity map
edits are the sanctioned-small-edit precedent when a fixed location is truly needed) — no
generic prototype-level scatter mechanism exists in this fork for flavor lore (the original
Auditor's Memo/Handbook trail was placed by hand by a dedicated "mapping wave," not via a
spawn table), so I followed the verified minimal-edit precedent exactly (`SpawnMobSolreignUnicorn`
in `solreign_verdant.yml`): reuse an **existing, already-verified floor tile** (an existing
vanilla `RandomSpawner` instance's exact coordinate) rather than a freehand position, and append
a single new entity at the end of the map file.

| Map | File | Anchor reused | New uid | Marker |
|---|---|---|---|---|
| Meridian (central admin hub) | `Resources/Maps/_Solreign/solreign_meridian.yml` | `pos: -21.5,20.5 / parent: 30` (existing RandomSpawner uid 732) | 900564 (prior max 900563) | `SpawnSolreignS07PersonnelWing` — F01, F02, F03, F07, F08 |
| Leviathan (largest, least-surveyed facility — same facility as the queued "41-ring" find) | `Resources/Maps/_Solreign/solreign_leviathan.yml` | `pos: -18.5,-22.5 / parent: 13329` (existing RandomSpawner uid 12143) | 900613 (prior max 900612) | `SpawnSolreignS07RecordsWing` — F04, F05, F06, F09, F10 |

Both edits are a single appended `- proto: <marker id> / entities: [{uid, Transform}]` block,
commented with the same rationale style the existing Wingmate-beacon rollout uses at the tail of
both files. No other map files touched.

**Coordination check (concurrent lanes):** `feat/wow-wiring` and `feat/mood-rollout`
(`~/AI/solreign-trees/wow-wiring`, `~/AI/solreign-trees/mood-rollout`) were diffed against
`master` before placement — both touch only sprite/texture assets and their own receipts, zero
overlap with prototypes, locale, or the two map files touched here. `feat/mapseed-fixes` did not
exist yet at the time of this work (per the brief, "may spin up") — check its diff against these
two map files before merge; if it touches the same coordinate ranges, defer to this receipt as
the tie-breaker (my edits are additive, single-entity, at the very end of each file, so a
rebase/merge conflict here should be a trivial keep-both).

### Verification

- **YAML linter:** `dotnet run --project Content.YAMLLinter -c Release` → **"No errors found in
  42901 ms."**
- **GameMapsLoadableTest** (confirms both touched maps still load after the marker additions):
  `dotnet test Content.IntegrationTests -c Release --filter "FullyQualifiedName~GameMapsLoadableTest"`
  → **Passed! Failed: 0, Passed: 246, Skipped: 0**, 2m01s.
- **Full `_Solreign` integration suite** (includes `SolreignPrototypeIdIntegrityTest`,
  `ProvidenceVoiceSystemIntegrationTest`, `StationDirectiveIntegrationTest`, etc. — validates the
  new prototypes are well-formed/instantiable and every new locale key resolves):
  `dotnet test Content.IntegrationTests -c Release --filter "FullyQualifiedName~_Solreign"` →
  **Passed! Failed: 0, Passed: 189, Skipped: 1**, 5m01s (the 1 skip is a pre-existing,
  unrelated `PersistentBlockSurvivesRoundRestartAndPreventsOffer` skip).
- **Full `_Solreign` unit suite:** `dotnet test Content.Tests -c Release --filter
  "FullyQualifiedName~Solreign"` → **Passed! Failed: 0, Passed: 1387, Skipped: 2** (the 2 skips
  are pre-existing, unrelated Windows-only ACL tests), 4s.
- **Every fragment's text proofread against canon** for: directives-are-round-robin (F07 states
  it outright, matches queued post 16's correction), Handbook-page-3 tone (deadpan corporate
  voice, consistent register across all 10 — F08 explicitly is NOT a Handbook page), and no
  contradiction of the queued lore-drip facts (see Continuity check above).

### Incidental fixes (unrelated to the arc, needed to unblock verification)

Master's current HEAD (`126e91779e`) does not build clean for `Content.YAMLLinter` /
`Content.IntegrationTests` — two pre-existing, unrelated defects blocked every verification step
above until fixed:

1. `Content.IntegrationTests/Tests/_Solreign/StationDirectiveIntegrationTest.cs:92` — an inline
   string literal passed to `GameTicker.StartGameRule` tripped the `RA0033`
   ("ForbidLiteral") analyzer. Introduced in `4aa7579322` (SR-W-081, unrelated lane). Fixed by
   extracting a named `private const string DirectiveRuleId`, the same workaround
   `SolreignCorporateRuleSystemIntegrationTest` already uses for its own `StartGameRule` call.
2. `Content.IntegrationTests/Tests/_Solreign/SolreignMapHealthTypesTest.cs:18` — a nullable
   reference annotation (`IReadOnlyList<string>?`) used without a `#nullable enable` directive
   (CS8632). Fixed by adding `#nullable enable` at the top of the file.

Both are one-line, zero-behavior-change fixes, committed on this branch since they were required
to get a green build at all. They are fully separable from the lore content — cherry-pick or
drop independently; they touch no _Solreign game logic, only test-file syntax.

I also caught one bug in my own draft during verification: the Oracle petition transcript
(F10) originally opened a line with a literal `[` at column 1, which Fluent's parser
misinterprets as the start of a select-expression variant, breaking locale loading for 3
integration tests. Fixed by rewording to `--- TRANSCRIPT ENDS... ---` (no functional/canon
change, pure syntax fix) — verified by rerunning the full `_Solreign` integration suite clean
afterward.

### Files touched (full list)

- `Resources/Prototypes/_Solreign/Entities/lore_mystery_subject07.yml` (new)
- `Resources/Locale/en-US/_solreign/lore-mystery-subject07.ftl` (new)
- `Resources/Maps/_Solreign/solreign_meridian.yml` (1 entity appended)
- `Resources/Maps/_Solreign/solreign_leviathan.yml` (1 entity appended)
- `Content.IntegrationTests/Tests/_Solreign/StationDirectiveIntegrationTest.cs` (incidental,
  unrelated build fix)
- `Content.IntegrationTests/Tests/_Solreign/SolreignMapHealthTypesTest.cs` (incidental, unrelated
  build fix)
- `docs/receipts/lore-mystery/LORE-MYSTERY-2026-07-16.md` (this file)
