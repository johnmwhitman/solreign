# Claude handoff — GAME integration watchdog

## Parked candidate

- Worktree:
  `/Users/johnwhitman/AI/solreign-trees/codex-game-integration-watchdog-20260727`
- Branch: `codex/game-integration-watchdog-20260727`
- Base: `fa12e6ee0295a42b8efb97ce2633f2816bbb92ef`
- Implementation commit: `790a918eeb`
- RobustToolbox:
  `960edb32c4dd417496e4667177625d8c3cb14f7e`
- Receipt:
  `docs/receipts/CODEX-GAME-INTEGRATION-WATCHDOG-2026-07-27.md`
- Kill-date: 2026-08-10

## What changed

`PoolManagerTestEventHandler` now resolves one immutable watchdog configuration
before starting the test pool. The default remains 20/21 minutes. An explicitly
valid `SOLREIGN_INTEGRATION_WATCHDOG_MINUTES` value may extend it through 30/31
minutes; malformed and out-of-policy values fail loudly before startup.

The new pure configuration parser is covered by 15 focused tests. The exact
candidate also passed a strict single-node build with 0 errors, a valid
environment smoke, an invalid fail-closed smoke, diff hygiene, and gitleaks.

This is test infrastructure only. It does not alter production runtime,
content, maps, CVars, RobustToolbox, launcher behavior, or hub compatibility.

## Review notes

Grok returned GO on the exact diff with no P0/P1 finding. MiniMax's external
endpoint returned HTTP 504, so no second-provider verdict is represented.
The pre-existing uncancelled fire-and-forget timeout tasks remain a documented
P2 harness concern outside this bounded slice.

## Integration procedure

1. Revalidate the then-current GAME master and branch ownership.
2. Review and cherry-pick `790a918eeb`.
3. Retain the immediately following documentation commit from this branch.
4. Rerun the 15 focused configuration tests.
5. Run the strict integration-project build.
6. Run the complete `FullyQualifiedName~Solreign` band on the resulting exact
   composition before relying on the extended timeout for a feature verdict.

Do not infer integration, push, package, deploy, restart, activation, launcher,
hub, credential, live-data, or production authority from this handoff.
