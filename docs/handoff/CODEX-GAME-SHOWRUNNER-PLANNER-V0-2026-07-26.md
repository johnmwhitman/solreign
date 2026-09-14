# Codex handoff — execution-empty Showrunner planner v0

Date: 2026-07-26

Status: **HANDOFF-READY AS AN INERT PLANNING KERNEL**

GAME branch: `codex/game-showrunner-planner-v0-20260726`
Implementation commit: `504054805294393ea4f05d45584d02d639d4d830`
Base and validated target: `origin/master` `6368757ee6b980db829ad06c7c21b08a9f61c6f1`
Implementation-commit synthetic merge tree: `e119de9b9ccefde262556f3f12ea238433b55cd5`
Integrator: Claude

## Outcome

This lane adds a pure deterministic planner for a future operator-preview Showrunner. It can select
exactly one opening, one escalation, and one finale from an evidence-shaped allowlist while enforcing
population, map, cooldown, active-rule, and one-headliner limits.

The planner is deliberately execution-empty:

- no `EntitySystem`, command, CVar, component, prototype, or game-rule hook;
- no production beat catalog;
- no `GameTicker`, `StartGameRule`, or `EndGameRule` path;
- no I/O, ECS, clock, random service, or network dependency;
- no activation state and no live-player behavior.

It therefore does **not** complete SR-W-016 and is not a playable Showrunner. The production
execution deck remains empty.

## Safety contract

- Emergency stop dominates every other input, including malformed catalogs.
- Capability state must be `Eligible`, but the planner authenticates neither that state nor its
  digest. A future trusted evidence verifier must supply both.
- Eligible and non-eligible descriptors require a shape-valid lowercase SHA-256 digest.
- Catalog, nested map sets, context sets, and identifier lengths are bounded before combination
  search.
- Descriptor enumeration stops when the 129th item is observed; it cannot materialize an unbounded
  source before enforcing the 128-card limit.
- Caller collections and refusal reasons are copied into read-only snapshots.
- Set-valued inputs are ordinal-deduplicated and sorted before hashing.
- Selection enumerates valid three-role combinations and ranks them with SHA-256 over a
  length-prefixed canonical representation; input order cannot affect the result.
- `PlanFingerprint` is deterministic and unkeyed. It identifies a normalized preview and must never
  authorize execution.

A future runtime confirmation flow needs a separate trusted verifier plus an issuer-bound,
round-bound, expiring, single-use nonce.

## Verification

Fresh branch results:

- focused `ShowrunnerPlannerTests`: **26 passed / 0 failed / 0 skipped**;
- full `Content.Tests`: **2,906 passed / 0 failed / 3 skipped**;
- `git diff --check`: clean;
- synthetic merge against freshly fetched `origin/master`: clean.

The SOLREIGN integration band is already red on the validated target:

- planner branch: **444 passed / 22 failed / 3 skipped**, 469 total;
- clean `origin/master`: **440 passed / 26 failed / 3 skipped**, 469 total;
- the first branch failure passed **1/1** when run alone on clean master.

Both complete runs exposed the same existing order/pool-sensitive failure family, dominated by
`SolreignFx Validate:AnchorEntityUnresolvable` warnings being promoted during teardown. The clean
run also reproduced existing dirty-dispose/HTN behavior. The four-failure numerical difference is
not a claimed improvement; seeds and pool order varied. The supported conclusion is narrower:
this inert planner produced **no measured integration regression**, while the current master band
does not satisfy a green integration gate.

Full commands and TDD/review evidence are in
`docs/receipts/SHOWRUNNER-PLANNER-V0-2026-07-26.md`.

## Independent review

Two read-only reviewers separately attacked implementation correctness and test/safety semantics.
They found and then verified closure of:

- unbounded descriptor materialization;
- mutable returned descriptor collections;
- non-canonical duplicate set entries;
- non-eligible null-digest crash;
- self-asserted evidence ambiguity;
- fingerprint-as-authorization ambiguity;
- mutable refusal reasons.

Final verdict from both reviewers: **PASS; no remaining P0-P2 in scope**.

## Claude integration route

1. Review implementation commit `504054805294393ea4f05d45584d02d639d4d830`.
2. Integrate the entire branch, including this handoff and receipt, only if the inert-kernel scope is
   still desired.
3. Re-run focused and full unit tests on the actual merge result.
4. Treat the integration band as a separate baseline-red repair lane; do not attribute its existing
   FX/pool failures to this planner without new causal evidence.
5. Keep the production deck empty. Do not add a runtime consumer until capability contracts become
   eligible through a trusted verifier and the operator preview/confirmation threat model is
   approved.

No push, merge, deployment, restart, activation, CVar change, live-box access, or production
mutation was performed or authorized.
