# A9 — wingmate beacon into the arrival path: RULED YES, but it is not a one-line move

**Date:** 2026-08-02 · **Lane:** SOLREIGN game/release
**Request:** `solreign-ops` `docs/ops/JOURNAL.md`, "2026-07-29 INBOUND from the acquisition lane"
(`9ac74d1`). The acquisition lane asked the game lane to rule by **2026-08-02** or close A9 as
won't-fix.

## Ruling

**The problem is real and worth fixing — A9 should NOT be closed won't-fix.**
**The proposed change is not the change that fixes it.** Nothing shipped in this release; what
shipped instead is this spec, which turns a guess into a mechanical job.

## The problem is confirmed, by my own measurement

I re-measured the beacon→latejoin distances from a clean worktree with my own scanner and
**reproduced the INBOUND table exactly** — terminus 31, leviathan 35, perihelion 35, verdant 35,
oasis 59, with nocturne 6 and meridian 7 already acceptable. Two independent instruments agreeing
is why the premise is trustworthy. A new arrival genuinely never walks past onboarding.

## Why "one `pos:` value each" is wrong — two blocking facts the INBOUND did not have

**1. The beacon is a pinned ANCHOR, not a free-standing entity.**
`Tools/_Solreign/MapPatch/manifests/community-fixtures-v1.json` pins `SolreignWingmateBeacon` as
the anchor for a `SolreignLibraryAnnex` + `SolreignNoticeboard` pair on **all seven maps**, with
`max_manhattan_tiles: 4`. `SolreignCommunityFixtureMapPlacementIntegrationTest` enforces it.
Moving only the beacon orphans the pair and fails that test; `map_patch.py --check` independently
refuses with `anchor … drifted`. So the real job is **15 placements across 5 maps** (beacon +
library + noticeboard each), driven through the manifest — not five one-line edits.

**2. The beacon is an interactive wallmount, and the two constraints on it pull opposite ways.**
* `SolreignWingmateBeaconMapPlacementIntegrationTest` needs the tile **breathable** —
  `IsTileMixtureProbablySafe` and ≥16 kPa O₂. A sealed wall tile has no air.
* `InteractiveWallmountCollisionTests.NoInteractiveWallmountFloatsWithoutAWall` needs a
  wall/window/grille **on the tile or one of its four neighbours**, or the mount renders hanging
  in open air.

So the target must be an open floor tile *beside* a wall — the "46 of 4130" BESIDE-a-wall case —
never the wall tile itself, and never an open-floor tile chosen for being empty.

## I got this wrong first, and the repo's own ratchet caught it

My first pass picked tiles that were empty and ringed by occupied tiles. That heuristic is wrong
at a hull edge, where the ring is satisfied from the inside face of the wall: two beacons landed
**in space** (`Expected: property Count equal to 1 / But was: 0`). My second pass fixed the space
problem but produced **four NEW floating wallmounts**.

`InteractiveWallmountCollisionTests` already says this, and it was right:

> *"correcting a wallmount's position needs someone looking at a real client — a wallmount moved
> to a tile with no wall behind it floats… Guessing seven coordinates blind would trade a visible
> bug for an invisible one."*

That is exactly what my first two attempts did. **This is the reason nothing shipped:** a green
ratchet proves no *new* floater, it does not prove the beacon looks right to a player, and
"never ship visuals unseen" is lane law. Fifteen relocated wallmounts is not a change to make blind.

## What IS proved, and ready to reuse

A third pass — breathable floor, wall in a 4-neighbour, no wallmount stacking — went green:

| Map | Beacon target | Nearest latejoin | Chebyshev before → after |
|---|---|---|---|
| terminus | `-47.5,-34.5` | `-46.5,-33.5` | 31 → 1 |
| leviathan | `-34.5,-8.5` | `-34.5,-8.5` | 35 → 0 |
| perihelion | `-25.5,-12.5` | `-25.5,-12.5` | 35 → 0 |
| verdant | `-25.5,-12.5` | `-25.5,-12.5` | 35 → 0 |
| oasis | `-35.5,-10.5` | `-36.5,-9.5` | 59 → 1 |

**`SolreignWingmateBeaconMapPlacementIntegrationTest`: 7/7 passed** on those coordinates, and the
collision/float ratchets reported **zero new** entries of either kind.

**Bonus that comes free with the move:** the ratchets reported it would **dissolve 5 of the 7
`SolreignCorporateProjectConsole + SolreignWingmateBeacon` collisions** (the live bug idiot4733
reported on 2026-07-30) and **2 of the 38 floaters** — the consoles never move, only the beacon
leaves the tile. That makes this fix worth more than onboarding alone.

## The remaining job, specified

1. For each of the 5 maps, pick library + noticeboard tiles within Manhattan 4 of the beacon
   target above, using the same three-part predicate (breathable floor, wall in a 4-neighbour, no
   interactive wallmount already on the tile).
2. Update `community-fixtures-v1.json`: anchor `pos`, both placement `pos`, and each
   `expected_occupants` (proto + uid) — `map_patch.py --check` is fail-closed and will name any
   mismatch.
3. `map_patch.py --apply`, then `Tools/solreign_gate.sh`, the beacon placement test, and
   `SolreignCommunityFixtureMapPlacementIntegrationTest`.
4. Delete the 5 dissolved collisions and 2 dissolved floaters from `KnownCollisions` /
   `KnownFloating`, and **re-measure the counts in the surrounding doc comments** ("9 tiles out of
   801", "true population >= 38") — a predicate change moves those figures too.
5. **Look at all five in a real client before it ships.** That step is not optional and is the one
   I could not do.

## What this does NOT fix, even done perfectly

The `FirstShiftSystem` spawn-hook design defect (every trigger is beacon UI; there is no
cargo/arrivals placement hook) and the solo-lobby trap — a lone arrival landing at
`run_level 0, map: null` — are both still open. Moving the beacon does not make a one-player round
start. Do not read this as closing either.
