# SR-W-099 Task 3 Report — Production-helper performance proof

## Outcome

The benchmark harness, production routing helper, focused tests, and immutable raw evidence are
committed. The task's performance verdict is **HOLD**: all measured classifier paths allocate
0 B/op, but the stable N=8 classifier ratios exceed the plan's 1.15x hard gate. No integrator
exception was assumed.

Canonical evidence and decision options:

`docs/orchestration/receipts/SR-W-099-WORLD-FEEDBACK-FX-V1_1-BENCHMARK-2026-07-24/README.md`

## TDD receipt

The dispatch-helper tests were added first. Their first focused compile failed for the intended
reason:

```text
error CS0246: The type or namespace name 'SolreignWorldFeedbackDispatchRules' could not be found
```

After adding the tiny internal helper and making the live system call it:

```text
Passed!  - Failed: 0, Passed: 8, Skipped: 0, Total: 8
```

The combined production classifier and dispatch-helper cohort then passed:

```text
Passed!  - Failed: 0, Passed: 41, Skipped: 0, Total: 41
```

The helper preserves the two-CVar early gate and released light/heavy effect ID, intensity, and
duration parameters. It contains no entity, session, or mutable world state.

## Benchmark verification

- Release benchmark project build: succeeded on the identical sequential retry after a transient
  unrelated compiler exit 139; 0 errors and 262 existing repository warnings.
- BenchmarkDotNet: 28/28 benchmarks executed from clean committed HEAD
  `b7f16266d63ef81461638667595c68b3b0640666`, exit 0, 1m03s.
- Required recipes: physical subtotal, positive non-kinetic, and fixed mixed sequence.
- Required entry counts: 1, 3, and 8; N=0 retained as an explicitly inconclusive diagnostic.
- Frozen source: released v1 commit `403512014f16aaceba53bb90ef435c2393dca2e4`.
- Frozen baseline includes both the released positive-entry scan and released scalar threshold
  mapping, returning the same cue enum as v1.1. Its threshold is a local source-bound `20f`,
  while setup checks the current production threshold independently.
- Inputs are prebuilt outside measured bodies; both classifiers return their result.
- Classifier, routing helpers, and counter burst use separate benchmark categories.
- Cached-counter burst uses `OperationsPerInvoke = 10000`.
- All reported classifier rows allocate 0 B/op.

Corrected clean-HEAD rerun N=8 ratios:

| Recipe | Ratio |
| --- | ---: |
| Physical subtotal | 1.68x |
| Non-kinetic | 3.15x |
| Mixed | 2.61x |

The worst absolute v1.1 classifier mean is 12.8642 ns for N=8 non-kinetic input. Absolute cost does
not waive the approved relative gate. The receipt records mean, median, error, outliers, raw hashes,
and the integrator choices without selecting one.

## Commits

- `65ba1168128d32ca8b4ae9c572e4a4fb554c7a5f` — production helper, benchmarks, tests, and evidence.
- `e6eadadf4b0701e20b6f078a9b4386aa1fb6c59a` — preserve raw artifact bytes while keeping
  `git diff --check` clean through a path-scoped whitespace attribute.
- `b7f16266d63ef81461638667595c68b3b0640666` — bind the frozen v1 threshold to the released
  source and independently guard the production threshold before measurement.

No merge, push, deploy, restart, CVar activation, live p95/p99 measurement, client measurement, or
accessibility-window claim occurred.
