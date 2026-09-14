# Nocturne HTN lifecycle qualification

## Authority and scope

- Base: GAME `origin/master` `6368757ee6b980db829ad06c7c21b08a9f61c6f1`.
- Branch: `codex/solreign-htn-nocturne-lifecycle-20260726`.
- Claude remains the only integrator.
- This lane owns one deterministic integration diagnostic, the smallest
  evidence-proven correction, and its receipt/handoff.
- It does not own FX stale-anchor handling, Station Audit markup, maps,
  gameplay content, CVars, RobustToolbox protocol behavior, or release/deploy
  actions.

## Observed failure

The exact-head SOLREIGN integration band recorded 441 passing, 26 failing, and
2 skipped tests. Twenty-four failures include the already-owned stale-anchor
FX warning; one is the already-owned Station Audit markup failure. The
remaining distinct failure occurs while returning
`BasicTrashVariationPass_ScattersTrashAcrossStation_NotSinglePile`:

- server seed `736584703`;
- Nocturne loads the corgi and Renault HTN spawners;
- an HTN planning job faults in
  `CoordinatesNotInRangePrecondition.IsMet`;
- the call reaches `NPCBlackboard.TryGetEntityDefault` with an unusable
  entity-manager dependency and emits `[FATL]`.

The existing round-restart regression is insufficient. Production
`RestartRound()` raises `RoundRestartCleanupEvent` before flushing entities,
but the test-pool recycle path has a separate phase that flushes entities
before calling `RestartRound()`.

## Test-first classification

The new test must fail on real observable behavior and distinguish these
mechanisms:

1. **Initialization arm:** the loaded server's `FollowCompound` coordinate
   precondition can evaluate a valid blackboard before teardown. A failure means
   HTN prototype-generation initialization is incomplete.
2. **Flush-before-restart arm:** a real waiting HTN job cannot resume into its
   next sentinel precondition after entities are flushed but before the round
   restart. A failure means the pool/test lifecycle has an uncovered window.
3. **Restart arm:** round restart replaces the queue identity. A failure means
   `DiscardPlanQueue()` is incomplete. The existing dedicated restart
   regression owns the stronger leaked-job reachability proof.

Use a test-only barrier operator and sentinel precondition around real
`HTNPlanJob`/`HTNSystem` behavior. Pin server seed `736584703`; do not depend
on suite order or a neighboring fixture.

## Red/green rules

1. Add only the focused diagnostic and run it against the untouched base.
2. Preserve the RED output naming the failing arm.
3. Change only the seam proven by that arm:
   - initialization: HTN prototype/point-of-use initialization;
   - flush-before-restart: integration-pool recycle ordering;
   - post-restart: HTN queue lifecycle.
4. Reject `null => false`, broad exception suppression, or per-precondition
   guards: those silently disable AI decisions and leave other dependencies
   unsafe.
5. Re-run the focused diagnostic repeatedly, the existing HTN restart test,
   the affected Nocturne/trash test, and then the full SOLREIGN integration
   band on a synthetic composition containing the separately reviewed FX and
   Station Audit fixes.

## Exit

If the test reproduces a behavioral failure, the lane may be handed to Claude
as a correction only with:

- a witnessed RED-before and GREEN-after for the same mechanism;
- exact commit and synthetic-composition SHAs;
- zero new integration failures;
- independent review of lifecycle correctness and test discriminating power;
- no claim of package or release readiness unless every required gate passes.

If the untouched base passes all three arms, stop without production edits.
Preserve the diagnostic and exact result as an evidence-only handoff; do not
represent it as a bug fix or as evidence that the historical fatal was
imaginary.

## Observed outcome

The untouched base passed the three-arm diagnostic:

- loaded `FollowCompound` dependency injection was usable;
- flushing the real Nocturne HTN owner canceled its component-owned waiting
  plan before the sentinel could run;
- round restart replaced the planner queue.

Accordingly, this lane makes no production change. The historical fatal
remains an intermittent suite-order or lifecycle observation, while the
suspected static initialization, flush-cancellation, and queue-retirement
seams are now covered by an explicit regression.
