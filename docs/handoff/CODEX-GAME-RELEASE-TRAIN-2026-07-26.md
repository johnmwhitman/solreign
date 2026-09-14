# Claude handoff — GAME release-train composition

## Parked candidate

- Worktree:
  `/Users/johnwhitman/AI/solreign-trees/codex-game-release-train-20260726`
- Branch: `codex/evidence-game-release-train-20260726`
- Canonical base:
  `6368757ee6b980db829ad06c7c21b08a9f61c6f1`
- Exact tested candidate:
  `aa5a51107bb312d2e7232ec70ca7327e0fe927b2`
- Exact tested tree:
  `1a7c03f73be16baba2b5b62d168e54849ce6aa08`
- RobustToolbox:
  `960edb32c4dd417496e4667177625d8c3cb14f7e`
- Receipt:
  `docs/receipts/CODEX-GAME-RELEASE-TRAIN-2026-07-26.md`
- Kill-date: 2026-08-09

## Outcome

The minimal current-head release train is technically green:

- bounded stale-anchor FX fail-soft;
- Station Audit markup repair plus forced-inspection regression proof;
- Contracts explainer parity on all seven rotation maps; and
- one test-only persistent-Ledger hermeticity correction discovered during
  exact composition.

Measured on the exact candidate:

- YAML/prototypes: clean;
- `_Solreign` units/prototypes: 2,467 pass / 0 fail / 2 Windows-only skips;
- authoritative SOLREIGN integration: 473 pass / 0 fail / 4 skips / 477 total;
- focused order-dependent reproducer after repair: 46 pass / 0 fail / 1 skip.

## Claude landing order

1. `bba52f6898cb7fc4430a2a12e83dfbec2f7242e7`
2. `9b9bd6e31d11047c31f7bba96c2be9cd2de51a9b`
3. `7669ba2f618663d17d292d92f6792d794bc4a5c1`
4. `e205e7454a089c5588806cf68bceafe8c072b5fd`
5. test-only correction from local commit
   `aa5a51107bb312d2e7232ec70ca7327e0fe927b2`

The fifth item is not an independent cherry-pick from another branch; review
its one-file diff and either cherry-pick the local commit after the four source
commits or reproduce the same current-round assertion in Claude's integration
branch.

Do not substitute the docs-only FX tip, the stale Contracts documentation tip,
the optional sprite package, or the HTN diagnostic into this core order.

## Remaining release gates

- Rebase/preview on the actual canonical head if it advances.
- Rebuild and rerun the authoritative band on the landed candidate.
- Run Windows CI for the two skipped ACL tests.
- Keep package identity, client/server pin, restore, launcher/hub join, live
  round, activation, and deployment evidence separately gated.

No push, canonical merge, package, deploy, restart, publication, CVar
activation, launcher/hub change, credential use, live/player data access, or
production mutation occurred. Claude remains the integrator.
