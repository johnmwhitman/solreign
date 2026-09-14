# FX anchor allocation repair

Date: 2026-07-24 UTC / 2026-07-23 America/Chicago

Branch: `fix/codex-fx-anchor-allocation`

Base GAME commit: `a061988945557efd849e996f71c48dc32b80066d`

RobustToolbox commit: `960edb32c4dd417496e4667177625d8c3cb14f7e`

Production authority: **none**

## Outcome

`SolreignFxAnchorKey.Equals(SolreignFxAnchorKey)` no longer boxes the populated
right-hand nullable anchor. The implementation now compares presence first and
then calls the underlying typed `NetEntity.Equals(NetEntity)` or
`NetCoordinates.Equals(NetCoordinates)`.

No pool, lease, rendering, network, prototype, CVar, or engine behavior was
changed. Hashing and public identity semantics are unchanged.

## Causal evidence

The parked Effect Observatory v2 baseline observed coordinate-anchor scans
allocating exactly 32 bytes per active pool slot and twice that in the lease
path's two scans. It correctly treated nullable boxing as a hypothesis:

- observatory tip: `dc6973fb9a95fb17229bc827018de0dc47b54d5a`;
- accepted baseline source: `b3c2a69f66fe2b06797649b4afafd5c48c339fe1`;
- normalized baseline SHA-256:
  `c6327369f3948251c4b96b8f82ff9791c0b16453682673cbd9964ddd0e749aa6`;
- coordinate scan observations: 32 B per pool slot and 64 B per lease slot.

A focused Release-mode regression test then isolated the production equality
call. Before the implementation change, over 100,000 comparisons:

| Scenario | Allocated bytes | Bytes per comparison |
| --- | ---: | ---: |
| Equal entity anchors | 2,400,000 | 24 |
| Equal coordinate anchors | 3,200,000 | 32 |
| Coordinate vs entity | 2,400,000 | 24 |

The red run completed 4 tests with 3 failures and 1 pass. After the change, all
four tests pass and every equal, unequal, and cross-kind measurement window
reports exactly zero managed bytes.

The permanent test:

- constructs all operands before measurement;
- warms the comparison path for 100,000 calls;
- uses a non-inlined helper and consumes the comparison result;
- measures 100,000 calls in each of three independent windows;
- verifies the thread did not change;
- requires every window, not merely the best window, to allocate exactly zero;
- separately verifies default, entity, coordinate, cross-kind, inequality, and
  equal-value hash semantics.

Runtime for the evidence was .NET 10.0.9 on macOS arm64. This receipt makes no
.NET 9 claim.

## Verification

All commands ran from the isolated worktree with one heavy .NET process at a
time.

| Gate | Result |
| --- | --- |
| Focused allocation regression, Release | 4 passed, 0 failed |
| Broad `SolreignFx` unit filter, Release | 311 passed, 0 failed |
| Full `Content.Tests`, Release | 2,774 passed, 3 existing platform-specific skips, 0 failed |
| Solreign `Content.IntegrationTests`, Release | 428 passed, 1 intentional persistence skip, 0 failed |
| `git diff --check` | clean |

The full integration gate took 6.9 minutes and exercised real server/client,
map, round, privacy, and lifecycle fixtures. The first build emitted existing
`NU1900` warnings because vulnerability metadata could not be reached from the
restricted environment; compilation and tests completed successfully.

## Boundary and handoff

This branch does not merge, push, deploy, activate, or modify the immutable
observatory evidence. It is a narrow production repair plus its permanent
regression test and this receipt.

Claude remains the integrator. Before integration, refresh `origin/master`,
review the branch tip named in the final handoff, and rerun or accept the
documented merge-tree simulation. Green tests and this receipt are evidence,
not production authorization.

## Final merge-readiness receipt

After the implementation commit, `git fetch --prune origin` confirmed
`origin/master` remained
`a061988945557efd849e996f71c48dc32b80066d`. The implementation commit is
`366f3efc3101a6c0a0a080e24ea4539ed4fdc21c`.

`git merge-tree --write-tree origin/master HEAD` completed without conflicts
and produced synthetic tree
`488c02481249f1da5c6b511e5cfee3398d18bd9e`.

Three independent read-only reviews reported no P0, P1, P2, or P3 findings:

- specification and semantic-equivalence review;
- allocation-methodology and false-green review;
- ownership, collision, and merge-scope review.

The methodology review specifically preserves one boundary: rerun the full
BenchmarkDotNet observatory before claiming broader end-to-end pool or lease
timing totals. This repair claims only the directly proven equality-allocation
result and does not make that broader performance claim.
