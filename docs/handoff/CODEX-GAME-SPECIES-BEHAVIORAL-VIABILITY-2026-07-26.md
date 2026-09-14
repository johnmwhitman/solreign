# Codex GAME species behavioral viability handoff — 2026-07-26

## Status

**LOCAL FOCUSED PASS / BROAD-BAND HOLD / CLAUDE INTEGRATION ONLY**

- Branch: `codex/game-species-behavioral-viability-20260726`
- Worktree: `/Users/johnwhitman/AI/solreign-trees/codex-game-species-behavioral-viability-20260726`
- Base: canonical GAME `master` `c1d4898ee12aba0d4970830ec50fa193152be16c`
- Reviewed implementation commit: `cb2299a2df5f818d2ec09756b5e79eacfba4ac3f`
- Reviewed implementation tree: `508e0673baaef11c649f8bfa6334e0edf073a3d1`
- No push, merge, package, deploy, restart, publication, activation, launcher/hub
  change, credential use, or live/player-data access occurred.

## What changed

`SolreignPlayableSpeciesBehavioralViabilityTest` closes the behavioral half of
`SR-BUG-004` without changing production code:

1. dynamically enumerates every loaded `SpeciesPrototype` with `RoundStart=true`
   (10 on this base);
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

## Test-first and review evidence

- Existing upstream all-species profile baseline: **12 passed / 0 failed**.
- Initial fault control with a healthy body: failed at zero damage as expected.
- A direct `PassiveDamage` negative control was rejected during independent
  review because it bypassed respiration; it is not present in the final diff.
- A 180-tick evacuated-room control also failed at zero because it did not cross
  the respirator missed-cycle threshold.
- Calibrated 900-tick real vacuum control: **1 passed / 0 failed**.
- Final focused fixture: **2 passed / 0 failed / 0 skipped**, 21 seconds.
- Strict `Content.IntegrationTests` Release build with analyzers/warnings policy:
  **0 errors** (1,224 warnings), 2 minutes 5.66 seconds.
- Staged `git diff --check`: clean.
- Staged gitleaks scan: no leaks found across 11.85 KB.
- Independent final rereview: **PASS, no P0-P3 findings**. The initial two P2
  findings and subsequent shared-atmosphere P3 were all closed before commit.

## Broad-band result — do not overclaim

The exact `FullyQualifiedName~Solreign` integration command did **not** complete:

- **284 passed / 220 failed / 1 skipped / 505 total**
- at 20 minutes, `PoolManagerTestEventHandler` shut the pool down;
- failures after shutdown cascaded as `Pool manager has not been initialized`;
- the first concrete failure reported before the cascade was
  `SpawnedCrew_AreClothed_AndNotAsphyxiating("SolreignOasis")` with zero bodies;
- that exact Oasis fixture then passed **1/1** in isolation in 28 seconds.

This makes the broad result watchdog/load/order dependent, not green. The branch
must not be described as full-band qualified. Claude should recompose on the
then-current GAME head and rerun the authoritative band on a quiet host or
otherwise disposition the 20-minute harness limit.

## Scope and rollback

Owned paths:

- `Content.IntegrationTests/Tests/_Solreign/SolreignPlayableSpeciesBehavioralViabilityTest.cs`
- this dated handoff

Rollback is deletion of the new test and this handoff. There are no runtime,
prototype, map, CVar, database, website, Director, engine, or asset changes.

## Sanitized fleet scouting retained for the successor

No private source, paths, internal architecture, credentials, or findings were
sent to external models. Public-domain prompts produced these themes; Codex then
mapped them locally and collision-checked them:

- **Grok 4.5:** strongest fresh GAME candidate is repeatable kart heats with
  authoritative timing and safe new-driver reset. Also useful: opening-message
  sequencing, voluntary pet release, and cross-map Afterlife parity. Do not
  duplicate the parked diagnostics callers, First Shift, contracts, FX, or
  Ledger lanes.
- **MiniMax M3:** strongest OPS corrections are local-only recap generation,
  valid/fail-closed announcement schedule parsing, strict show readiness, and
  atomic schema-checked post-show evidence. Best small additive web candidate:
  a copy-address fallback beside the existing `ss14://` launcher handoff.

These are scouting inputs, not roadmap authority or implementation approval.
