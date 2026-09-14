# Codex GAME species behavioral viability refresh — 2026-07-27

## Status

**EXACT-HEAD FOCUSED PASS / COMPLETE SOLREIGN BAND PASS / REVIEW ONLY**

- Branch: `codex/game-species-viability-refresh-20260727`
- Worktree: `/Users/johnwhitman/AI/solreign-trees/codex-game-species-viability-refresh-20260727`
- Base: canonical GAME `master` `a4fbb1758644d5827a4d92348c0767e94481ff62`
- Test implementation commit: `ee1e47d4ea4355ba91af157a20357a1b01d65ccf`
- Evidence/handoff commit: this document's commit
- No push, merge, package, deploy, restart, publication, activation, launcher/hub
  change, credential use, or live/player-data access occurred.

## What changed

`SolreignPlayableSpeciesBehavioralViabilityTest` closes the behavioral half of
`SR-BUG-004` without changing production code. The reviewed test-only candidate
was cherry-picked onto the current clean GAME master rather than using its old
feature branch as a base:

1. dynamically enumerates every loaded `SpeciesPrototype` with `RoundStart=true`
   instead of maintaining a stale species allowlist;
2. instantiates every real mob prototype concurrently on its own isolated copy
   of the established sealed `3by3-20oxy-80nit` test room, preventing one
   species from consuming another species' calibration atmosphere;
3. runs 900 ticks (30 simulated seconds) through the real atmosphere, lung,
   respirator, damage, and mob-state systems;
4. requires every selectable species to remain alive and accumulate zero
   `Asphyxiation`;
5. runs the same observation helper against a real human after evacuating every
   tile in the room, proving the actual respiration path produces detectable
   asphyxiation.

The invariant is deliberately breathing-specific. Vox legitimately reacts to
oxygen in the mixed-gas room, so a zero-total-damage assertion would reject
valid current content. The existing static organ gate remains the structural
companion; this fixture proves runtime breathing behavior.

## Current-base verification

Only one heavy `dotnet` command ran at a time. A failed sandbox test-host launch
was reproduced exactly once outside the sandbox; see the environment note below.

Focused fixture, including the real-vacuum fault injection:

```text
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj \
  -c DebugOpt -m:1 -nodeReuse:false -p:UseSharedCompilation=false \
  --filter FullyQualifiedName~SolreignPlayableSpeciesBehavioralViabilityTest \
  -- NUnit.ConsoleOut=0
```

- **2 passed / 0 failed / 0 skipped**
- **15 seconds**
- The command also compiled the current DebugOpt integration-test composition
  successfully before the test host ran.

Complete canonical SOLREIGN integration band, from the same built tree:

```text
SOLREIGN_INTEGRATION_WATCHDOG_MINUTES=30 \
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj \
  -c DebugOpt --no-build --no-restore -m:1 -nodeReuse:false \
  -p:UseSharedCompilation=false \
  --filter FullyQualifiedName~_Solreign -- NUnit.ConsoleOut=0
```

- **449 passed / 0 failed / 4 skipped / 453 total**
- **15 minutes 9 seconds**
- Exit code **0**

This retires the prior branch's harness-timeout HOLD for this exact composition.
It does not prove the full unit suite, Release packaging, prototype/map/orphan
validation, source-stamped artifact identity, launcher join, hub compatibility,
or production readiness.

## Environment incident and recovery

The worktree hook could not clone RobustToolbox because DNS was unavailable.
The exact engine SHA and its five nested submodules were copied from the clean
canonical `rel-build` worktree. No branch content was sourced from that copy,
and no network retry loop was used.

The first focused command then compiled successfully inside the sandbox but the
test host could not bind localhost and aborted with `SocketException (13):
Permission denied`. The exact same command was run once outside the sandbox and
passed 2/2. This is classified as a sandbox transport restriction, not a game
failure. No Crashpad files were inspected or mutated.

## Scope and rollback

Owned paths:

- `Content.IntegrationTests/Tests/_Solreign/SolreignPlayableSpeciesBehavioralViabilityTest.cs`
- this dated handoff

Rollback is deletion of the new test and this handoff. There are no runtime,
prototype, map, CVar, database, website, Director, engine, asset, deploy, backup,
or scheduler changes.

## Integration recommendation

Claude may review and cherry-pick the two commits onto the then-current GAME
master if the diff remains conflict-free. This evidence is local and
review-only; it grants no merge, release, activation, or production authority.
