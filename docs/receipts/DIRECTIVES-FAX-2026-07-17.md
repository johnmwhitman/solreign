# DIRECTIVES-FAX receipt — 2026-07-17

**Verdict: GREEN** (build + full unit suite + Solreign integration suite + YAML linter, all clean)

- **Tree:** `/Users/johnwhitman/AI/solreign-trees/directives-fax`, branch `feat/directives-fax`
  (off `origin/master`)
- **Brief:** v14 wave-1 item #1 per council C1 (einstein-001, "ship first", cost S) — the
  DIRECTIVES-FAX builder lane.
- **Clean-room provenance note:** the idea for this feature came from Einstein Engines' public
  description of Station Goals (a round-start printed objective players can complete). No
  Einstein Engines code, repository, or diff was read, cloned, fetched, or consulted at any
  point during this build. Every implementation idiom below (game-rule split, per-round
  `Dictionary`/`HashSet` trackers, write-before-dispatch Season Ledger writes, the milestone
  popup + chat-mirror dual delivery, the CVar-gated "layerable rule" pattern) was copied
  directly from SOLREIGN's own existing, already-shipped features: `StationDirective`, the
  Corporate Ladder (`SolreignCorporateRuleSystem`), Social Firsts
  (`SolreignSocialFirstsSystem`), and Early Death (`SeasonLedgerSystem.EarlyDeath`). The clause
  vocabulary, streak semantics, print-point priority, and all copy are original SOLREIGN design
  work, built fresh from the brief.
- **Run:** 2026-07-17, foreground/blocking, sequential `dotnet` invocations, no merges, no
  pushes.

## What shipped

### 1. Feature flag
`Content.Shared/CCVar/CCVars.SolreignDirectivesFax.cs` — `solreign.directives_fax.enabled`,
**default FALSE** (council precondition: ships dormant). While off, `DirectivesFaxLayerSystem`
never starts the game rule — zero behavior change, verified by an integration test.

### 2. Directive selection (no cross-system read)
`DirectivesFaxRuleSystem.Started` independently **replays**
`StationDirectiveSelection.SelectDirectiveIndex(GameTicker.RoundId,
StationDirectiveCatalog.Directives.Count)` — the exact same pure, deterministic function the
live `StationDirectiveRuleSystem` uses to pick the shift's Corporate Directive — rather than
reading that system's `Access`-restricted component. This was a deliberate design change from
the initial plan (read the live component via a new public getter on
`StationDirectiveRuleSystem`): two independent `RoundStartingEvent` subscribers (the Station
Directive and Directives Fax layer systems) have no guaranteed relative start order in
RobustToolbox, so a live-component read would race. Replaying the pure selection function has
no such race (same round id, same fixed catalog count, same answer every time) and required
**zero changes to the existing, already-shipped StationDirective files**.

### 3. Print-point decision
Investigated whether SOLREIGN places any fax machines. **They do, extensively** — every real
SOLREIGN station map (`solreign_terminus`, `_leviathan`, `_meridian`, `_oasis`, `_perihelion`,
`_verdant`, `_nocturne`) already places multiple department-labeled vanilla `FaxMachineBase`
entities (`FaxMachine.name`: "Bridge", "HoP's Office", "Cargo", "Security", etc.) — no new fax
machine prototype or map edit was needed. `DirectivesFaxRuleSystem.Print.cs`
(`FindFaxPrintTarget`) resolves the print target with a priority chain, cheapest-to-most-
defensive:
1. A fax machine named **"Bridge"** (present on `terminus`/`leviathan`/`meridian`/`oasis`) —
   PROVIDENCE issuing an HR directive reads most naturally as arriving at the command deck,
   matching the Corporate Directive's own chain-of-command HR framing.
2. A fax machine whose name **contains "HoP"** (present on every map that lacks a dedicated
   Bridge fax — `perihelion`/`verdant` have "HoP Office" but no "Bridge") — still command/HR
   tier, same framing.
3. **Any** fax machine belonging to the station at all (deterministic: first one the entity
   query finds) — covers the small `nocturne` map, which has only one fax ("Reach") and neither
   a Bridge nor HoP fax. Guarantees a fax prints on every current SOLREIGN map.
4. **Defensive-only fallback:** no fax machine resolves at all (a hypothetical future map with
   zero fax coverage, or a bare content-integrity test map) — spawn a loose `Paper` entity at
   the station's own transform origin via `FaxSystem.Receive`'s sibling path
   (`PaperSystem.SetContent` directly) rather than silently doing nothing. Never expected to
   trigger against any real SOLREIGN map; exercised directly by the integration tests (pooled
   test servers run bare maps with no fax machines).

The fax is printed via `FaxSystem.Receive(faxUid, printout)` — simulating an incoming fax
*from* PROVIDENCE, which is both the cleanest existing entry point (no `FaxFileMessage`/actor
session required, unlike `PrintFile`) and thematically apt (the directive arrives from Head
Office, not typed locally).

