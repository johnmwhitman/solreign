# SOLREIGN — Landing receipt: H3 compose (2026-07-27)

Composes four already-pushed codex lanes onto `master` `fa12e6ee02` (== `origin/master` at compose
time). Zero merge conflicts. States what was VERIFIED versus what is BELIEVED.

## Lanes composed

| Lane | Contributes |
|---|---|
| `codex/game-land-watchdog-h3-20260727` | Env-tunable integration watchdog; Tier 2 expedition roles wired across all 7 station maps |
| `codex/game-operator-preflight-refresh-20260727` | Repeatable kart heats (dormant); `solreignshowpreflight` operator command |
| `codex/game-species-viability-refresh-20260727` | `SolreignPlayableSpeciesBehavioralViabilityTest` (SR-BUG-004's behavioural gate) |
| `codex/game-upstream-drift-admission-20260727` | `Tools/_Solreign/UpstreamDrift/` audit script + pytest suite |

Net: **54 files, +5796 / −31**, of which **39 are code**.

### Superseded lanes — covered, not dropped on resemblance
Four sibling lanes carry content already contained here. The DROP test was run against MASTER and
against the composed tree, not against a sibling:

- `codex/game-horizon3-recovery-ship-20260727` and `codex/game-land-watchdog-20260727` are git
  ancestors of `codex/game-land-watchdog-h3-20260727` (`git merge-base --is-ancestor`, verified).
- `codex/game-land-species-viability-20260727` and `codex/game-species-behavioral-viability-20260726`
  carry `SolreignPlayableSpeciesBehavioralViabilityTest.cs` at blob `ce6d3da75450`. The composed tree
  contains that **exact blob** via the species-viability-refresh lane (verified by
  `git rev-parse <ref>:<path>` on all three refs).

## VERIFIED — gates run on the composed tree

| Gate | Result |
|---|---|
| `Tools/solreign_gate.sh` — unit `_Solreign` | **2598 passed / 0 failed** (master baseline: 2515) |
| `Tools/solreign_gate.sh` — NetworkedComponentParityTest | **1 passed / 0 failed** |
| `Tools/solreign_gate.sh` — YAMLLinter | **exit 0** |
| `dotnet test Content.IntegrationTests --filter _Solreign` | **461 passed / 0 failed / 4 skipped, exit 0** |

Integration duration **13m03s (790s)** against the 20-minute soft watchdog — **~7 minutes of
headroom**, and the log contains **zero** occurrences of the collapse signature
("Pool manager has not been initialized") and zero watchdog trips. Master's last recorded run was
431 passed in 18m48s; this tree runs **+30 tests in less wall-clock**, consistent with the earlier
figure having been taken on a busier machine.

Machine was verified quiet before gating: load 1.54, zero `dotnet` processes, no process above 20%
CPU. Fleet lanes dispatched during this session were **explicitly build-forbidden** in their briefs
so they could not starve the battery (this is the failure that produced a false 142-failure red on
2026-07-26).

## NOT verified by this receipt
- Release-config packaging (RA0049 is a warning in Debug, an error in Release) and the client
  sandbox scan. Both live in OPS `deploy/build_verify.py` and must run before any deploy.
- Runtime behaviour on a live server. Nothing here has been observed in a client.
- Deploy state. Production is unchanged.

## Adversarial static review (grk lane, build-forbidden)
**No P0.** Verified by hand against source before acceptance:
- Claim "`[Dependency]` classes are `partial`" — **confirmed**: `SolreignLapTrackerSystem` and
  `SolreignShowPreflightCommand` are both `partial`. RA0049 does not apply.
- Claim "no networked component surface added" — consistent with the parity gate passing.

Three P2s accepted as real and **queued, not blocking**:
1. `SolreignLapTrackerSystem.cs:65` — the kill-switch normalization path sets `tracker.Finished = true`
   directly and then clears heat state, bypassing `FinishHeat` and its announcement. Reproduced by
   reading the source. Requires the dormant feature ON *and* an admin disabling it mid-heat, so a
   driver's last circuit would silently lock with no checkered-flag broadcast.
2. `SolreignTier2JobReachabilityIntegrationTest.cs` — seeds the job's own playtime tracker rather
   than a feeder Cargo role, so the test could stay green if department aggregation regressed for
   the path players actually earn time on.
3. `SolreignShowPreflightCommandIntegrationTest.cs` — samples ticker state and executes the command
   outside `WaitAssertion`, a plausible future flake source. This run was green.

## Feature state
`solreign.kart_repeatable_heats_enabled` defaults to **false** — the kart feature ships dormant.
The Tier 2 roles are *not* switch-gated: they are map-placed with a reachability integration test,
so unlike the 2026-07-25 activation pass they are actually reachable by players.
