# SOLREIGN activation-contract reconciliation

Date: 2026-07-25

Repository: GAME

Branch: `codex/solreign-activation-contract-reconcile-20260725`

Base: `04f3f96cc7b76bdd862603f21fa7200e717f179c`

Status: implemented as a test-only HOLD package; Claude remains the integrator.

## Purpose

Reconcile integration fixtures with the twelve capabilities intentionally activated on the current
game master. Preserve runtime kill-switch coverage by arranging disabled state explicitly rather
than relying on obsolete default-off assumptions.

## Scope

1. Pin the name, default, and flags of all twelve activated CVars in one unit contract.
2. Update stale integration setup and assertions for the default-on contract.
3. Import only the three already-reviewed FX activation fixtures from the prior parked test branch.
4. Run serialized focused and broad SOLREIGN batteries.
5. Classify all failures without suppression and hand the package to Claude as HOLD.

No production CVar, component, system, prototype, map, engine, or deployment file is changed.

## Acceptance

- The twelve-CVar contract passes.
- Changed fixtures compile and exercise real enabled and explicit disabled paths.
- No assertion introduced by this slice fails.
- Independent review has no remaining finding.
- Existing engine and unstacked-map failures remain visible and block a green release verdict.