### 4. Clause vocabulary (closed, machine-checkable, pop-1 completable)
`DirectivesFaxClauseCatalog.cs` / `DirectivesFaxClauseEvaluation.cs` — exactly three clause
kinds, cheap to evaluate (a handful of already-tracked counters read once at shift end — **no
per-tick scan**):
- **`ZeroCasualties`** — no crew death recorded this shift (`MobStateChangedEvent` → `Dead`,
  resolved to account via mind, deduped in a `HashSet`).
- **`CargoRevenueMin`** — the station's Cargo account balance rose by at least N credits this
  shift (`SharedCargoSystem.GetBalanceFromAccount`, round-start baseline snapshotted in
  `Started`, delta read at round end).
- **`SupplyOrdersMin`** — at least N supply orders were placed this shift
  (`StationCargoOrderDatabaseComponent.NumOrdersCreated` delta). Named honestly as "placed," not
  "fulfilled" — investigation found no free "orders fulfilled" counter in this fork's Cargo
  system (only orders-created is tracked as a running total); adding a true fulfillment counter
  would have required touching vanilla `CargoSystem.Orders.cs`, which the additive-only rail
  disfavors for a cost-S item. This is recorded as a design compromise, not an oversight.

All three are genuinely solo-completable (the aliveness doctrine): a lone pop-1 player can
avoid dying, and can crew Cargo alone to place orders and sell product. Each Station Directive
maps to a fixed 1-3 clause set chosen for flavor coherence (e.g. "Safety Inspection" →
zero-casualties; "Quarterly Audit" → revenue + orders, two clauses). A round is compliant only
if **every** clause is met (AND, not majority — tested explicitly).

### 5. Persistent compliance streaks (Season Ledger)
New `directives_fax_streak` table (`user_id` PK, `current_streak`, `best_streak`,
`last_round_id`, `updated_utc`) — career-scoped, same as `first_death`/`social_firsts`/`mark`
(a season bump never resets a streak). Semantics, designed per the brief's explicit correction
away from a naive "present at shift end" streak toward the presence-aware version:
- **Present ≥ 5 minutes this shift AND clauses met** → streak + 1.
- **Present ≥ 5 minutes this shift AND clauses unmet** → streak resets to 0.
- **NOT present ≥ 5 minutes (absent, or left early)** → the account's ledger row is never
  touched at all — `DirectivesFaxRuleSystem.SnapshotPresentEligibleAccounts` simply omits it
  from the round-end update loop. Absence never resets a streak. Presence is tracked via
  `IPlayerManager.PlayerStatusChanged` (connect/disconnect timestamps accumulated per account,
  cleared each round) — no new persistent infrastructure needed.
- The write (`SeasonLedgerStore.RecordDirectivesFaxOutcomeAsync`) is idempotent per (account,
  round id) — a retried write for an already-recorded round returns the existing streak
  unchanged rather than double-incrementing.
- **Write-before-dispatch:** the ledger write is `await`ed and its returned streak count is what
  gates any milestone notification — never the reverse.

PROVIDENCE acknowledges milestones (3 / 5 / 10 / 25 / 50 consecutive compliant shifts) via
`SolreignAwardPopup.ShowMilestone` (no fabricated "+N" — same honesty law as Social Firsts) with
escalating deadpan copy, plus a screenshot-surviving private chat mirror (the Social Firsts
dual-delivery idiom, since a round-end popup can easily be missed).

### 6. Round-end summary line
`DirectivesFaxRuleSystem.AppendRoundEndText` (the same `RoundEndTextAppendEvent` hook
`SolreignCorporateRuleSystem` uses) appends a "PROVIDENCE DIRECTIVE COMPLIANCE REPORT" block:
header, one MET/UNMET line for the directive overall, and one MET/UNMET line per clause.

## Rails compliance
- **Ships FALSE:** `solreign.directives_fax.enabled` default false; verified by
  `Disabled_ByDefault_NeverStartsTheRule`.
- **Additive-only:** zero vanilla files edited. Two existing `_Solreign` files extended
  (`SeasonLedgerStore.cs` +schema/doc-comment battery only — new `CREATE TABLE IF NOT EXISTS`,
  no existing table touched; `CCVars.Solreign*` untouched, new CVar in its own file per the
  D0 collision-control convention). Zero changes to `StationDirectiveRuleSystem`/`Catalog`/
  `Selection` (see item 2 above).
- **PG-13 house voice:** `directives_fax.ftl`, corporate-sinister-but-warm, closed vocabulary
  (only a code-owned directive display name, a code-owned clause description, and small integer
  thresholds/streak counts are ever interpolated — never player/daemon text).
- **Write-before-dispatch:** enforced for every ledger write (see item 5).
- **Zero daemon dependency:** everything reads the Season Ledger SQLite file + round-local ECS
  state.
- **Cheap clause evaluation:** every clause reads a value already being tracked incrementally
  (a `HashSet`/`Dictionary` count, or a component field) — evaluated exactly once, at shift end
  (`AppendRoundEndText`) or on-demand; no per-tick scan anywhere in this feature.
