# Codex GAME Providence reactive truth qualification receipt — 2026-07-26

## Verdict

`PASS-AS-HELD / CLAUDE INTEGRATION HOLD`

This lane qualifies a narrow server-side boundary: genuine ECS deaths reach
Providence's reactive handler, select the expected line class, compose only the
covered curated fields, and respect the shared cooldown before a dispatch
attempt. It does not authorize merge, push, package, deploy, restart,
activation, launcher/hub work, credentials, or production mutation. Claude
remains the sole integrator.

## Exact identity

- Branch:
  `codex/game-providence-reactive-truth-qualification-20260726`
- Worktree:
  `/Users/johnwhitman/AI/solreign-trees/codex-game-providence-reactive-truth-qualification-20260726`
- Canonical/tested base and merge base:
  `14a3c7a01b90cec8205b12504480bcb32df1e774`
- Implementation commit:
  `dd2724e90e621b4b03b2e7bf7926b2bae0215dcd`
- Implementation tree:
  `8af781cdfc8a35aa876a1d2bd4fee91d0b50afba`
- Conflict-free `git merge-tree --write-tree origin/master <implementation>`:
  `8af781cdfc8a35aa876a1d2bd4fee91d0b50afba`
- RobustToolbox gitlink:
  `960edb32c4dd417496e4667177625d8c3cb14f7e`

The strict-build composition was kept separate:

- Evidence branch:
  `codex/evidence-providence-strict-composition-20260726`
- Composition commit:
  `4548f3c095c43e6202e8240bc96a8bf142068919`
- Composition tree:
  `426fd6f7f1b09964143d420423778e208461c225`
- Inputs:
  implementation `dd2724e90e621b4b03b2e7bf7926b2bae0215dcd`
  plus parked strict-test repair
  `79fb230c10e695f97daea7055fdc9b34703135df`

The implementation branch deliberately does not absorb the sibling strict-test
repair. Claude can review and land the two independently.

## What changed

1. Added a four-case connected, fresh, destructive integration fixture that
   drives real threshold damage and therefore the production
   `MobStateChangedEvent` path.
2. Added observation-only test state for the selected death-line class and
   terminal async-continuation count. Production control flow never reads this
   state.
3. Reconciled comments with executable truth:
   - the reactive CVar source default is `true` and remains a server-only kill
     switch;
   - Ledger memory means a different round identity, not numerically proven
     chronology;
   - Providence has 60 OGG files across 12 collections;
   - nine categories have production callers and three remain staged;
   - attribution points to the durable in-repo `ATTRIBUTION.txt`.
4. No CVar default, runtime selection rule, Ledger query, prototype value,
   localization string, audio asset, or production delivery behavior changed.

## Supported qualification claims

- A real crew death reaches the server-side reactive handler.
- CVar-off before the death leaves reactive counting and attempt state silent.
- A fresh account selects one curated Generic template.
- A same-round Ledger row does not qualify as Memory.
- A persisted row with a different round identity selects one curated Memory
  template.
- A rapid second real death is counted, and the cooldown suppresses a second
  dispatch attempt after both async continuations terminate.
- In the covered success paths, composed text excludes the raw account ID,
  historical character name, private epitaph ID, and raw Ledger cause code.
- Source configuration is default-on with a server-only runtime kill switch.

## Verification

All .NET commands were serialized with `-m:1`, `-nodeReuse:false`, and
`-p:UseSharedCompilation=false`.

| Gate | Result |
| --- | --- |
| New real-death lifecycle fixture | **4 passed / 0 failed / 0 skipped** |
| Existing Providence event-reactive fixture | **3 passed / 0 failed / 0 skipped** |
| Full `Content.Tests` | **2,934 passed / 0 failed / 3 skipped / 2,937 total** |
| Full serial `FullyQualifiedName~Solreign` integration band | **491 passed / 0 failed / 4 skipped / 495 total** in 13m04s |
| Strict integration-project build on the separate composition | **1,072 warnings / 0 errors** in 55.69s |
| Providence OGG inventory | **60** paths in **12** collections |
| `git diff --check` | clean |
| Independent code review | PASS, no P0-P3 |
| Independent architecture review | PASS-AS-HELD |
| Independent test review | PASS in the stated dispatch-attempt scope |

The three unit skips are the two existing Windows-only ACL cases and
`TestAlertManager`. The four integration skips are the existing low-pop
disabled, inspection disabled, authenticated lifecycle, and persistent-block
gated cases.

The strict build's warning count is recorded rather than described as clean.
It is repository-wide diagnostic output; the relevant result is zero errors
with analyzers and the repository warning policy enabled.

## Test-first and mutation evidence

- Before the line-class observation seam existed, the new fixture failed to
  compile with three `CS1061` errors.
- Mutating the same-round eligibility rule to accept any non-null record made
  the same-round case fail: **0 passed / 1 failed**. The executable rule was
  restored unchanged.
- Removing the death cooldown check made the completed-two-death case fail:
  **0 passed / 1 failed**, observing two attempts instead of one. The gate was
  restored before the final batteries.
- A controlled mid-flight lookup experiment proved the post-await CVar check,
  but required a mutable Ledger override in production code. Both the override
  and that test were discarded after review. Mid-flight behavior is therefore
  held, not claimed by the committed lane.

## Held claims

This receipt does not prove:

- the actual `ChatSystem.DispatchGlobalAnnouncement` call, client receipt,
  global or multi-client audience fan-out, UI rendering, generic cue playback,
  or Providence voice audio;
- hostile-name sanitizer wiring through a real humanoid identity (the pure
  sanitizer remains unit-tested, and the production call is inspection
  evidence only);
- CVar-off after a Ledger lookup is already in flight;
- Ledger failure-path privacy or log privacy; existing error logs include raw
  GUIDs;
- attacker-origin confidentiality;
- a real previous-round transition, numeric chronology, cross-process
  round-ID uniqueness, backup/restore behavior, or scheduler-race ordering;
- accessibility, live-player readability, frequency, annoyance, anti-fatigue,
  operator rollback drills, or production activation;
- performance, the full upstream integration corpus, packaging, deploy,
  launcher/hub compatibility, Director, Showrunner, Chronicle, website, or
  external egress.

## Integration note

Claude should inspect and integrate the implementation commit, then compose it
with the strict-test repair on then-current canonical master. If master moves,
recompute the merge tree and rerun the strict build plus affected test bands.
Passing local evidence is not merge or production authority.
