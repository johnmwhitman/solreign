# FX World Feedback Bridge v1

Status: implementation-ready, dormant by default
Owner lane: `feat/codex-visual-presentation-next`
Base: GAME `origin/master` at `5d42a9c53cee12646df44e8367dc61c1799cec52`

## Outcome

Make SOLREIGN's existing FX Language v1 visible during ordinary play without changing combat,
electrocution, networking, or RobustToolbox behavior. The bridge translates two authoritative
content events into already allowlisted, pooled cues:

| Source event | Cue | Anchor | Meaning |
| --- | --- | --- | --- |
| `DamageChangedEvent`, positive actual delta | `impact_light` or `impact_heavy` | damaged target | The damage model changed the target's state by a positive amount. |
| `ElectrocutedEvent` | `body_shock_generic` | electrocuted target | Electrocution completed successfully and raised the upstream success event. |

Positive entries in `DamageChangedEvent.DamageDelta` are summed without allowing healing entries to
cancel them. Applied positive damage below 20 selects `impact_light`; 20 or more selects
`impact_heavy`. Each event belongs to one damaged target and produces at most one cue. Null-delta,
deleted, or terminating targets are skipped.

## Safety and compatibility contract

- New server CVar: `solreign.fx.world_feedback_v1`, default `false`, server-owned.
- The existing `solreign.fx.cue_v1` master gate remains authoritative inside the sole cue raiser.
- No new network message, prototype ID, asset, engine patch, launcher package, gameplay mutation, or
  public activation is introduced.
- All cues use `ISolreignFxCueRaiser`; direct `RaiseNetworkEvent` calls are forbidden.
- Existing allowlist validation, PVS filtering, per-tick/category egress budgets, client intake cap,
  pooling, reduced-motion profile, low-VFX profile, cosmetic-minimal profile, and no-flash policy
  remain authoritative.
- `body_shock_generic` is deliberately ordinary public vocabulary. This adds benign cover traffic
  but is not represented as a complete secret-role confidentiality proof.
- Healing-only or non-positive damage, a disabled consumer, or failed / cancelled / insulated
  electrocution produces no cue through this bridge.
- Rollback is immediate: set either consumer or master CVar to `false`; code removal is additive-only.

## Deterministic rules

Pure shared rules provide:

1. `AppliedPositiveDamage(damage)` summing only positive post-modifier entries.
2. `ClassifyAppliedDamage(appliedPositiveDamage)` returning none/light/heavy.
3. Constants for the three existing effect IDs.

The first implementation used `MeleeHitEvent`, but independent review found upstream raises that
event before blockers and damage application. A second draft used `DamageDealtEvent`, but review
proved its payload can still include unsupported requested damage which changes no target state.
The final bridge therefore subscribes through `DamageableComponent` to `DamageChangedEvent` and
uses its actual `DamageDelta`, preventing false impacts for both blocked attacks and unsupported
damage types. `DamageChangedEvent` is currently obsolete upstream; the local pragma and this spec
make replacement with its future damage-model-specific successor an explicit compatibility task.

## File boundary

Only these additive paths are owned:

- `Content.Shared/_Solreign/FX/Consumers/SolreignWorldFeedbackRules.cs`
- `Content.Server/_Solreign/FX/Consumers/SolreignWorldFeedbackSystem.cs`
- `Content.Shared/CCVar/CCVars.SolreignFxConsumers.cs`
- `Content.Tests/_Solreign/FX/Consumers/SolreignWorldFeedbackRulesTests.cs`
- `Content.IntegrationTests/Tests/_Solreign/FX/Consumers/SolreignWorldFeedbackIntegrationTest.cs`
- `docs/specs/FX-WORLD-FEEDBACK-V1-SPEC-2026-07-19.md`
- `docs/superpowers/plans/2026-07-19-fx-world-feedback-v1.md`
- `docs/receipts/fx-consumers/FX-WORLD-FEEDBACK-V1-2026-07-19.md`

No Season Ledger, Oracle, audio, Compliance Terminal, Memorial, global stylesheet/palette, map,
roster, server configuration, or RobustToolbox file is in scope.

## Acceptance evidence

- Red-first unit tests prove applied-delta classification, non-finite suppression, and positive-only
  aggregation; integration proves unsupported damage produces no cue.
- Integration proof establishes the CVar defaults false and the system resolves with the existing
  cue raiser. Event-level integration is added where the harness can observe the network cue without
  weakening production visibility or adding test-only production hooks.
- Targeted C# builds, unit/integration tests, linter/static analysis, and the existing SOLREIGN FX
  suite pass.
- A committed receipt records exact commands, results, diff, base, rollback, and remaining canary
  work. No live-effect or activation claim is made.