- **House laws:** `SeasonLedgerDbPath.Resolve` used transitively (no new DB file — the streak
  table lives in the same Season Ledger SQLite file, inheriting the existing per-pooled-instance
  temp-DB test seam in `TestPair.ServerOptions` automatically); `QueueDel`→`WaitRunTicks(1)` not
  needed (no spawned test entities require that cleanup in these tests — the board/rig entities
  follow `ContractClaimFlowIntegrationTest`'s "Dirty=true, never returned to the pool" idiom
  instead, the established precedent for tests that start persistent game-rule entities).

## Files

**New:**
- `Content.Shared/CCVar/CCVars.SolreignDirectivesFax.cs`
- `Content.Server/_Solreign/DirectivesFax/DirectivesFaxClauseCatalog.cs`
- `Content.Server/_Solreign/DirectivesFax/DirectivesFaxClauseEvaluation.cs`
- `Content.Server/_Solreign/DirectivesFax/DirectivesFaxStreakMilestones.cs`
- `Content.Server/_Solreign/DirectivesFax/DirectivesFaxRuleSystem.cs`
- `Content.Server/_Solreign/DirectivesFax/DirectivesFaxRuleSystem.Tracking.cs`
- `Content.Server/_Solreign/DirectivesFax/DirectivesFaxRuleSystem.Print.cs`
- `Content.Server/_Solreign/DirectivesFax/DirectivesFaxLayerSystem.cs`
- `Content.Server/_Solreign/DirectivesFax/Components/DirectivesFaxRuleComponent.cs`
- `Content.Server/_Solreign/SeasonLedger/SeasonLedgerStore.DirectivesFax.cs`
- `Content.Server/_Solreign/SeasonLedger/SeasonLedgerSystem.DirectivesFax.cs`
- `Resources/Prototypes/_Solreign/game_rules_directives_fax.yml`
- `Resources/Locale/en-US/_solreign/directives_fax.ftl`
- `Content.Tests/_Solreign/DirectivesFaxClauseCatalogTests.cs`
- `Content.Tests/_Solreign/DirectivesFaxClauseEvaluationTests.cs`
- `Content.Tests/_Solreign/DirectivesFaxStreakStoreTests.cs`
- `Content.IntegrationTests/Tests/_Solreign/DirectivesFaxSystemIntegrationTest.cs`
- This receipt.

**Edited (additive only):**
- `Content.Server/_Solreign/SeasonLedger/SeasonLedgerStore.cs` — new `directives_fax_streak`
  `CREATE TABLE IF NOT EXISTS` block + class-doc schema-battery comment update. No existing
  table, column, or method touched.

## Verification (Release, this tree, 2026-07-17)

```
dotnet build SpaceStation14.slnx -c Release
```
**Clean — 50 projects, 0 errors** (warnings are the pre-existing NuGet advisory/prune +
obsolete-API noise). One analyzer round-trip during development: RA0051 (`[Dependency]` fields
must not be `readonly`) on the two new system files, fixed by dropping the modifier — matching
the fork's existing house style.

```
dotnet test Content.Tests -c Release --no-build
```
**Passed! Failed: 0, Passed: 2491, Skipped: 3, Total: 2494** (baseline ~2455/3skip + this
lane's new unit tests ✓; skips are the known Windows-only ACL pair + TestAlertManager). One
gate round-trip during development: `SolreignOrphanReachabilityTest` correctly flagged the new
`SolreignDirectivesFax` game-rule prototype as YAML-unreachable (it is started only from C#, by
design) — declared in `Resources/_Solreign/orphan_allowlist.yml` with the same reasoning as the
existing `SolreignStationDirective` entry.

```
dotnet test Content.IntegrationTests -c Release --no-build --filter "FullyQualifiedName~Solreign"
```
**Passed! Failed: 0, Passed: 341, Skipped: 1, Total: 342, Duration: 6 m 41 s** (baseline 338/1
+ 3 new DirectivesFax tests ✓; the skip is the known
`PersistentBlockSurvivesRoundRestartAndPreventsOffer`). Two earlier attempts on this machine
were poisoned by the pool's global "Tests are taking too long" watchdog while THREE other
worktree lanes ran their own integration suites concurrently (all post-watchdog failures were
`Pool manager has not been initialized` — 168/168, zero genuine failures; the DirectivesFax
tests passed in every attempt). The clean run above was taken after machine load subsided —
infrastructure artifact, recorded for honesty.

```
dotnet run --project Content.YAMLLinter -c Release --no-build
```
**No errors found** (validates the new `game_rules_directives_fax.yml` prototype).

## Skipped / deliberate non-goals
- **True "orders fulfilled" clause** — ships as "orders placed" instead; see item 4.
- **Bridge/HoP print-target priority — no dedicated live-map integration test.** Pooled
  integration test servers run bare maps with no fax machines placed, so the print-point
  priority chain is exercised end-to-end only via its documented defensive fallback (loose
  Paper spawn) in the integration suite; the Bridge/HoP/any-fax priority ordering itself is a
  short, direct filter (`FindFaxPrintTarget`) verified by code review against every real
  SOLREIGN map's actual fax layout (table in item 3). Flagged as a nice-to-have follow-up, not
  gold-plated into this cost-S item.
- No merges, no pushes — branch left for review.
