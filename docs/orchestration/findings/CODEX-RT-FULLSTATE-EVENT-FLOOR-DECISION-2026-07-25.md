# RobustToolbox full-state / entity-event ordering decision

Date: 2026-07-25

Status: **REJECT blanket tick floor; HOLD protocol work**

Repository baseline:

- GAME `1021c979c8303da0e219ada0c4b8231fad47075b`
- RobustToolbox `960edb32c4dd417496e4667177625d8c3cb14f7e`

This is a decision and evidence packet. It authorizes no engine patch, merge, push, package,
launcher change, deploy, activation, or live mutation.

## Trigger

After a client applies a full snapshot, `PartialStateReset` deletes entities absent from that
snapshot. A deferred `MsgEntity` may then dispatch and carry a SOLREIGN cosmetic FX cue whose anchor
was deleted. The FX receiver correctly rejects the unresolvable anchor, but its aggregated warning
fails integration teardown.

The initial proposed engine repair was:

> After applying full snapshot T, prune queued entity messages at or before T, reject later arrivals
> at or before T, preserve messages after T, and clear the floor on disconnect.

That proposal is rejected.

## Verified execution order

- Network packets are processed before the client tick applies game state.
- `MsgState` enters `GameStateProcessor`; `MsgEntity` enters the client entity-event priority queue.
- The full-state branch sets `LastProcessedTick`, then `PartialStateReset` deletes omitted entities.
- Deferred entity events drain later in the client entity manager tick.

This makes the observed missing-anchor sequence possible.

## Why a global floor is unsafe

1. `MsgState` is unreliable or reliable-unordered. `MsgEntity` uses a separate reliable-ordered
   stream. There is no cross-stream causal barrier.
2. `GameState` carries no entity-event watermark, acknowledgement, request generation, or barrier.
3. A snapshot replaces replicated entity state, not arbitrary transient event semantics. Entity
   events include BUI/RPC payloads that cannot be reconstructed from the snapshot.
4. Equality is unsafe. Same-tick work can emit a valid event after snapshot construction; it still
   carries source tick T.
5. Replay checkpoint reset also calls `PartialStateReset`, while replay event delivery follows a
   different path. Attaching a live network floor there would create replay divergence.
6. Raw unsigned `GameTick` comparison wraps after the engine's documented multi-year horizon.

Independent protocol red-team rejected the floor. The separate code-archaeology lane re-examined
and retracted its original barrier assumption after checking the wire types.

## Recommended immediate SOLREIGN repair

Keep all generic entity events intact.

Treat `SolreignFxCueV1` as explicitly lossy cosmetic data:

- A missing anchor after network resynchronization is an expected quiet drop or bounded metric, not
  an integration-failing warning.
- Preserve validation and warning behavior for malformed, unauthorized, or unsafe payloads.
- Prefer coordinate anchors for broadcast cosmetic effects when an entity lifetime is not required.
- If engine support is desired, use an explicit opt-in marker for resync-supersedable cosmetic
  events; never infer discardability from source tick alone.

The previously verified disconnect/full-flush queue-clear patch at nested RobustToolbox commit
`bae90c907` remains logically separate and useful: it clears queued future messages and the local
ordering counter when the connection epoch ends. It does not justify a snapshot floor.

## Protocol-grade future option

A lossless generic solution requires an explicit event-stream fence:

1. Add a generation ID to every full-state transaction, including bootstrap and retries.
2. Construct the snapshot carrying that generation.
3. Enqueue a matching barrier on the same reliable-ordered event stream after all causally prior
   events.
4. Client waits for both snapshot and barrier.
5. Dispatch all pre-barrier events against the old world.
6. Apply the snapshot atomically.
7. Retain every post-barrier event, including same-tick events.
8. Disconnect/full flush clears queue, barrier, and generation state.

This is a protocol project, not a one-method fix. It requires versioning, concurrency ordering,
replay parity, launcher/client-package compatibility, and performance evidence.

## Required protocol regression matrix

- State arrives before barrier.
- Barrier arrives before state.
- Pre-barrier events dispatch before destructive reset.
- Post-barrier same-tick events survive.
- BUI/RPC events are never silently discarded.
- Stale request generations cannot unlock a newer resync.
- Disconnect clears queue and generation.
- Replay checkpoints do not mutate the live network barrier.
- Same-tick event ordering is preserved.
- Missing-anchor cosmetic cues drop without exception or integration-failing warning.

## Decision

Do not implement or cite a `SourceTick <= fullStateTick` discard rule. Route the immediate defect to
a narrow SOLREIGN FX consumer lane. Keep the protocol-grade barrier in Engine Lab research until it
has an approved wire-compatibility design and the full regression matrix.
