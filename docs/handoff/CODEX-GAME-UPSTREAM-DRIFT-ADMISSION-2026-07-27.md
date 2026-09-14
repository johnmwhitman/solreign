# Codex GAME upstream-drift admission handoff

## State

`PARKED / PASS-AS-LOCAL-INCOMPLETE-HISTORY-EVIDENCE`

- Branch: `codex/game-upstream-drift-admission-20260727`
- Worktree:
  `/Users/johnwhitman/AI/solreign-trees/codex-game-upstream-drift-admission-20260727`
- Base: `a4fbb1758644d5827a4d92348c0767e94481ff62`
- Implementation: `4dac5ebf484764e5f08ce10e34ed10adc3dace8b`
- Full receipt:
  `docs/receipts/CODEX-GAME-UPSTREAM-DRIFT-ADMISSION-2026-07-27.md`

## What is ready

A read-only, bounded, deterministic local Git audit now replaces the unsafe
historical rehearsal for drift inventory. It reports divergence, review
categories, conservative overlap, and parent-repository RobustToolbox pointer
changes without fetching, merging, building, recursing into the engine, or
mutating the worktree.

The current shallow-object inventory measured 680 fork-only commits, 243
upstream-only commits, 2,066 upstream-only paths, and 33 conservative overlap
paths. The fixture suite passes 44/44 and the final actual audit preserved
HEAD, refs, status, staged diff, and unstaged diff byte-for-byte.

## What is not ready

This is not a full-history result and must not be cited as upstream freshness,
merge safety, build compatibility, launcher/hub compatibility, security
correctness, license clearance, release readiness, or deploy authority.

## Consume

1. Review the implementation commit and the full receipt.
2. Land only through the named integrator's normal process; this lane did not
   merge or push.
3. Run the tool next in a disposable full-history clone without
   `--allow-incomplete-history`.
4. Keep human review and a separate disposable merge rehearsal as independent
   gates.

No live or production action is required to consume this handoff.
