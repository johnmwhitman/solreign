# FX World Feedback Bridge v1 receipt

**Date:** 2026-07-19
**Repository:** GAME
**Branch:** `feat/codex-visual-presentation-next`
**Base:** `5d42a9c53cee12646df44e8367dc61c1799cec52` (`origin/master`)
**Corrected implementation tip:** `403512014f16aaceba53bb90ef435c2393dca2e4`
**State:** committed, dormant, not merged, not pushed, not deployed, not activated

## Outcome

The existing FX Language v1 pipeline now has an additive, server-side consumer for ordinary world
feedback:

- positive actual damage deltas below 20 raise allowlisted `impact_light`;
- positive actual damage deltas at or above 20 raise allowlisted `impact_heavy`;
- successful upstream electrocution raises allowlisted `body_shock_generic`;
- healing-only changes, unsupported damage that changes no target state, failed insulated shocks,
  deleted targets, and disabled gates raise nothing through this bridge.

The adapter changes presentation only. It adds no gameplay mutation, prototype, asset, network
message, engine patch, launcher dependency, map edit, or public configuration.

## Review corrections

The first draft subscribed to pre-application `MeleeHitEvent`. Independent review found this could
show a false impact for a blocked wide attack. A second draft used `DamageDealtEvent`; review proved
that event can retain an unsupported requested damage type even when the damage model changes no
state. The final implementation consumes `DamageChangedEvent.DamageDelta`, the current upstream
event containing the actual applied delta.

`DamageChangedEvent` is obsolete upstream. Its warning suppression is deliberately narrow and
restored immediately after the handler. Replacing it with the future actual-delta successor is
tracked compatibility debt; using the requested-damage event today would be less correct.

RobustToolbox also forbids duplicate exact component/event subscriptions. The abandoned
`InjurableComponent, DamageDealtEvent` draft exposed that rule in integration startup; no engine
change was made.

## Safety and rollback

Two independent fail-closed switches are required:

- `solreign.fx.world_feedback_v1 = false` by default, `SERVERONLY`;
- existing `solreign.fx.cue_v1 = false` on the branch base.

Either switch disables delivery. All cues pass through `ISolreignFxCueRaiser`, retaining the
existing sealed allowlist, PVS filtering, egress/intake budgets, pooling, accessibility profiles,
and no-flash policy. Target entity is both the cue anchor and PVS source. No direct network raise
was added.

## Verification

Final commands were run from the worktree with Release configuration, single-node MSBuild, and
node reuse disabled:

```text
dotnet test Content.Tests/Content.Tests.csproj -c Release --no-build --no-restore \
  --filter FullyQualifiedName~Content.Tests._Solreign.FX --verbosity minimal -m:1 /nodeReuse:false
PASS: 316 passed, 0 failed, 0 skipped

dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c Release --no-restore \
  --filter FullyQualifiedName~_Solreign.FX --verbosity minimal -m:1 /nodeReuse:false
PASS: 9 passed, 0 failed, 0 skipped

git diff --check origin/master...HEAD
PASS: no whitespace errors
```

The nine integration tests include the new three-scenario real server/client fixture plus the
existing FX prototype, PVS, and two-client confidentiality coverage. The new fixture uses fresh
pairs to prevent pooled state from weakening evidence.

Independent review found the false-impact blocker, verified its correction, and gave the code a GO
subject to this committed receipt and green suites. A read-only merge audit found no exact-path
overlap and no conflict signal against current `origin/master`, local master, wave-6 orchestration,
or the visible integration branches.

## Canary checklist and known risk

Do not enable either CVar as part of merge. In a private canary, verify:

1. physical impacts read clearly at both sides of the 20-point threshold;
2. rapid damage respects existing FX budgets and does not create camera/audio fatigue;
3. toxin, radiation, suffocation, and other damage-over-time cues are evaluated explicitly;
4. if internal damage feels too physical or noisy, constrain the consumer by damage type before
   wider activation rather than weakening global FX safety controls;
5. reduced-motion, low-VFX, cosmetic-minimal, and no-flash profiles remain comfortable;
6. disable `solreign.fx.world_feedback_v1` immediately on unexpected presentation or load.

The all-positive-damage behavior matches the v1 spec but remains a product/canary question, not a
claim of proven live-player delight.

## Owned paths

- `Content.Server/_Solreign/FX/Consumers/SolreignWorldFeedbackSystem.cs`
- `Content.Shared/CCVar/CCVars.SolreignFxConsumers.cs`
- `Content.Shared/_Solreign/FX/Consumers/SolreignWorldFeedbackRules.cs`
- `Content.Tests/_Solreign/FX/Consumers/SolreignWorldFeedbackRulesTests.cs`
- `Content.IntegrationTests/Tests/_Solreign/FX/Consumers/SolreignWorldFeedbackIntegrationTest.cs`
- `docs/specs/FX-WORLD-FEEDBACK-V1-SPEC-2026-07-19.md`
- `docs/superpowers/plans/2026-07-19-fx-world-feedback-v1.md`
- `docs/receipts/fx-consumers/FX-WORLD-FEEDBACK-V1-2026-07-19.md`

## Claude handoff

Cherry-pick in order:

1. `211a033008ff6bc8518ce35fa229656a6d4f3806`
2. `39068111ef450002ea7cef300530d9acab38e908`
3. `039966bf196fd3c04bca37e335d8a60370fa6b3b`
4. `403512014f16aaceba53bb90ef435c2393dca2e4`
5. the receipt commit containing this file

The first three commits preserve the red-first history; commit 4 is required and supersedes their
pre-application event design. Claude should re-run the two commands above after integration. No
merge, push, deploy, or CVar activation was performed here.
