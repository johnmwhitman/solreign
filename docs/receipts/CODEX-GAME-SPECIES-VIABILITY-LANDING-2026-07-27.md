# Codex GAME species viability landing receipt — 2026-07-27

## Verdict

**RECOVERED EXACTLY / FOCUSED PASS / UNITS PASS / BROAD INTEGRATION HOLD**

The two requested test-only commits were recovered onto clean
`origin/master` without their unrelated local-master ancestry. The new
species fixture passed both focused and broad-band execution, but the complete
SOLREIGN integration band finished with one unrelated existing zone-gate test
failure caused by an HTN planner null reference. This branch is therefore
preserved for review and is **not integration-ready evidence**.

No retry was run after the broad-band failure.

## Identity and recovery

- Repository: `/Users/johnwhitman/AI/Kolton-SS14/server`
- Worktree:
  `/Users/johnwhitman/AI/solreign-trees/codex-game-land-species-viability-20260727`
- Branch: `codex/game-land-species-viability-20260727`
- Base: `fa12e6ee0295a42b8efb97ce2633f2816bbb92ef`
- Base was revalidated read-only with
  `git ls-remote origin refs/heads/master` before worktree creation.
- Source test commit: `ee1e47d4ea2c7128fbdce00f061fa1739c18fc83`
- Recovered test commit: `2528c1c3129d3c028fa3eafcac51e17f4b334cbe`
- Source evidence commit: `f7074bccc92bbf1eb008f178b63826635d410f5a`
- Recovered evidence commit: `e103c736efaaa47981cfa440e626795d9a3a1be2`
- Source and recovered stable patch IDs match:
  - test: `af67bedd40034657e8203a0e7f059a8ce4745543`
  - evidence: `3a3bad92951b966f9d04e4b9729f1e1c343c8289`

The base-to-tip payload before this receipt was exactly:

- `Content.IntegrationTests/Tests/_Solreign/SolreignPlayableSpeciesBehavioralViabilityTest.cs`
- `docs/handoff/CODEX-GAME-SPECIES-BEHAVIORAL-VIABILITY-REFRESH-2026-07-27.md`

No production source, prototype, map, asset, CVar, database, or
RobustToolbox content changed. No YAML changed, so no YAML validation was
required by this recovery lane.

The worktree hook could not clone RobustToolbox because sandbox DNS was
unavailable. The exact required engine SHA
`960edb32c4dd417496e4667177625d8c3cb14f7e` and its five exact nested
submodule SHAs were attached as local Git worktrees from the clean
`rel-build` object stores. No network retry loop or engine-content change
occurred.

## Serial verification

Every `dotnet` command ran alone with
`-m:1 -nodeReuse:false -p:UseSharedCompilation=false`. Integration tests ran
outside the sandbox because their localhost harness requires it.

### Focused behavioral fixture

```text
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj \
  -c DebugOpt -m:1 -nodeReuse:false -p:UseSharedCompilation=false \
  --filter FullyQualifiedName~SolreignPlayableSpeciesBehavioralViabilityTest \
  --logger "trx;LogFileName=species-focused.trx" \
  --results-directory /private/tmp/solreign-species-viability-20260727 \
  -- NUnit.ConsoleOut=0
```

- Passed: 2
- Failed: 0
- Skipped: 0
- Duration: 15 seconds
- TRX SHA-256:
  `fc89d214edaf4ce9dce3f7e03f841323fd3e51f0514bdfcb3981fef1b4451efc`

### Full Content.Tests

```text
dotnet test Content.Tests/Content.Tests.csproj \
  -c DebugOpt --no-restore \
  -m:1 -nodeReuse:false -p:UseSharedCompilation=false \
  --logger "trx;LogFileName=content-tests.trx" \
  --results-directory /private/tmp/solreign-species-viability-20260727 \
  -- NUnit.ConsoleOut=0
```

- Passed: 2,992
- Failed: 0
- Skipped: 3 platform-specific tests
- Total: 2,995
- TRX SHA-256:
  `4c0b15c71e560364a83e39afa3db9e1f0836420b1d2fb0d3e3ca5118493a04e5`

### Complete SOLREIGN integration band

```text
SOLREIGN_INTEGRATION_WATCHDOG_MINUTES=30 \
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj \
  -c DebugOpt --no-build --no-restore \
  -m:1 -nodeReuse:false -p:UseSharedCompilation=false \
  --filter FullyQualifiedName~_Solreign \
  --logger "trx;LogFileName=solreign-integration.trx" \
  --results-directory /private/tmp/solreign-species-viability-20260727 \
  -- NUnit.ConsoleOut=0
```

- Passed: 432
- Failed: 1
- Skipped: 4
- Total: 437
- Duration: 17 minutes 32 seconds
- TRX SHA-256:
  `51c1b6eb000ada214d18c8fcccf9d8b1c410ebe5fbe78ee6c36e1a8e2f41fdfb`

Both recovered species tests passed inside this broad band.

The sole failure was:

```text
Content.IntegrationTests.Tests._Solreign.SolreignZoneGateSystemIntegrationTest
  .HellEntry_ThenLadder_ThenReturn_RoundTripsTraveler
```

The test failed during dirty-pair teardown after the server emitted:

```text
System.NullReferenceException
  at Content.Server.NPC.HTN.PrimitiveTasks.Operators.UtilityOperator.Plan(...)
SERVER [FATL] system.htn: Received exception on planning job
```

The similarly named
`SolreignZoneGateIntegrationTest.HellEntry_ThenLadder_ThenReturn_RoundTripsTraveler`
passed in the same run. The recovered species test does not touch the zone-gate
or HTN paths, but no clean-base comparison or retry was performed, so this
receipt does not classify the failure as baseline or flaky.

## Scope and handoff

The branch remains local and unpushed. No merge, deploy, restart, activation,
launcher/hub mutation, credential access, production access, or worktree
cleanup occurred.

Integration must remain on hold until the parent lane decides whether to
reproduce and repair the independent HTN failure on a separate bounded
troubleshooting lane.
