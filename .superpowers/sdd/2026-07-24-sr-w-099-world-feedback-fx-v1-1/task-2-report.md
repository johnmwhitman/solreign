# SR-W-099 Task 2 Report — Default-off Observer and Real-system Behavior

## Files changed

- `Content.Server/_Solreign/FX/Consumers/SolreignWorldFeedbackSystem.cs`
- `Content.Server/_Solreign/FX/Consumers/SolreignWorldFeedbackMetrics.cs`
- `Content.Shared/CCVar/CCVars.SolreignFxConsumers.cs`
- `Content.IntegrationTests/Tests/_Solreign/FX/Consumers/SolreignWorldFeedbackIntegrationTest.cs`
- This mandatory report.

The pre-existing dirty `task-1-report.md` was preserved and is not part of Task 2.

## TDD receipt

### RED

The first narrow compile after adding the tests failed because the observer CVar did not exist:

```sh
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c Release --no-restore \
  --filter 'Name~WorldFeedbackCVars|Name~ThreeGateTruthTable' \
  --verbosity minimal -m:1 /nodeReuse:false
```

```text
Content.IntegrationTests/.../SolreignWorldFeedbackIntegrationTest.cs(...): error CS0117:
'CCVars' does not contain a definition for 'SolreignFxWorldFeedbackObserveEnabled'
```

After adding only the CVar and closed metric surface, the same focused tests compiled. The local
test host first required a sandbox exception for its loopback socket, then produced the intended
behavioral RED:

```text
Failed WorldFeedback_ThreeGateTruthTable_BoundsDeliveryAndObservation
master=False, delivery=False, observe=True
Expected: 1.0d
But was:  0.0d

Failed!  - Failed: 1, Passed: 1, Skipped: 0, Total: 2
```

This demonstrated that the exact CVar contract already passed while observe-only behavior was
still absent.

### GREEN

The eight-combination truth table and exact dormant CVar contract passed after the consumer
implementation:

```text
Passed!  - Failed: 0, Passed: 2, Skipped: 0, Total: 2, Duration: 18 s
```

The process-global observer and delivery gates remain at each scenario's requested values across
the controlled source event, post-event ticks, and assertions. The connected test session's
superseded randomly spawned body is detached and deleted before gates are enabled, preventing it
from introducing unrelated world damage while still exercising the real systems. The complete
Task 2 fixture then passed:

```sh
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c Release --no-restore \
  --filter 'FullyQualifiedName~SolreignWorldFeedbackIntegrationTest' \
  --verbosity quiet -m:1 /nodeReuse:false
```

```text
Passed!  - Failed: 0, Passed: 9, Skipped: 0, Total: 9, Duration: 7 m 12 s
```

## Review fix round 1

Review identified that the first fixture revision enabled delivery/observation only synchronously
around `ChangeDamage`, then disabled both before post-event ticks and assertions. It also counted
one delivered electrocution cue and one observation without directly bounding raiser attempts.

### RED

The truth-table test was first changed to set all three CVars to the requested scenario and assert
that delivery and observation remained there through post-event ticks. The existing helper still
disabled them immediately after `ChangeDamage`, producing the required behavioral RED:

```sh
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c Release --no-restore \
  --no-build --filter 'Name~ThreeGateTruthTable' --verbosity minimal \
  --logger 'console;verbosity=normal' -m:1 /nodeReuse:false
```

```text
Failed WorldFeedback_ThreeGateTruthTable_BoundsDeliveryAndObservation
master=False, delivery=False, observe=True: observation must remain enabled through post-event ticks
Expected: True
But was:  False
Failed: 1, Passed: 0, Total: 1, Duration: 12 s
```

### GREEN

The test helpers no longer toggle process-global CVars around individual method calls. Every
scenario now sustains its master/delivery/observation state across the source event, ticks,
metric capture, and cue assertions; cleanup remains in each test's `finally` block. Because the
connected fixture starts with a random player body, it attaches the session to the controlled
atmosphere-inert skeleton and queues the superseded body for deletion before enabling gates.

