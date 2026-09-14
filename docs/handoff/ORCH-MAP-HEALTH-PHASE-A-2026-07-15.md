# SR-W-012 Phase A — Map Health Scorecard — 2026-07-15

Date: 2026-07-15

Repository: GAME (worktree `orch-map-health-scorecard`)

Branch: `feat/orch-map-health-scorecard`, based on GAME `master` @
`6ab6e949ed` (the `feat/codex-game-population-map-pool` / SR-W-011 merge)

Spec: `docs/research/SR-W-012-MAP-HEALTH-GAP-AUDIT-2026-07-15.md` ("Smallest honest implementation
slice" + "Phase A hard checks" + "Test and performance gates"). Phase B (traversal/pathfinding gate)
is explicitly out of scope for this branch — not started, not stubbed.

Authority: local commits only; no push; no merge. Claude remains the only merge operator per repo
convention.

## Spec conformance table

| Audit requirement | Status | Where |
| --- | --- | --- |
| Shared test-only `SolreignMapTestCatalog`, no second seven-map list | Done | `Content.IntegrationTests/Tests/_Solreign/SolreignMapTestCatalog.cs`; `SolreignMapPoolIntegrationTest.cs` refactored to consume it (`PoolId`/`ExpectedMapIds` now alias the catalog) |
| Cross-checked against indexed `SolreignMapPool` prototype | Done | Unchanged `SolreignMapPoolIntegrationTest.ProductionPoolHasSevenUniqueResolvableMaps` still asserts `pool.Maps` against the catalog-derived list |
| One `MapHealthResultV1` JSON line per map via NUnit output, no file/runtime I/O | Done | `SolreignMapHealthTypes.cs` (record + JSON DTO + `MapHealthResultV1Jsonl.ToLine`), emitted via `TestContext.Out.WriteLine` in `SolreignMapHealthScorecardIntegrationTest.cs` (same idiom as `SolreignPerfBaselineTest.Baseline`) |
| Schema version, map id, min/max population | Done | `MapHealthResultV1` fields; `MaxPlayers` uses `-1` sentinel for the declared-unbounded case (`SolreignTerminus`, `maxPlayers` unset in its prototype → `uint.MaxValue`) |
| Pass/fail/not_evaluated per dimension | Done | `MapHealthStatus` enum; JSON renders `"pass"`/`"fail"`/`"not_evaluated"` |
| Bounded aggregate counts | Done | Station grids, configured jobs, missing job spawns, latejoin spawns, APCs, overloaded APCs, evaluated/unsafe spawn tiles |
| Bounded failure-code list, never entity IDs/coordinates/exceptions/player data | Done | `MapHealthFailureCode` closed constant set; no entity/coordinate data ever enters `MapHealthResultV1` |
| One map is one NUnit case | Done | `TestCaseSource(typeof(SolreignMapTestCatalog), nameof(SolreignMapTestCatalog.MapIds))` on `ProductionMapPassesPhaseAHardChecks(string mapProtoId)` |
| `Assert.Multiple` after result construction | Done | One `Assert.Multiple` block asserting every evaluated dimension |
| Check 1: pool member resolves to one `GameMapPrototype`, station-owned grid | Done | `protoMan.TryIndex` + `StationMemberComponent`/`StationDataComponent.Grids` resolution (same "largest station-member grid" logic as `PostMapInitTest.GameMapsLoadableTest`) |
| Check 2: latejoin/container spawn on a station grid | Done | `CountLatejoinSpawns`, scoped to `StationDataComponent.Grids` (not all grids on the map) |
| Check 3: every configured job has a spawn on a loaded station-member grid, never global query | Done | `CountMissingJobSpawns`, scoped to `stationGrids`; closes the audit's stated gap in the generic test's unscoped `entManager.EntityQuery<SpawnPointComponent>()` |
| Check 4: `StationEmergencyShuttleComponent` present; shuttle loads; `TryFTLDock` succeeds | Done | Absence is now a hard `Fail` (`no_emergency_shuttle_component`) instead of the generic test's silent skip — closes the audit's stated gap |
| Check 5: APC `MaxLoad >= CurrentSupply` after 2s settle | Done | `EvaluateApcLoad`, same comparison and same 2s window as `StationPowerTests.TestApcLoad`, scoped to `stationGrids` |
| Check 6: non-space tile + `IsTileMixtureProbablySafe` + oxygen partial-pressure threshold | Done | `EvaluateSpawnAtmosphere`; oxygen check is `MinimumSafeOxygenPartialPressureKpa = 16f`, a test-local constant (16 kPa hypoxia-onset reference point; `IsMixtureProbablySafe` deliberately does not check composition — see its own doc comment) |
| Stored-power runway stays `not_evaluated`, existing `[Explicit]` test not extracted | Done | `MapHealthResultV1.StoredPowerRunway` is hardcoded `NotEvaluated`; `StationPowerTests.TestStationStartingPowerWindow` untouched |
| Injected-failure fixture (unsafe/oxygen-poor spawn + overloaded APC), fixture-only, never in rotation pool | Done | `InjectedFailureFixtureProvesRedDetection`, built on `PoolManager.TestMap` ("Empty" — already excluded from `GameMapsLoadableTest`'s own case list, not a Solreign pool member) |
| Load each Solreign map no more than once per scorecard run | Done | One `ticker.LoadGameMap` call per test case, map deleted at the end of that same case |
| Baseline duration/memory of generic suites captured before adding the fixture | Done | See "Baseline numbers" below |
| No production map YAML edits | Done | `git diff --name-only` touches only 4 files, all under `Content.IntegrationTests/Tests/_Solreign/`; zero files under `Resources/Maps` or `Resources/Prototypes` |
| Additive only, no game-system behavior change, no CVar value change | Done | All new/changed code is test-only; the only non-test-catalog file touched is the SR-W-011 test itself, refactored (not behavior-changed) to remove list duplication |

## Baseline numbers (captured BEFORE the fixture existed, per the audit's gate)

Captured by stashing all SR-W-012 work, running against the clean SR-W-011 tree, then restoring the
stash. This avoided a first, contaminated attempt where the build raced against an in-flight edit
(see "Judgment calls" below).

```
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj \
  --filter "FullyQualifiedName~PostMapInitTest.GameMapsLoadableTest|FullyQualifiedName~StationPowerTests" \
  -m:1 -nodeReuse:false -p:UseSharedCompilation=false
```

- Result: `Passed: 38, Failed: 0, Skipped: 0, Total: 38` — NUnit-reported duration **1 m 47 s**.
- Wall clock (`/usr/bin/time -l`): **114.15 s real**, 12.10 s user, 2.15 s sys.
- Peak memory: **772,734,976 bytes (~737 MiB)** maximum resident set size.

Post-change regression check, same filter, same command, run after all SR-W-012 Phase A code landed:

- Result: `Passed: 38, Failed: 0, Skipped: 0, Total: 38` — NUnit-reported duration **1 m 41 s**.
- No behavioral or timing regression; the generic suites are untouched by this branch.

## Verification gates run (tails)

### 1. Focused new fixture

```
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj \
  --filter "FullyQualifiedName~SolreignMapHealthScorecardIntegrationTest" \
  -m:1 -nodeReuse:false -p:UseSharedCompilation=false
```

`Failed: 2, Passed: 6, Skipped: 0, Total: 8` — Duration **41 s**.

The 2 failures are `SolreignOasis` and `SolreignTerminus`, both on the `SpawnAtmosphere` dimension —
**real findings**, not test bugs. See "Key finding" below. The other 6 cases (5 clean production maps
+ the injected-failure fixture) pass.

### 2. All-Solreign integration

```
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj \
  --filter "FullyQualifiedName~Content.IntegrationTests.Tests._Solreign" \
  -m:1 -nodeReuse:false -p:UseSharedCompilation=false
```

`Failed: 2, Passed: 138, Skipped: 1, Total: 141` — Duration **2 m 28 s**.

The 1 skip is the known pre-existing `PersistentBlockSurvivesRoundRestartAndPreventsOffer`. The 2
failures are the same `SolreignOasis`/`SolreignTerminus` findings above — no other test regressed.
(Prior SR-W-011 handoff recorded 132 passed / 1 skipped / 133 total on the pre-Phase-A tree; 133 + 8
new cases = 141, and only the 2 genuine new findings are red.)

### 3. Existing generic map-load/power suites (regression check)

See "Baseline numbers" above — `38/38` passed both before and after, no timing regression.

### 4. Unit battery

```
dotnet test Content.Tests/Content.Tests.csproj \
  --filter "FullyQualifiedName~Content.Tests._Solreign" \
  -m:1 -nodeReuse:false -p:UseSharedCompilation=false
```

`Failed: 0, Passed: 1147, Skipped: 2, Total: 1149` — Duration **4 s**. The 2 skips are the existing
Windows-only Ledger staging ACL runtime proofs. This branch adds no new Content.Tests coverage (see
"Judgment calls").

### 5. Release server build

```
dotnet build Content.Server/Content.Server.csproj --configuration Release --no-incremental \
  --verbosity minimal -m:1 -nodeReuse:false -p:UseSharedCompilation=false
```

`Build succeeded. 0 Error(s), 840 Warning(s)`. This branch touches zero files under `Content.Server`;
the 3-warning drift from the SR-W-011 handoff's recorded 837 is pre-existing repo evolution between
commits, not something this branch introduced.

### 6. Diff/no-deletion checks

`git diff --name-only` from `6ab6e949ed`: exactly 4 files, all under
`Content.IntegrationTests/Tests/_Solreign/`. No deletions, no files under `Resources/Maps`,
`Resources/Prototypes`, or any production path.

## Key finding: two real Phase A RED results (not test bugs)

> **PARTIALLY SUPERSEDED by the "Adversarial review round" section below.** The Oasis finding was
> confirmed a genuine map defect and has now been FIXED on this branch (sanctioned one-line map
> edit). The Terminus finding was re-ruled: the vacuum is real, but a borg has no atmospheric needs,
> so flagging its spawn was an over-strict check, not a map defect — the check now exempts fixed
> non-atmospheric job entities and Terminus is GREEN. Both scorecard rows are now all-pass.

This is the headline result of standing this scorecard up: it immediately found two previously
invisible player-safety gaps in production maps, exactly the class of defect the audit predicted
"no map-level test checks job/latejoin spawn tiles after initialization" would be hiding.

**`SolreignOasis` — `SpawnPointScientist` at grid tile `(-15, -27)`.** `AtmosphereSystem.
GetTileMixture` returns a real "not yet processed" result for this exact tile — indistinguishable
from no mixture — even after 10 s of total settle (2 s APC + 8 s additional, tested during
investigation). All four orthogonal neighbor tiles are fully healthy and pressurized (~98 kPa,
~100 mol, `IsTileMixtureProbablySafe` = true), confirming this is not a settle-window artifact but a
tile-specific condition isolated to the exact spot the Scientist job spawn marker sits on.

**`SolreignTerminus` — `SpawnPointBorg` at grid tile `(13, 62)`.** This one is unambiguous: the tile
and *all four* of its orthogonal neighbors read `pressure=0, totalMoles=0` — `GasMixture.SpaceGas`,
real hard vacuum, not a registration gap. A Borg spawning at round start here has no atmosphere at
all.

Both were confirmed genuine (not artifacts of this test's own timing/methodology) by: (a) extending
the settle window from 2 s to 10 s with no change in outcome, and (b) probing all four orthogonal
neighbor tiles, which for Oasis are fine and for Terminus are equally vacant. Per the hard
constraint ("Do NOT edit any of the seven production map YAML files — expected production paths:
none"), this branch does not attempt to fix either map. Both `ProductionMapPassesPhaseAHardChecks`
cases correctly report `Fail`/`unsafe_spawn_tile_atmosphere` — this is the scorecard working as
designed, not a defect in the test. **Recommend a follow-up mapping ticket for both spawn points**
before treating SR-W-012 as `active`/`proven` per the audit's own advancement gate.

## Judgment calls

1. **Baseline capture required a re-run.** The first baseline attempt raced a build against my own
   in-flight edits (I had already started writing the new files while the baseline `dotnet test`
   was mid-build), producing a meaningless contaminated number (a failed compile, `4.3 GB` peak RSS
   that reflects a failed-build MSBuild session, not test execution). I stashed all new/modified work
   (`git stash -u`), re-ran the baseline against the clean SR-W-011 tree, then restored the stash.
   The numbers above are from that clean run.

2. **`AtmosphereAdditionalSettleSeconds` (3 s) beyond the shared 2 s APC window.** The audit says
   check 6 should read "after the same fixed settle window" as check 5. I kept the APC check at
   exactly 2 s (unchanged, matching `StationPowerTests.TestApcLoad` precisely) but added 3 s more
   before the atmosphere read, because grid tile atmosphere is populated through a per-tick-budgeted
   invalidation queue (`atmos.max_process_time`), not synchronously at map load — a large map's
   initial invalidation batch can plausibly still be draining at the 2 s mark. I verified (see "Key
   finding") that this additional buffer does **not** mask either real finding — both were confirmed
   real even at 10 s total settle — so the extra 3 s is cheap insurance against a settle-window false
   positive on some future/larger map, not a workaround for the two current findings.

3. **No new `Content.Tests` unit coverage added.** The audit's "Expected owned paths" explicitly says
   "test-local result/validator types" — I read this as meaning the `MapHealthResultV1` types belong
   in the test project, not in `Content.Server` production code (unlike the `ReportEntry`/`ReportJsonl`
   split I otherwise mirrored the idiom of). Since `Content.Tests` doesn't reference
   `Content.IntegrationTests`, adding a pure JSON-shape unit test for `MapHealthResultV1Jsonl` would
   have required either a cross-project type share or a duplicated type — I judged this added
   complexity wasn't worth it for Phase A and left `Content.Tests._Solreign` untouched (still green,
   see gate 4).

4. **Fixture reuses `PoolManager.TestMap` ("Empty") instead of authoring a new test map YAML.** The
   audit says "inject one fixture-only unsafe/oxygen-poor spawn and one overloaded APC in a test map."
   Rather than hand-author a new grid YAML (risky to get right blind, and unnecessary asset-license
   surface), I reused "Empty" — already excluded from `GameMapsLoadableTest`'s own case list
   specifically because it's a minimal test-only map, and already has exactly one job/latejoin spawn
   point to corrupt. This keeps the fixture fully test-local with zero new map files.

5. **Injected-failure values are written directly onto real engine components** (`ApcComponent.
   MaxLoad`, `PowerNetworkBatteryComponent.CurrentSupply`, and the spawn tile's real `GasMixture` via
   `AtmosphereSystem.GetTileMixture(...).Clear()`/`AdjustMoles`), evaluated by the exact same
   read-only detector functions the production-map check uses (`EvaluateApcLoad`/
   `EvaluateSpawnAtmosphere`) — not a separate mocked code path. Injection happens in the same
   `WaitPost` block as evaluation with zero ticks in between, so nothing can "correct" the injected
   state before it's read.

6. **Everything in check 5/check 6 runs inside `server.WaitPost`, not directly on the test thread
   after `RunSeconds`.** `StationPowerTests.TestApcLoad` reads APC state directly on the calling
   thread after `RunSeconds` with no wrapper, and that pattern is safe for plain
   `EntityQueryEnumerator` reads. During the atmosphere-null investigation I moved all of check 5 and
   6 into an explicit `WaitPost` for extra safety once `AtmosphereSystem` method calls were involved
   (not just raw component queries); this made no difference to the outcome (confirming it was never
   a thread-safety issue) but is the more conservative, clearly-correct pattern going forward.

## Files touched

- `Content.IntegrationTests/Tests/_Solreign/SolreignMapTestCatalog.cs` (new) — shared seven-map ID
  catalog.
- `Content.IntegrationTests/Tests/_Solreign/SolreignMapHealthTypes.cs` (new) — `MapHealthStatus`,
  `MapHealthResultV1`, `MapHealthFailureCode`, JSON DTO + serializer.
- `Content.IntegrationTests/Tests/_Solreign/SolreignMapHealthScorecardIntegrationTest.cs` (new) — the
  Phase A fixture: `ProductionMapPassesPhaseAHardChecks` (7 cases via the catalog),
  `InjectedFailureFixtureProvesRedDetection` (vacuum + overloaded APC), and
  `InjectedOxygenOnlyFailureProvesOxygenBranch` (added in the review round).
- `Content.IntegrationTests/Tests/_Solreign/SolreignMapPoolIntegrationTest.cs` (modified) — `PoolId`/
  `ExpectedMapIds` now alias `SolreignMapTestCatalog`; no behavior change, no new/removed test cases.
- `Resources/Maps/_Solreign/solreign_oasis.yml` (modified, review round, SANCTIONED) — one-line
  spawn-point move; see "Sanctioned production map fix" below.

## Ownership and rollout

- Expected production paths (revised after review): the ONLY production file touched is the
  sanctioned one-line Oasis spawn-point move (see review round below). Zero game-system code changed.
- `research` → `active` only after SR-W-011 disposition and ownership revalidation (already true on
  this branch's base). Both first-run findings are now resolved (Oasis fixed, Terminus re-ruled) —
  all seven scorecard rows are green.
- Phase B (traversal/pathfinding gate) remains untouched — not started, not stubbed, per instruction.

## Adversarial review round (2026-07-15) — cdx: 0 P0, 4 P1, 7 P2, all applied

An adversarial review (cdx) blocked merge until 4 P1 and 7 P2 findings were fixed. All eleven were
applied on this branch, plus one newly SANCTIONED production map fix. Where this section conflicts
with earlier sections, this section wins.

### P1 fixes

1. **RED-fixture spawn selection was pool-unsafe.** The fixture picked the first global
   `SpawnPointComponent` after loading `PoolManager.TestMap`; a dirty pool containing another
   "Empty" instance could supply a spawn outside `stationGrids`, silently absorbing both injections.
   Selection is now filtered to the loaded map's own grids. Fixing this exposed a second latent hole:
   the "Empty" grid ships with **no `GridAtmosphereComponent`**, so `GetTileMixture` fell back to a
   fresh *immutable* `GasMixture.SpaceGas` per call and the gas injections had been silent no-ops all
   along (the vacuum RED case had been passing vacuously — the tile was already space-like). The
   fixture loader now ensures `GridAtmosphereComponent` on the fixture grids, and both injections
   assert `mixture.Immutable == false` so a no-op injection can never again masquerade as detector
   proof.
2. **Oxygen branch now has its own RED case.** `InjectedOxygenOnlyFailureProvesOxygenBranch` injects
   a normobaric (~101 kPa via `Atmospherics.MolesCellStandard` of pure nitrogen), safe-temperature,
   oxygen-free mixture that passes `IsTileMixtureProbablySafe` and asserts
   `unsafe_spawn_tile_oxygen` specifically — plus asserts the pressure/temperature code did NOT
   fire. Deleting the oxygen branch now turns a test red. The original vacuum case asserts
   `unsafe_spawn_tile_atmosphere` specifically instead of a three-way OR.
3. **Missing/empty job configuration is a hard FAIL.** A station without `StationJobsComponent` (or
   with an empty `SetupAvailableJobs`) previously left check 3 at `not_evaluated`, which the final
   assertion (then `Is.Not.EqualTo(Fail)`) accepted. It now fails with the bounded code
   `no_job_configuration`, and the final assertion requires `jobSpawns == pass`.
4. **Borg exemption (cdx domain ruling).** Fixed non-atmospheric job entities are exempt from the
   pressure/temperature/oxygen portions of check 6 but KEEP station-grid membership and the
   non-space walkable-tile requirement. Implemented capability-first, not `job == Borg`: the spawn's
   `Job` → `JobPrototype.JobEntity` → `EntityPrototype`, exempt only if the prototype has none of
   `Respirator`, `Barotrauma`, `TemperatureDamage` (a future breathing JobEntity automatically
   re-enters the full check). Latejoin/AnyJob markers (`Job == null`) and profile-based jobs
   (`JobEntity == null`) are never exempt. **Terminus consequence:** the vacuum at `SpawnPointBorg`
   tile `(13, 62)` is real, but a borg (no Respirator/Barotrauma/TemperatureDamage anywhere in its
   chassis chain, verified from `BaseMob` up) takes no harm from it — the earlier RED was an
   over-strict check, not a map defect. Terminus now reports
   `spawnAtmosphere=pass, unsafeSpawnTileCount=0, failureCodes=[]`.

### P2 fixes

5. `MapHealthResultV1Jsonl.ToLine` enforces its own bounds: emitted schema version is the constant
   `1`; map id length-capped at 64; failure codes allowlisted against the closed
   `MapHealthFailureCode.All` set, deduplicated, capped at 16.
6. RED-fixture injection/evaluation blocks moved from `WaitPost` to `WaitAssertion` so in-block
   assertion failures propagate properly.
7. The fixed 3 s atmosphere delay was replaced with a bounded readiness poll: every 0.5 s, up to an
   8 s budget, the test checks whether every station-grid spawn tile's mixture resolves; budget
   expiry still evaluates (and fails) — never a skip.
8. A bounded scorecard row is now ALWAYS emitted before any assertion. Prototype resolution, map
   load (`try/catch` → new bounded code `map_load_failed`), and grid resolution no longer assert
   mid-flight, making `map_prototype_unresolved` reachable and guaranteeing the worst failures still
   produce their row. The final `Assert.Multiple` additionally asserts `failureCodes` is empty.
9. `TryFTLDock` runs LAST (verified: on success it physically re-parents and docks the shuttle onto
   the station map via `FTLDock` → `SetCoordinates`), so APC and atmosphere checks observe pristine
   round-start state; the docked shuttle grid entity is explicitly deleted afterwards (deleting the
   scratch map alone would leave a successfully-docked shuttle behind).
10. Spawn-tile counters now count **distinct (grid, tile) pairs**, not spawn entities — duplicate
    markers on one tile no longer double-count (Terminus: 145 spawn entities → 138 distinct tiles).
    A tile shared by an exempt and a non-exempt spawn keeps the full check (OR-merge).
11. `SolreignMapTestCatalog.MapIds` is now `ImmutableArray<string>`.

Reviewer note honored: the `[EnsureCVar(GridFill, false)]` attributes stay — the no-CVar constraint
meant production/default values; test-scoped overrides are acceptable.

### Sanctioned production map fix (separate commit)

cdx confirmed the Oasis finding as a genuine player-safety map defect and authorized a minimal fix.
`Resources/Maps/_Solreign/solreign_oasis.yml`, entity uid `10522` (`SpawnPointScientist`): moved
from `pos: -14.5,-26.5` (grid tile `(-15,-27)`, atmosphere never resolves) one tile north to
`pos: -14.5,-25.5` (grid tile `(-15,-26)`, measured healthy at ~98.11 kPa during the neighbor-probe
investigation). Exact diff is one line; nothing else in the map file changed. Oasis now reports
`spawnAtmosphere=pass, unsafeSpawnTileCount=0, failureCodes=[]`.

### Post-review gate results (all green)

- Focused fixture: `Passed: 9, Failed: 0, Skipped: 0, Total: 9` — 33 s
  (7 production maps + vacuum RED + oxygen RED). All seven maps emit all-pass rows with empty
  failure codes.
- All-Solreign integration: `Passed: 141, Failed: 0, Skipped: 1, Total: 142` — 2 m 25 s (the one
  skip is the known pre-existing `PersistentBlockSurvivesRoundRestartAndPreventsOffer`).
- Generic map-load/power suites: `Passed: 38, Failed: 0` — 1 m 43 s (this also load-validates the
  edited Oasis map through `GameMapsLoadableTest`).
- Unit battery: `Passed: 1147, Failed: 0, Skipped: 2 (known Windows-only), Total: 1149` — 3 s.
- Release server build: 0 errors (see gate log; warnings are the pre-existing repo debt).

### Evidence rows (previously-red maps, post-fix)

```
{"schemaVersion":1,"mapId":"SolreignOasis","minPlayers":0,"maxPlayers":35,"stationGrid":"pass","latejoinSpawn":"pass","jobSpawns":"pass","emergencyShuttle":"pass","apcLoad":"pass","spawnAtmosphere":"pass","storedPowerRunway":"not_evaluated","stationGridCount":1,"configuredJobCount":34,"missingJobSpawnCount":0,"latejoinSpawnCount":4,"apcCount":41,"overloadedApcCount":0,"evaluatedSpawnTileCount":60,"unsafeSpawnTileCount":0,"failureCodes":[]}
{"schemaVersion":1,"mapId":"SolreignTerminus","minPlayers":70,"maxPlayers":-1,"stationGrid":"pass","latejoinSpawn":"pass","jobSpawns":"pass","emergencyShuttle":"pass","apcLoad":"pass","spawnAtmosphere":"pass","storedPowerRunway":"not_evaluated","stationGridCount":1,"configuredJobCount":38,"missingJobSpawnCount":0,"latejoinSpawnCount":6,"apcCount":48,"overloadedApcCount":0,"evaluatedSpawnTileCount":138,"unsafeSpawnTileCount":0,"failureCodes":[]}
```

## cdx round 2 (2026-07-15) — 4 residuals, all applied (commit `5251bdb635`)

1. **[P1] Readiness poll mutability.** The check-6 poll now requires `mixture is { Immutable: false }`
   — any-non-null accepted the immutable `SpaceGas` sentinel returned while a tile hasn't
   materialized, exiting immediately and risking a false-RED on slow initialization. Exempt spawns
   are skipped by the poll so an immutable space-like borg tile can't burn the budget.
2. **[P2] Serializer bounds regression test.** `SolreignMapHealthTypesTest` (5 pure unit cases, no
   pair): schema forced to 1, 64-char map-id truncation, unknown-code drop, first-occurrence dedupe,
   and the cap invariant (`MaxFailureCodes >= MapHealthFailureCode.All.Count`).
3. **[P2] Targeted dedupe/exemption tests.** `DuplicateSpawnMarkersOnOneTileCountOnce` (3 markers,
   1 tile → evaluated == 1) and `ExemptSpawnSkipsAtmosphereButSharedTileKeepsFullCheck` (exempt borg
   spawn on a mutable vacuum tile → 0 unsafe; non-exempt marker joining the same tile → 1 unsafe with
   `unsafe_spawn_tile_atmosphere`, proving the OR-merge). Gas state forced tick-free alongside the
   evaluations so adjacent-tile equalization can't smear the setup.
4. **[P3] Oasis comment.** The placement comment referencing the scientist spawn's old coordinate now
   reads `-14.5,-25.5`; comment-only.

Round-2 gates: focused (scorecard + types) `16/16` green — 32 s; all-Solreign integration
`Passed: 148, Failed: 0, Skipped: 1 (known)` — 3 m 8 s; unit battery `1147/0/2 known skips` — 3 s;
generic map-load/power `38/38` — 2 m 9 s (load-validates the comment-edited Oasis map); Release
server build 0 errors.
