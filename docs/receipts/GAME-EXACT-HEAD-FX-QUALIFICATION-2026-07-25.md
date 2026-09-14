# GAME exact-head FX qualification receipt

Date: 2026-07-25
Exact head: `04f3f96cc7b76bdd862603f21fa7200e717f179c`
RobustToolbox: `960edb32c4dd417496e4667177625d8c3cb14f7e`
Verdict: **HOLD — not package-qualified**

## Baseline evidence

| Gate | Result |
|---|---|
| `Content.YAMLLinter` DebugOpt build | PASS, 0 errors, 1,346 warnings, 1m42s |
| YAML executable | PASS, `No errors found in 34743 ms` |
| release guards | PASS, 2 passed |
| full `Content.Tests` | PASS, 2,865 passed / 0 failed / 3 skipped |
| focused FX integration before correction | FAIL, 5 passed / 3 failed / 1 skipped |

Build restore emitted `NU1900` warnings because the sandbox could not reach NuGet vulnerability
feeds. Compilation and the executable YAML validation completed successfully.

## Regressions corrected on this branch

### Activation-contract drift

Two integration tests still asserted the old default-off posture even though the deliberate
activation commits ship both `solreign.fx.cue_v1` and
`solreign.fx.world_feedback_v1` enabled:

- render-pool default test expected zero pooled entities and observed the bounded 198-entity pool;
- World Feedback gate test expected both switches false and observed both true.

The tests now protect the intended default-on posture while retaining explicit both-off,
master-off, and consumer-off behavior checks.

### Raw-wire test contamination

The real-PVS confidentiality test passed alone but failed after an FX-producing predecessor: the
far client observed one `impact_light`. A minimal unrelated real-ticker predecessor did not
reproduce it. Detailed traces showed recycled pairs receiving default-on World Feedback cues whose
entity anchors were unrelated to the explicit source under test.

Red evidence:

- World Feedback electrocution test followed by the PVS test: 1 passed / 1 failed.
- Focused FX band: 5 passed / 3 failed / 1 skipped.

Correction:

- raw-wire two-client fixtures request a fresh pair;
- both scenarios disable and restore the independent World Feedback producer;
- the two-client fixture's CVar cleanup restores captured prior values instead of forcing the
  historical `false` default.

Green evidence:

- exact two-test order reproducer: 2 passed / 0 failed;
- final focused FX band: **9 passed / 0 failed / 0 skipped**, 1m02s.

No player-facing production behavior or CVar value changed in this branch.

## Full SOLREIGN integration result

Command:

```text
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj --no-build -c DebugOpt \
  -m:1 -nodeReuse:false -p:UseSharedCompilation=false --filter "FullyQualifiedName~Solreign"
```

Final exact-state rerun: **433 passed / 29 failed / 7 skipped / 469 total**, 13m25s.

An earlier run before the final fixture-cleanup corrections measured 430 passed / 33 failed /
6 skipped in 14m45s. The final rerun above is authoritative for this branch state.

The failures form at least three independent release blockers:

1. Several integration tests still assert historical dormant defaults for features deliberately
   enabled by `eb664d7ec7d1ac40708c80231c5f19bdce6c1d6b` (Directives Fax, Echo, Mark,
   Movement Bob, and related activation paths).
2. Default-on FX emits or retains cues across round/test-pair teardown. After their anchors are
   deleted, the connected client reports
   `Validate:AnchorEntityUnresolvable`; the integration harness correctly treats those warnings as
   failures. This affected unrelated lifecycle, map/job-spread, Providence, antag, and Wingmate
   tests, so it cannot be dismissed as an FX-test-only artifact.
3. Late antag ghost-role setup encountered a separate pooled map-state failure:
   `YamlDotNet.Core.SemanticErrorException: Did not find expected <document start>` while
   `GameTicker.LoadMaps()` recycled a pair. This receipt does not attribute that failure to FX
   without a minimal reproducer.

Because the full band is red, map-load and package qualification were not run.

## Required successor work

Use a fresh branch from the then-current GAME master:

1. Reproduce one minimal non-FX test plus the anchor warning.
2. Determine whether teardown cues are created outside `GameRunLevel.InRound`, retained in an FX
   queue across run-level changes, or both.
3. Add a failing lifecycle test before changing production code.
4. Make the narrowest lifecycle correction; do not suppress the validation warning globally.
5. Reconcile the remaining stale activation assertions against `eb664d7ec7`.
6. Isolate the antag ghost-role map/YAML setup failure and distinguish product state from
   pooled-harness contamination.
7. Rerun focused FX, full SOLREIGN integration, map gates, then package qualification in that order.

This receipt is evidence, not merge, deploy, activation, or release authorization.