The electrocution test also snapshots `SolreignFxDiagnosticsSystem.CountersForTests` around the
successful real event and asserts broadcast delta exactly `1` and targeted delta `0`. The
broadcast correlation counter increments at raiser-attempt creation, before egress coalescing, so
one delivered cue can no longer hide duplicate raiser attempts. Existing production already made
exactly one attempt, so this added assertion passed without a production change; no separate
failing behavior was fabricated for that coverage.

Focused review targets:

```sh
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c Release --no-restore \
  --filter 'Name~ThreeGateTruthTable|Name~TryDoElectrocution' --verbosity minimal \
  --logger 'console;verbosity=normal' -m:1 /nodeReuse:false
```

```text
Test Run Successful.
Total tests: 2
     Passed: 2
 Total time: 20.4452 Seconds
```

Complete narrow world-feedback fixture:

```sh
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c Release --no-restore \
  --no-build --filter 'FullyQualifiedName~SolreignWorldFeedbackIntegrationTest' \
  --verbosity minimal --logger 'console;verbosity=normal' -m:1 /nodeReuse:false
```

```text
Test Run Successful.
Total tests: 9
     Passed: 9
 Total time: 1.0349 Minutes
```

Review-round changes are test/report only:

- `Content.IntegrationTests/Tests/_Solreign/FX/Consumers/SolreignWorldFeedbackIntegrationTest.cs`
- This Task 2 report.

## Regression verification

Existing FX confidentiality and two-client PVS integration tests:

```text
Passed!  - Failed: 0, Passed: 3, Skipped: 0, Total: 3, Duration: 24 s
```

Existing profile-gate and render-recipe accessibility tests:

```text
Passed!  - Failed: 0, Passed: 63, Skipped: 0, Total: 63, Duration: 6 ms
```

World-feedback classifier rules:

```text
Passed!  - Failed: 0, Passed: 33, Skipped: 0, Total: 33, Duration: 13 ms
```

## Implementation

- Added dormant server-only `solreign.fx.world_feedback_observe`, default `false`.
- Preserved the existing master and delivery CVar names, flags, and dormant defaults.
- Added one process-lifetime counter named `solreign_fx_world_feedback_total` with labels exactly
  `class` and `outcome`.
- Pre-created exactly ten legal children. Callers record through a closed enum and cannot provide
  arbitrary labels.
- Returns before target/delta classification when both delivery and observation are disabled.
- Records positive non-kinetic final damage as `non_kinetic/filtered` only when observing and
  never sends it to the raiser.
- Records kinetic and electrocution shadow candidates without calling the raiser when delivery is
  disabled.
- When delivery is enabled, relies on the raiser's existing master gate and records only its
  accepted-or-coalesced boolean or rejection when observation is enabled.
- Treats the successful `ElectrocutedEvent` as one independent electrocution source event. Its
  Shock `DamageChangedEvent` may independently record one filtered observation; no correlation
  state was added.

## Privacy and scope self-review

- Metric dimensions contain no entity, session, account, coordinate, role, damage-value,
  relationship, map, secret, or cue identifiers.
- Added no logging, identity-bearing state, endpoint, export activation, integration, or external
  publication.
- Metric child creation is limited to ten fixed `WithLabels` calls; no arbitrary label API is
  exposed.
- Observe-only paths return before anchor conversion and never call the raiser.
- Empty, zero-only, and healing-only deltas produce no observation.
- `git diff --check` passed.
- Task 2 code changes are limited to the four required paths. The pre-existing Task 1 report
  remains untouched by this task.

## Concerns

No Task 2 defect is known. Focused integration builds emit existing repository NU1510 and
obsolete-API warnings. The full fixture is intentionally slow because it creates isolated
client/server pairs; all selected tests passed.

## Commit

`feat(solreign): add dormant world feedback observer` (the final commit SHA is supplied with the
task handoff).
