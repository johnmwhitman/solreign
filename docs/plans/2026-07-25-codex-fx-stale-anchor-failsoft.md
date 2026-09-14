# Codex plan — stale-anchor FX fail-soft

Date: 2026-07-25

Branch: `codex/solreign-fx-stale-anchor-failsoft-20260725`

Base: `db7c1321fbd0532f0010342a4b69678863b37702`

## Purpose

Keep SOLREIGN's explicitly lossy broadcast cosmetic FX path observable without making the
ordinary race between an ephemeral server anchor and its client-side entity state warning-fatal.
Do not weaken validation, confidentiality, or rendering acceptance.

## Boundaries

- Content-scoped change only; no RobustToolbox or protocol change.
- Only a known, allowlisted `Broadcast` effect with a supplied `NetEntity` that is genuinely
  absent locally may enter the informational bucket.
- Unknown effects, `DetailOnly` effects, malformed payloads, nullspace/transform failures,
  non-finite values, and every other validation failure remain warnings.
- The cue remains rejected and never renders.
- Existing bounded aggregation, count, and correlation sample remain intact.
- No merge, push, deploy, restart, activation, launcher, hub, or live authority.

## Test-first sequence

1. Add a pure policy matrix and observe the missing-type compile failure.
2. Implement the narrow predicate and exact informational-reason match.
3. Run the focused policy, aggregator, and wire-validator tests.
4. Add a real connected client/server fixture that deletes an ephemeral anchor before client
   processing and proves wire receipt without rendering.
5. Run the complete `_Solreign.FX` unit namespace.
6. Obtain independent confidentiality/fail-closed review.
7. Record exact evidence, commit, synthetic-merge check, and park for Claude.

## Integration order

This production fix is independent of the activation-test reconciliation and Saltern YAML repair.
Claude should preview their combined integration, then rerun the exact changed-fixture band. The
earlier disconnect queue-clear experiment remains a separate RobustToolbox lane.
