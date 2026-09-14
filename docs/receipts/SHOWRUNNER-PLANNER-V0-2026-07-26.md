# Showrunner planner v0 — verification receipt

Date: 2026-07-26
Branch: `codex/game-showrunner-planner-v0-20260726`
Implementation commit: `504054805294393ea4f05d45584d02d639d4d830`

## Scope receipt

Added:

- `Content.Server/_Solreign/Showrunner/ShowrunnerModels.cs`
- `Content.Server/_Solreign/Showrunner/ShowrunnerPlanner.cs`
- `Content.Tests/_Solreign/Showrunner/ShowrunnerPlannerTests.cs`

Static inspection found no Showrunner consumer outside these files and no runtime registration,
command, CVar, prototype, component, `GameTicker`, rule-start, or rule-end path.

## TDD receipt

1. Clean-base Season 1 sequencer baseline: **10/10 passed**.
2. New planner tests were written before production types; the first run failed compilation because
   the Showrunner API did not exist.
3. Initial implementation reached **18/19**; the one failure revealed an invalid test fixture
   (`MinPlayers` exceeded `MaxPlayers`). Correcting the fixture produced **19/19**.
4. Mutable-descriptor and duplicate-set canonicalization tests then failed **2/22** before snapshots
   and set normalization were implemented.
5. Bounded-enumeration and null-digest regressions failed **2/2** before their guards were added.
6. Mutable-refusal-reason regression failed **1/1** before `Array.AsReadOnly` was applied.
7. Final focused suite: **26/26 passed**.

The v0 fingerprint vector was independently calculated from the documented length-prefixed
canonical form:

`2c298f7dcc14ddb7d3d436551df2e15b3b5992f2d79c8756a1d277407cfbd9a3`

## Commands and results

Focused:

```text
dotnet test Content.Tests/Content.Tests.csproj \
  --filter "FullyQualifiedName~ShowrunnerPlannerTests" \
  --no-restore -m:1 -nodeReuse:false -p:UseSharedCompilation=false

Passed: 26, Failed: 0, Skipped: 0
```

Full unit:

```text
dotnet test Content.Tests/Content.Tests.csproj \
  --no-restore -m:1 -nodeReuse:false -p:UseSharedCompilation=false

Passed: 2906, Failed: 0, Skipped: 3, Total: 2909
```

Planner-branch SOLREIGN integration:

```text
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj \
  --filter "FullyQualifiedName~Solreign" \
  -m:1 -nodeReuse:false -p:UseSharedCompilation=false

Passed: 444, Failed: 22, Skipped: 3, Total: 469, Duration: 11m40s
```

Clean-master isolated first failure:

```text
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj \
  --filter "Name=DirectiveOutcomeQuery_FiredAfterAppendRoundEndText_ReusesTheCachedOutcome_NoDoubleCompute" \
  -m:1 -nodeReuse:false -p:UseSharedCompilation=false

Passed: 1, Failed: 0, Skipped: 0
```

Clean-master identical SOLREIGN integration:

```text
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj \
  --filter "FullyQualifiedName~Solreign" \
  --no-restore -m:1 -nodeReuse:false -p:UseSharedCompilation=false

Passed: 440, Failed: 26, Skipped: 3, Total: 469, Duration: 10m21s
```

The two complete integration runs share existing pool-order-sensitive FX teardown contamination.
The branch did not worsen the measured count. This is baseline comparison evidence, not a claim that
integration is green.

Merge validation:

```text
git fetch origin master
git merge-tree --write-tree origin/master 504054805294393ea4f05d45584d02d639d4d830
git rev-parse 504054805294393ea4f05d45584d02d639d4d830^{tree}

origin/master: 6368757ee6b980db829ad06c7c21b08a9f61c6f1
synthetic merge tree: e119de9b9ccefde262556f3f12ea238433b55cd5
```

## Review and orchestration receipt

Local read-only lanes covered:

- current-master Showrunner/runtime inventory;
- roadmap and capability-contract eligibility;
- planner/test architecture;
- deterministic/canonicalization code review;
- adversarial safety and test review.

Sanitized external drafts were also requested from Grok and MiniMax routes for architecture and
mechanical test-matrix pressure. They received no private source, credentials, player data, or
production state. Their output was treated as draft input; local code inspection, tests, and
independent review remained authoritative.

Both final local reviewers returned **PASS with no remaining P0-P2**.

## Explicit non-authority

This receipt proves a reviewed inert planning kernel and no measured unit/integration regression
against the current red baseline. It does not prove a capability contract is eligible, authorize a
production catalog, permit runtime wiring, complete SR-W-016, authorize merge, or authorize any
deployment or activation.
