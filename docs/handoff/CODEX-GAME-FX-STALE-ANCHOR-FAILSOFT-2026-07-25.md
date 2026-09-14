# Codex handoff — GAME FX stale-anchor fail-soft

Date: 2026-07-25

State: **PARKED / HOLD FOR CLAUDE INTEGRATION**

Branch: `codex/solreign-fx-stale-anchor-failsoft-20260725`

Base: `db7c1321fbd0532f0010342a4b69678863b37702`

## What changed

- Added a pure fail-closed policy for classifying one expected lossy FX condition.
- Preserved strict cue rejection and the existing bounded diagnostic aggregator.
- Logged only the exact known-`Broadcast`/locally-missing-anchor bucket at `Info`.
- Kept every unknown, confidential, malformed, ambiguous, and non-anchor failure at `Warning`.
- Added a policy matrix and a connected client/server race fixture.

## Evidence

- `_Solreign.FX` unit namespace: **322/322 passed**.
- Connected stale-anchor fixture: **1/1 passed**.
- Real fixture observed wire delivery, informational expected-loss logging, and no accepted `dust`
  render cue.
- Independent confidentiality/fail-closed review: **PASS, no actionable P0-P3 findings**.
- RobustToolbox remains exactly `960edb32c4dd417496e4667177625d8c3cb14f7e`.
- Synthetic merge preview: clean against local/`origin/master`
  `03588948f194ddabaf1e394b517e59d439e621db`.
- Combined-tree exact band: **48/48 passed**.
- Combined-tree broad SOLREIGN band: **465 passed / 1 failed / 4 skipped / 470 total**, improving
  the historical 437/29/3 result by 28 fewer failures.
- Saltern map load **1/1** and Saltern-dependent AntagGhostRole **28/28**.
- Sole remaining reproducible failure:
  `FirstDeathSceneIntegrationTest.PostRoundDeath_DoesNotClaim_TheSceneSurvivesForARealRound`,
  reproduced **0/1** in isolation during teardown synchronization.

Full receipt:
`docs/receipts/GAME-FX-STALE-ANCHOR-FAILSOFT-2026-07-25.md`.

## Claude route

1. Review the narrow security predicate and independent review result.
2. Preview-integrate this production fix with activation reconciliation commit
   `b7d81bc20b415bf7d91ce494ed8982f74991f005` and Saltern repair commit
   `e22d1b2b41db33e41d2955611e79325abe671905`.
3. Rerun the exact changed-fixture integration band; do not inherit the historical stale
   default-off fixture result as evidence. The detached preview already measured 48/48, but the
   integrator should reproduce it on the actual landed commit.
4. Keep the earlier nested RobustToolbox disconnect queue-clear patch separate.
5. Route the remaining First Death teardown failure to a fresh test-first lifecycle lane; do not
   conflate it with FX or Saltern.

No merge, push, package publication, deploy, restart, activation, launcher, hub, or production
authority is granted.
