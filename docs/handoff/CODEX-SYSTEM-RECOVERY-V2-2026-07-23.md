# SOLREIGN diagnostics system recovery v2 — Codex handoff

Date: 2026-07-23

Repository: GAME

Branch: `cdx/system-recovery-v2`

Base: `a061988945` (`origin/master` at final rebase)

Implementation head before this receipt: `ef95060990`

Authority: **review and integration input only. This branch does not authorize merge, push,
deployment, restart, CVar activation, or production action.**

## Outcome

The previously extracted SR-REF-015 power and SR-REF-016 atmosphere diagnostics now receive real
game state instead of remaining inert:

- power samples post-solver `NetworkBatteryPostSync` state from station-owned SMES units and their
  connected power networks;
- atmosphere samples real station tile mixtures during `AtmosDeviceUpdateEvent`;
- both master CVars remain `false` by default and are now `SERVERONLY`;
- immutable map/space gas is treated as readable vacuum state instead of being discarded;
- routine atmosphere metric events are coalesced to one per sensor per five seconds while state
  evaluation and hazard transition events remain immediate;
- empty/no-SMES collection emits no false emergency;
- the most disrupted power network wins the public diagnostic snapshot instead of the network with
  the highest raw draw;
- a depleted backup battery on a generator-supplied grid is a reserve brownout/critical warning,
  not a fabricated blackout/emergency.

No antag rotation code was wired. No vanilla subsystem, prototype, map, network component,
RobustToolbox source, launcher, deployment path, or production configuration was changed.

## Commit sequence

1. `a9b75e6fce` — `Wire Solreign power and atmos diagnostics`
2. `187ec101c4` — `Harden recovered diagnostics sampling`
3. `ef95060990` — `Correct diagnostic reserve severity`
4. the receipt commit containing this file

The first implementation commit was forward-ported from the reviewed diagnostic subset of
`cdx/system-recovery@0de6fd8a0f`; the old branch and the broken autonomous system chain were not
merged. The branch was then rebased onto current `origin/master`, retaining main's dark-by-default
policy and narrowing both master controls to `SERVERONLY`.

## Exact scope

- `Content.Server/_Solreign/Diagnostics/SolreignPowerDiagnosticSystem.cs`
- `Content.Server/_Solreign/Diagnostics/SolreignAtmosDiagnosticSystem.cs`
- `Content.Shared/CCVar/CCVars.SolreignPowerDiagnostics.cs`
- `Content.Shared/CCVar/CCVars.SolreignAtmosDiagnostics.cs`
- `Content.Tests/_Solreign/SolreignPowerDiagnosticTests.cs`
- `Content.Tests/_Solreign/SolreignAtmosDiagnosticTests.cs`
- `Content.Tests/_Solreign/SolreignDiagnosticCVarTests.cs`
- `Content.IntegrationTests/Tests/_Solreign/SolreignDiagnosticSystemsIntegrationTest.cs`
- this handoff receipt

## TDD and review evidence

RED evidence was captured before each implementation stage:

- both real-caller integration tests failed before the event wiring existed;
- the dedicated CVar test failed because both controls were enabled and used `CVar.SERVER`;
- the edge-case regression compile failed because live-sample, immutable-mixture, and sample-gate
  behavior did not exist;
- the depleted-backup cases failed as `Blackout` and `Emergency` before the severity repair.

GREEN evidence on the rebased implementation:

- focused diagnostics unit tests: `39 passed, 0 failed`;
- focused real-caller integration tests: `2 passed, 0 failed`;
- final full unit battery: `2785 passed, 3 skipped, 0 failed`.

The three skips are existing platform-specific tests:

- `TestAlertManager`;
- `WindowsCleanupFailureResidueRetainsRestrictedAcl`;
- `WindowsStageAclAllowsOnlyCurrentServiceIdentity`.

The final full SOLREIGN integration result is recorded in the verification section below.

Three independent read-only reviews covered specification fit, event/performance behavior, and
merge safety. Their findings produced the empty-sample guard, disruption-first network selection,
immutable-space handling, atmosphere metric cadence, immediate-transition test, and corrected
depleted-backup severity. The final merge review found no changed-path collision after rebase.

## Final verification

### Full unit battery

```sh
dotnet test Content.Tests/Content.Tests.csproj --no-restore --no-build \
  -m:1 -nodeReuse:false -p:UseSharedCompilation=false \
  --logger "console;verbosity=minimal"
```

Result: `Passed 2785, Failed 0, Skipped 3, Total 2788`.

### Focused real-caller integration

```sh
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj --no-restore --no-build \
  -m:1 -nodeReuse:false -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~SolreignPowerDiagnosticIntegrationTest|FullyQualifiedName~SolreignAtmosDiagnosticIntegrationTest" \
  --logger "console;verbosity=minimal"
```

Result: `Passed 2, Failed 0, Skipped 0, Total 2`.

### Full SOLREIGN integration battery

```sh
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj --no-restore --no-build \
  --filter "FullyQualifiedName~Solreign" \
  -m:1 -nodeReuse:false -p:UseSharedCompilation=false \
  --logger "console;verbosity=minimal"
```

Result: `Passed 430, Failed 0, Skipped 1, Total 431` in 14 minutes 28 seconds.

The single skip was the existing
`PersistentBlockSurvivesRoundRestartAndPreventsOffer` case.

### Static merge checks

- `git diff --check origin/master...HEAD`: clean before receipt.
- synthetic merge of rebased implementation head against `origin/master`: clean, tree
  `7366b10242cfe6c00a3a563ea470e96b390ff186`.
- RobustToolbox remained pinned to `960edb32c4dd417496e4667177625d8c3cb14f7e`; all nested
  submodules were initialized at their expected revisions.
- no tracked or untracked generated residue was present; ignored build outputs remain local only.

Re-run the synthetic merge and changed-path checks after cherry-picking the receipt because
`origin/master` may move while other SOLREIGN lanes are active.

## Integration and rollout boundary

This work is merge-ready code, not activation evidence. Both diagnostics remain dormant until an
operator intentionally enables their master CVar in a private canary. Suggested canary order:

1. enable one diagnostic at a time on a private test round;
2. validate normal, degraded, absent-sensor/SMES, and recovery states;
3. inspect server timing and event volume for at least one representative map;
4. disable immediately on false severity, event load, or operator-noise concerns;
5. do not advance from canary to proven without live-player and operator evidence.

Rollback before activation is removal of this branch's commits. Rollback after activation is first
setting the relevant master CVar to `false`, then reverting the commits through the normal reviewed
release process if necessary.
