# SR-W-099 Task 1 Report — Truthful Physical Classifier

## Files changed

- `Content.Shared/_Solreign/FX/Consumers/SolreignWorldFeedbackRules.cs`
- `Content.Tests/_Solreign/FX/Consumers/SolreignWorldFeedbackRulesTests.cs`

## TDD receipt

### RED

Command:

```sh
dotnet test Content.Tests/Content.Tests.csproj --filter 'FullyQualifiedName~SolreignWorldFeedbackRulesTests'
```

The sandboxed first attempt could not create MSBuild named pipes (`SocketException (13):
Permission denied`), so the command was rerun outside the sandbox. It restored and compiled the
fresh worktree, then failed exactly on the closed-classifier behavior still absent from the prior
implementation:

```text
Failed AppliedPositiveDamage_MixedPhysicalAndInternalDamage_ReturnsPhysicalSubtotal
Expected: 19.9899998f
But was:  39.9900017f

Failed AppliedPositiveDamage_ReleasedNonPhysicalType_ReturnsZero("Shock")
Expected: 0
But was:  22.0f

Failed AppliedPositiveDamage_ReleasedNonPhysicalType_ReturnsZero("Holy")
Expected: 0
But was:  22.0f

Failed AppliedPositiveDamage_ReleasedNonPhysicalType_ReturnsZero("Heat")
Expected: 0
But was:  22.0f

Failed AppliedPositiveDamage_ReleasedNonPhysicalType_ReturnsZero("Asphyxiation")
Expected: 0
But was:  22.0f

Failed AppliedPositiveDamage_UnknownEmptyAndDefaultTypes_ReturnZero
Multiple failures: unknown, empty, and default identifiers each expected 0 but were 22.0f

Failed!  - Failed:     6, Passed:    14, Skipped:     0, Total:    20
```

### GREEN

Command (run after the implementation and once again after formatting-only refactor):

```sh
dotnet test Content.Tests/Content.Tests.csproj --no-restore --filter 'FullyQualifiedName~SolreignWorldFeedbackRulesTests'
```

Output:

```text
Passed!  - Failed:     0, Passed:    20, Skipped:     0, Total:    20, Duration: 17 ms - Content.Tests.dll (net10.0)
```

The project emits existing repository warnings (NU1510 and obsolete API warnings) during the
focused build; none originate from the two Task 1 files.

## Implementation

`AppliedPositiveDamage` now makes one pass over the damage dictionary. It ignores non-positive
entries and adds only the closed physical IDs `Blunt`, `Slash`, `Piercing`, and `Structural`. The switch adds no
steady-state allocations and naturally rejects released non-physical, unknown, empty, and default
identifiers. `ClassifyAppliedDamage` and its non-finite scalar defense are unchanged.

## Self-review

- Scope is limited to the required production and test files, plus this mandated report.
- The tests cover every permitted physical ID; released `Shock`, `Holy`, `Heat`, and
  `Asphyxiation`; unknown, empty, and default IDs; zero and negative values; the 19.99/20 scalar
  boundary; mixed internal/physical damage; healing with physical damage; and multiple physical
  entries.
- `git diff --check` passed.

## Concerns

None for Task 1. The full `Content.Tests` project compiles broad dependencies for the focused
filter and reports pre-existing warnings, but the selected rules suite passes.

## Commit

`fix(solreign): classify only physical world feedback damage` (the final commit SHA is supplied
with the task handoff).

## Fix round 1

Review identified that `Structural` is a required physical damage ID. Its test was added before
the production change and failed as expected:

```sh
dotnet test Content.Tests/Content.Tests.csproj --no-restore --filter 'FullyQualifiedName~SolreignWorldFeedbackRulesTests'
```

```text
Failed AppliedPositiveDamage_AllowedPhysicalType_ReturnsPositiveSubtotal("Structural")
Expected: 22.0f
But was:  0.0f

Failed!  - Failed:     1, Passed:    20, Skipped:     0, Total:    21
```

The closed classifier was then extended with `Structural`. The same focused command passed:

```text
Passed!  - Failed:     0, Passed:    21, Skipped:     0, Total:    21, Duration: 39 ms - Content.Tests.dll (net10.0)
```

The original RED transcript above is now complete: its six failed tests were mixed physical and
internal damage; released `Shock`, `Holy`, `Heat`, and `Asphyxiation`; and unknown/empty/default
identifiers. The multi-assert unknown/empty/default test reports three assertion details under its
single failed test result.

## Fix round 2

### Files

- `Content.Shared/_Solreign/FX/Consumers/SolreignWorldFeedbackRules.cs`
- `Content.Tests/_Solreign/FX/Consumers/SolreignWorldFeedbackRulesTests.cs`

### RED

Command:

```sh
dotnet test Content.Tests/Content.Tests.csproj --no-restore --filter 'FullyQualifiedName~SolreignWorldFeedbackRulesTests'
```

The requested `DamageSpecifier` classifier and its closed result enum did not yet exist, so the
test project failed at compile time rather than silently exercising the old subtotal API:

```text
Content.Tests/_Solreign/FX/Consumers/SolreignWorldFeedbackRulesTests.cs(162,90): error CS0246:
The type or namespace name 'SolreignDamageFeedbackCue' could not be found

Content.Tests/_Solreign/FX/Consumers/SolreignWorldFeedbackRulesTests.cs(160,22): error CS0103:
The name 'SolreignDamageFeedbackCue' does not exist in the current context

Content.Tests/_Solreign/FX/Consumers/SolreignWorldFeedbackRulesTests.cs(161,19): error CS0103:
The name 'SolreignDamageFeedbackCue' does not exist in the current context
```

### GREEN

Command:

```sh
dotnet test Content.Tests/Content.Tests.csproj --no-restore --filter 'FullyQualifiedName~SolreignWorldFeedbackRulesTests'
```

Output:

```text
Passed!  - Failed:     0, Passed:    29, Skipped:     0, Total:    29, Duration: 15 ms - Content.Tests.dll (net10.0)
```

### Implementation and self-review

`ClassifyDamage(DamageSpecifier)` makes one pass over the dictionary, tracks whether any positive
entry exists, and totals only the closed physical IDs. It returns `None` for no positive entries,
`NonKinetic` for positive non-physical-only input, and kinetic light/heavy from the physical
subtotal without rescanning. The scalar finite/threshold helper remains unchanged. Tests cover
empty-equivalent zero/negative input, unknown/internal/Shock non-kinetic input, mixed input,
healing, and both thresholds.

Commit: `39fffb35ebe7e635682c4a2c7d177e93c0a94d0f` — `fix(solreign): classify applied damage in one pass`.

Concern: the project emits existing NU1510 and obsolete API warnings while compiling its broad
focused-test dependency graph; the final selected suite passed without test failures.

## Fix round 3

Added coverage for an empty `DamageSpecifier => None` and for all closed physical IDs (`Blunt`,
`Slash`, `Piercing`, `Structural`) producing kinetic output. This was a coverage expansion: the
new tests passed immediately against the existing one-pass implementation, so no fabricated RED
receipt and no production change.

Command:

```sh
dotnet test Content.Tests/Content.Tests.csproj --no-restore --filter 'FullyQualifiedName~SolreignWorldFeedbackRulesTests'
```

Output:

```text
Passed!  - Failed:     0, Passed:    33, Skipped:     0, Total:    33, Duration: 18 ms - Content.Tests.dll (net10.0)
```

Self-review: scope is test-only (plus this gitignored report); the existing classifier correctly
handled the new cases, so no production repair was needed. Existing repository build warnings
remain unrelated.
