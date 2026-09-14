# Claude handoff — GAME strict integration-build repair

## Parked candidate

- Worktree:
  `/Users/johnwhitman/AI/solreign-trees/codex-game-strict-integration-build-repair-20260726`
- Branch: `codex/game-strict-integration-build-repair-20260726`
- Canonical/tested base:
  `70eb235834d1e43e2e7500e9b8dbbbeb64273df0`
- Implementation commit:
  `79fb230c10e695f97daea7055fdc9b34703135df`
- Implementation tree and same-base preview tree:
  `538fd4fd6b444d651a5394a64146db7c0327f625`
- RobustToolbox:
  `960edb32c4dd417496e4667177625d8c3cb14f7e`
- Receipt:
  `docs/receipts/CODEX-GAME-STRICT-INTEGRATION-BUILD-REPAIR-2026-07-26.md`
- Kill-date: 2026-08-09

## Outcome

This two-file, test-infrastructure-only repair restores an analyzer-enabled
build of the SOLREIGN integration-test project:

- a fail-closed null guard resolves the network-parity `CS8604`;
- a typed Corporate Project prototype ID resolves four `RA0033` diagnostics;
- no analyzer or warning suppression was added; and
- no runtime code, content, YAML, CVar, or production behavior changed.

Measured on the exact candidate:

- strict integration-project build: 149 warnings / **0 errors**;
- focused affected tests: **5 pass / 0 fail / 0 skip**;
- current Shadow viability unit: **1 pass / 0 fail / 0 skip**;
- full units: **2,935 pass / 0 fail / 3 skip**; and
- full SOLREIGN-filtered integration: **487 pass / 0 fail / 4 skip**.

The receipt records the complete 5-error → 4-error → 0-error compiler ladder,
durations, identities, hashes, expected skips, and claim limits.

## Claude landing procedure

1. Revalidate the then-current GAME canonical head.
2. Review and cherry-pick
   `79fb230c10e695f97daea7055fdc9b34703135df`.
3. Review and retain the separate documentation commit immediately following
   the implementation commit on this branch, or copy both dated documents into
   Claude's integration evidence history.
4. If canonical master is no longer
   `70eb235834d1e43e2e7500e9b8dbbbeb64273df0`, treat the recorded preview tree
   as stale and regenerate composition evidence.
5. Build `Content.IntegrationTests` with analyzers and normal warning policy
   enabled.
6. Rerun the focused affected tests, full `Content.Tests`, and the complete
   `FullyQualifiedName~Solreign` integration band before landing.

Do not describe the evidence as a full upstream integration run or a
warning-free repository. No push, canonical merge, package, deploy, restart,
publication, CVar activation, launcher/hub change, credential use, live/player
data access, or production mutation occurred. Claude remains the integrator.
