# Codex Nocturne HTN lifecycle receipt — 2026-07-26

## Verdict

`PASS-AS-EVIDENCE`; no production correction is justified.

This branch adds one deterministic integration diagnostic and documentation.
It changes no game, engine, map, prototype, or runtime behavior. Claude may
land the regression if its maintenance value is judged worthwhile, but this
receipt is not merge, release, activation, or deploy authority.

## Identity

- Repository: GAME
- Base: `6368757ee6b980db829ad06c7c21b08a9f61c6f1`
- Branch: `codex/solreign-htn-nocturne-lifecycle-20260726`
- Test and evidence commit:
  `1d7819cea6657fd838e823f2c161a9370f157aa6`
- Owned runtime paths: none
- Owned test path:
  `Content.IntegrationTests/Tests/_Solreign/SolreignNocturneHtnLifecycleIntegrationTest.cs`
- Historical seed: `736584703`

## Question tested

An earlier mixed integration run emitted an HTN planning fatal while
`BasicTrashVariationPass_ScattersTrashAcrossStation_NotSinglePile` returned
its pair. The stack passed through
`CoordinatesNotInRangePrecondition.IsMet` and
`NPCBlackboard.TryGetEntityDefault`. The diagnostic separates three plausible
causes:

1. server-instance dependency injection missing from the server-loaded
   `FollowCompound` precondition;
2. a component-owned plan resuming after its owner is removed by the pool's
   distinct `FlushEntities()` phase;
3. an old plan queue remaining reachable after `RestartRound()`.

The test loads real Nocturne content, selects an HTN owner whose transform is
on the newly loaded Nocturne map, evaluates the server-loaded
`FollowCompound` precondition, installs a controlled barrier/sentinel plan
under that component's real cancellation token, and observes each lifecycle
boundary.

## Results

### Untouched-base diagnostic

Command:

```text
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj \
  --filter "FullyQualifiedName~SolreignNocturneHtnLifecycleIntegrationTest" \
  -m:1 -nodeReuse:false -p:UseSharedCompilation=false \
  --logger "trx;LogFileName=/private/tmp/codex-solreign-htn-three-arm-final.trx"
```

Result: `1 passed / 0 failed / 0 skipped`, 10.728 seconds.

All three arms passed before any production edit:

- the loaded coordinate precondition evaluated with the current server's
  dependencies;
- `ComponentShutdown` canceled the waiting plan during entity flush;
- the plan never reached its sentinel after cancellation;
- the canceled job reached `Finished` with no plan and no exception;
- restart replaced `_planQueue`;

TRX:
`/private/tmp/codex-solreign-htn-three-arm-final.trx`

An earlier simpler version also passed `1 / 0 / 0`. Independent review then
required tighter map ownership, terminal-job, cleanup, and claim-scope
assertions. The first reviewed run correctly caught a test-expectation error:
the cooperatively canceled job task is canceled, not successfully completed.
Correcting that assertion—without a production change—produced the green
result above.

### Neighboring focused tests

Command selected:

- this diagnostic;
- `SolreignHtnPlanQueueRoundRestartTest`;
- `BasicTrashVariationPass_ScattersTrashAcrossStation_NotSinglePile`.

Result on the uncomposed base: `2 passed / 1 failed / 0 skipped`, 19 seconds.
The sole failure was not HTN: the existing restart regression's client
teardown reported the already-isolated
`SolreignFx Validate:AnchorEntityUnresolvable` warning. That is owned by the
separate parked FX fail-soft lane and is not changed here.

TRX:
`/private/tmp/codex-solreign-htn-lifecycle-focused.trx`

## Interpretation and non-actions

- Do not add null guards to `NPCBlackboard` or
  `CoordinatesNotInRangePrecondition`; the suspected dependency is present in
  the deterministic reproduction.
- Do not change HTN queue replacement; the old queue is retired.
- Do not change pool flush ordering; component-owned cancellation prevents
  advancement after flush.
- Do not claim the historical fatal is disproven. The test establishes that
  the three suspected static seams are sound under the pinned standalone
  scenario; it does not reconstruct the original full-suite ordering.
- The focused uncomposed result is not a new regression and is not green
  release evidence. A separate release-qualification composition is
  responsible for proving behavior after the parked FX and Station Audit
  candidates are included; this receipt makes no composition-readiness
  claim.

## Claude handoff

Review the diagnostic for brittleness and maintenance value. If retained, it
should land as an evidence/regression test only. If rejected, the production
tree needs no compensating change. Continue release qualification using the
reviewed FX and Station Audit candidates; investigate HTN again only if a
fresh exact-head run records a new fatal with a reproducible seed and owner.
