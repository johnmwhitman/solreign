# GAME activation-contract reconciliation receipt

Date: 2026-07-25

Repository: GAME

Branch: `codex/solreign-activation-contract-reconcile-20260725`

Base: `04f3f96cc7b76bdd862603f21fa7200e717f179c`

Verdict: **HOLD — reviewable test-only package, not release-qualified**

Authority: evidence and integration input only. This receipt authorizes no merge, push, deploy,
restart, public release, CVar activation, or live mutation. Claude remains the integrator.

## Change boundary

The package adds `Content.Tests/_Solreign/ActivationCVarContractTests.cs` and changes eleven
SOLREIGN integration fixtures. It changes no production path.

The unit contract pins twelve deliberately activated features:

- Shift Archive
- Station Audit and Station Audit Inspection
- Echo and Mark
- Fleet Operations
- Records Terminal
- Directives Fax
- Providence reactive behavior
- World Feedback FX and FX Cue v1
- Movement Bob

Each case verifies the exact CVar name, `true` default, and flags. Ten are `SERVERONLY`; FX Cue v1
and Movement Bob are `REPLICATED | SERVER`.

Fixtures now arrange kill-switch state explicitly, capture and restore mutable CVar state where the
runtime lifecycle permits it, and account for setup-time enabled artifacts. Echo and Mark compare
exact entity UID sets rather than counts, exclude seeded leakage, and delete gardens only for the
test station. Directives Fax verifies the real startup rule and separately exercises the no-rule
query branch without claiming that a mid-round toggle terminates an active rule.

Three FX files were imported byte-for-byte from parked reviewed commit
`16540bd0b111c1320325d64eb93746bf30932e1b`; the old aggregate commit was not cherry-picked.

## Verification evidence

### Central activation contract

Latest repeated result: **12 passed, 0 failed, 0 skipped** in 16 ms.

### Final exact changed-fixture integration band

Result: **43 passed, 4 failed, 0 skipped, 47 total** in 2m39s.

All four failures were teardown/startup error-log failures with:

`system.solreign_fx_cue: Validate:AnchorEntityUnresolvable`

No changed assertion failed. This is the separately isolated RobustToolbox full-state event-timeline
race: a stale queued FX cue can be delivered after the full snapshot removes its anchor.

### Broad `FullyQualifiedName~Solreign` observation

Result: **437 passed, 29 failed, 3 skipped, 469 total** in 16m47s.

Most failures were the same FX anchor race across changed and untouched systems. The filter also
selected AntagGhostRole cases through argument text; their 28-case Saltern setup cascade is resolved
only on the separate, unstacked `codex/solreign-saltern-map-yaml-20260725` branch at
`e22d1b2b41db33e41d2955611e79325abe671905`. One FirstDeath teardown failure was separate. None was
suppressed or relabeled green.

### Independent review

The first review found three issues: count-only Echo/Mark assertions, ownership-blind garden
deletion, and an inaccurate Directives cleanup rationale. All were corrected. Final narrow
rereview: **PASS, no remaining finding in scope**. `git diff --check` was clean.

## Product semantic HOLDs

These require separate production-aware decisions and are not fixed by a test-only reconciliation:

1. Directives Fax gates future `RoundStarting` work but does not terminate an active rule, report,
   or streak mid-round.
2. Shift Archive describes an immediate kill switch, but an open or in-flight UI is not invalidated.
3. Fleet emergency jettison intentionally lacks the feature gate; retaining a safety release while
   ordinary Fleet behavior is disabled is a design tradeoff.

Fleet and Shift runtime transition coverage should accompany those decisions, not be invented here.

## Integration order

1. Review this test-only commit independently.
2. Combine it with the Saltern repair commit `e22d1b2b41db33e41d2955611e79325abe671905`.
3. Separately land a deterministic RobustToolbox regression and the full-state event-floor repair.
4. Re-run the exact 47-case band and the broad SOLREIGN band from the combined tree.

Until those steps are green, this package must remain HOLD.
