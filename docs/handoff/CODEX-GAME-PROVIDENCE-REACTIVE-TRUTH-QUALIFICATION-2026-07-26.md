# Claude handoff — Providence reactive truth qualification

## Lane

- Worktree:
  `/Users/johnwhitman/AI/solreign-trees/codex-game-providence-reactive-truth-qualification-20260726`
- Branch:
  `codex/game-providence-reactive-truth-qualification-20260726`
- Base:
  `14a3c7a01b90cec8205b12504480bcb32df1e774`
- Implementation:
  `dd2724e90e621b4b03b2e7bf7926b2bae0215dcd`
- Implementation tree:
  `8af781cdfc8a35aa876a1d2bd4fee91d0b50afba`
- Receipt:
  `docs/receipts/CODEX-GAME-PROVIDENCE-REACTIVE-TRUTH-QUALIFICATION-2026-07-26.md`

## Recommended Claude action

Review and cherry-pick the implementation commit onto then-current GAME
master. Do not treat this handoff as merge, deploy, or activation authority.
Compose it with the independently parked strict-test repair
`79fb230c10e695f97daea7055fdc9b34703135df` before rerunning the strict build.

The precomputed local evidence composition is:

- branch `codex/evidence-providence-strict-composition-20260726`
- commit `4548f3c095c43e6202e8240bc96a8bf142068919`
- tree `426fd6f7f1b09964143d420423778e208461c225`
- strict build `1,072 warnings / 0 errors`

That branch is evidence, not a preferred integration base.

## Why this is useful

Providence's real-death path previously had pure rules and synthetic round-end
coverage but no integration proof that genuine threshold damage exercised the
classification, Ledger selection, curated composition, and cooldown path. The
new fixture closes that gap without changing runtime decisions or defaults.

The lane also repairs stale truth comments that contradicted executable state:
the reactive source default is on, persisted memory is keyed by different
round identity rather than numeric chronology, and the voice inventory is 60
files with nine wired and three staged categories.

## Exact evidence

- New lifecycle fixture: **4 / 0 / 0**
- Existing event-reactive fixture: **3 / 0 / 0**
- Full units: **2,934 / 0 / 3**
- Serial SOLREIGN integration: **491 / 0 / 4**
- Strict composition build: **1,072 warnings / 0 errors**
- Independent reviews: code PASS; test PASS; architecture PASS-AS-HELD
- Merge tree against recorded base equals implementation tree

## Scope boundary

Supported: real server ECS death, pre-event disabled silence, Generic versus
same-round versus different-round-identity Memory selection, exact curated
success-path composition, covered private-field exclusion, truthful death
counting, and cooldown suppression at the dispatch-attempt boundary.

Held: client receipt, actual global chat fan-out, audio, hostile-name ECS
wiring, mid-flight CVar-off, Ledger failure/log privacy, real chronology or
cross-process identity, live-player quality, production activation, rollback
drill, performance, package/deploy, launcher/hub, web, Director, and external
integrations.

## Collision note

This branch owns only the listed implementation files and its unique
receipt/handoff. It did not merge, push, deploy, restart, activate, access live
credentials, or touch the PixelLab secret/bakeoff lane.
