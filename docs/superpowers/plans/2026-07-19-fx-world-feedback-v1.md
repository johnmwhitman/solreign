# FX World Feedback Bridge v1 Implementation Plan

> Execute test-first. Keep every production file additive and every switch false by default.

**Goal:** Connect accepted positive damage and successful electrocutions to the existing safe FX
Language v1 pipeline.

**Architecture:** A small server `EntitySystem` subscribes to existing content events and delegates
all rendering/network policy to `ISolreignFxCueRaiser`. A pure shared rules class owns classification
and positive-only damage aggregation so event semantics are independently testable.

## Task 1: Pure rules, red then green

1. Add failing tests for non-positive/non-finite suppression, positive-only aggregation, and the
   exact 20-damage light/heavy boundary.
2. Run only the new test fixture and capture the expected compile/test failure.
3. Implement the minimum shared rules to pass.
4. Re-run the fixture and commit the green unit slice.

## Task 2: Dormant consumer, red then green

1. Add integration/structural tests asserting the new CVar exists and defaults false, the consumer
   is an `EntitySystem`, and it depends only on the typed FX raiser.
2. Verify red.
3. Add the dedicated CVar partial and server consumer.
4. Subscribe on `DamageableComponent, DamageChangedEvent` and globally on the broadcast
   `ElectrocutedEvent`; check the consumer CVar before all work. Record the obsolete-event
   compatibility debt until upstream provides an actual-delta successor.
5. For damage, sum positive actual-delta entries, classify once, skip null deltas/deleted targets,
   and raise one cue with the damaged entity as both anchor and PVS source. Do not use
   pre-application combat events or requested-damage payloads.
6. For electrocution, raise exactly one `body_shock_generic` cue anchored to the success event's
   target. The upstream event is emitted only after common electrocution succeeds.
7. Re-run focused tests and commit the consumer slice.

## Task 3: Integration and confidentiality review

1. Use the existing integration harness to exercise master-off, consumer-off, healing-only,
   unsupported-damage, light, heavy, successful-electrocution, and insulated-failure behavior
   through real systems.
2. Confirm failed/cancelled electrocution cannot raise `ElectrocutedEvent` in upstream semantics.
3. Run the existing raise-path static analysis and two-client confidentiality suites.
4. Independently review event ordering, PVS anchor selection, base-rate cover wording, and per-event
   workload.
5. Fix findings test-first and commit.

## Task 4: Verification and receipt

1. Build `Content.Shared`, `Content.Server`, and `Content.Tests` in the repository's normal mode.
2. Run the new unit/integration fixtures plus the complete existing `_Solreign/FX` test battery.
3. Run formatting/lint/static-analysis commands used by current GAME receipts.
4. Record base SHA, branch tip, exact commands/results, owned paths, rollback switches, known gaps,
   and a Claude-ready cherry-pick order in the receipt.
5. Confirm `git diff origin/master...HEAD` contains only the declared additive boundary.

## Explicit non-goals

- No public CVar activation, deploy, push, merge, map edit, asset generation, or engine patch.
- No pre-application melee cue and no gameplay mutation.
- No expansion of the wire allowlist or secret-role detail vocabulary.
- No changes to the FX core merely to make the consumer easier to test.
