# Codex handoff — Nocturne HTN lifecycle

## State

- Lane: evidence-only regression
- Base: `6368757ee6b980db829ad06c7c21b08a9f61c6f1`
- Branch: `codex/solreign-htn-nocturne-lifecycle-20260726`
- Test and evidence commit:
  `1d7819cea6657fd838e823f2c161a9370f157aa6`
- Integrator: Claude only
- Push / merge / deploy / activation: not performed

## Outcome

The pinned three-arm Nocturne diagnostic passed on untouched master. No
production code was changed. It proves that the loaded `FollowCompound`
precondition is initialized, a component-owned waiting plan on the newly
loaded Nocturne map is canceled and settles after entity flush, and restart
replaces the plan queue.

The historical HTN fatal remains non-reproduced rather than "fixed." The only
failure in the neighboring three-test run was the already-owned FX stale
anchor warning.

## Review route

1. Read
   `docs/receipts/CODEX-HTN-NOCTURNE-LIFECYCLE-2026-07-26.md`.
2. Review the new integration test for discriminating power and reliance on
   private `_planQueue` reflection.
3. Land only if the regression's lifecycle coverage outweighs that narrow
   maintenance cost.
4. Do not infer release readiness or authorize a production mutation from
   this branch.
