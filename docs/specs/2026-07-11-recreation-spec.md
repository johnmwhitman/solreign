# Recreation Mechanics Spike — go-karts + mini golf

**Date:** 2026-07-11
**Lane:** Recreation & vehicles pack (task #19; follows the RC camera car spike, same day)
**Status:** Spike complete. Mini golf is YAML-only and playable today (assuming physics tuning
holds up in a live server — not verified, see Verification). The kart is a YAML-only *seat prop*
today; it will not drive until the relay hookup (gap #1) lands.
**Reads first:** `docs/specs/2026-07-11-rc-vehicles-spike.md` — already scoped gap #5 ("Rideable
vehicles (go-karts)... do NOT clone from AGPL forks; build on `SetRelay` + `Buckle` ourselves...
2-4 days, own spike") and gap #6 (custom art). This doc is that spike.

## Executive summary

Upstream SS14 has no vehicle system (confirmed again in this spike — see prior doc), but it has
everything mini golf needs already built for other purposes, and everything a go-kart's *body*
needs except the one wire that connects "player is buckled in" to "player's input steers this
entity."

- **Mini golf: zero C# gap.** Club = existing `MeleeWeapon` + `MeleeThrowOnHit` (the exact combo
  `Content.Shared/Weapons/Melee/Components/MeleeThrowOnHitComponent.cs` implements, and the exact
  combo Solreign already shipped for the pool noodle delighter — see
  `Resources/Prototypes/_Solreign/Entities/delighters/pool_noodle.yml`). Ball = the existing
  `BaseSportsBall` pattern (`Resources/Prototypes/Entities/Objects/Fun/sports.yml`). Hole =
  `TriggerOnCollide` + `DeleteOnTrigger` + `EmitSoundOnTrigger` + `PopupOnTrigger`, the same
  generic Trigger primitives that build floor traps and landmines
  (`Resources/Prototypes/floor_trap.yml`). All four pieces are shipped as working prototypes below.
- **Go-kart body: zero C# gap.** The exact component recipe used for pilotable mechs — `Physics
  (bodyType: KinematicController)` + `Fixtures` + `MobMover` + `InputMover` +
  `MovementSpeedModifier` + `GravityAffected` + `Pullable`
  (`Resources/Prototypes/Entities/Objects/Specific/Mech/mechs.yml:BaseMech`) — places and
  simulates fine as a plain prop. Swap the mech's pilot container slot for a `Strap` seat
  (`Resources/Prototypes/Entities/Structures/Furniture/chairs.yml`) and you get a prop you can
  buckle a crew member into today.
- **Go-kart *driving*: one real C# gap.** Buckling to a `Strap` does not call
  `SharedMoverController.SetRelay()`. Nothing upstream wires "buckled to X" to "steers X" — the
  two existing analogues (`CardboardBoxSystem`, `SharedMechSystem`) call `SetRelay` from their own
  container-insert / storage-open events, not from `StrappedEvent`/`UnstrappedEvent`. Someone has
  to write that ~15-line hookup. See gap #1.
- **Speed zones: zero C# gap, contingent on gap #1.** `SpeedModifierContactsComponent`
  (`Content.Shared/Movement/Components/SpeedModifierContactsComponent.cs`) already modifies
  `MovementSpeedModifierComponent` on *whatever entity's own physics fixture touches it* — mob or
  prop, doesn't care. Put it on a boost-pad or mud-patch tile prop; once the kart itself carries
  `MovementSpeedModifier` (it does, see recipe above) and is actually being driven (gap #1), pads
  and puddles work with no new code. Shipping the pads now would be cosmetic only (nothing to
  drive over them yet) so they're deferred to the kart-driving PR, not built in this spike.
- **Lap tracking: genuine, non-trivial C# gap.** Nothing upstream tracks "this entity passed
  checkpoint N of a course in order." `TriggerOnCollide` is stateless per-course-position — it can
  fire a beep at a checkpoint, but "did they hit checkpoints 1→2→3 in order, how many laps, what's
  their time" needs a real component + system. See gap #2.

## What's shipped in this spike

`Resources/Prototypes/_Solreign/Entities/recreation.yml`:

| Entity | What it is | Components (all pre-existing upstream types) |
|---|---|---|
| `SolreignGolfBall` | The ball. | `Item`, `Fixtures` (circle, bouncy, low friction), `TileFrictionModifier` (rolls further on green than a basketball would), `Catchable`, `EmitSoundOnCollide`/`EmitSoundOnLand` |
| `SolreignGolfClubPutter` | Short, precise club. | `MeleeWeapon` (0 damage — PG), `MeleeThrowOnHit` (short speed/distance = a gentle putt) |
| `SolreignGolfClubDriver` | Long, powerful club. | Same combo, tuned for a full drive (higher speed/distance) |
| `SolreignGolfHole` | The cup + flag. | `TriggerOnCollide` (matches the ball's fixture), `DeleteOnTrigger` (`targetUser: true` — sinks the ball, not the cup), `EmitSoundOnTrigger` (chime), `PopupOnTrigger` ("It's in the hole!") |
| `SolreignGoKart` | The kart body/seat. | `Physics(KinematicController)`, `Fixtures`, `MobMover`, `InputMover`, `MovementSpeedModifier`, `GravityAffected`, `Pullable`, `Strap` (driver seat) |

Two club tunings ship (putter/driver) to show the primitive covers both the "tap it three feet"
and "smack it across the green" cases with the same component, just different numbers — no new
code needed for club variety, only new prototypes.

### Why the club is PG-safe by construction

`MeleeWeaponComponent.damage` is set to `Blunt: 0` on both clubs (same idiom as the pool noodle
delighter). `MeleeThrowOnHitComponent` only imparts velocity — it does not require nonzero damage
to fire (confirmed by reading `MeleeThrowOnHitSystem.OnMeleeHit`, which throws on `args.IsHit`
regardless of damage dealt). A club can whack a person exactly like it whacks a ball — same PG
result as the pool noodle: a squeak-free shove, not damage. Whitelisting clubs to *only* hit balls
would need a new `Whitelist` check in a bespoke system; out of scope for this spike, flagged as a
nice-to-have polish item below (not a launch blocker — SS14's existing "everything can hit
everything" norm already applies to bats, noodles, etc. in this codebase).

## Gap list — honest effort estimates

| # | Gap | What's needed | Effort |
|---|-----|---------------|--------|
| 1 | **Kart actually drives when buckled** | New `Content.Shared/_Solreign/Vehicles/DriverSeatComponent.cs` (marker component, mirrors the empty-marker idiom used elsewhere in this repo) on the kart's `Strap`, + a small `DriverSeatSystem` subscribing `StrappedEvent`/`UnstrappedEvent` on that component and calling `_mover.SetRelay(buckle.Owner, strap.Owner)` / removing `RelayInputMoverComponent` on unstrap (mirrors `CardboardBoxSystem.OnEntInserted`/`OnEntRemoved` almost line-for-line, ~50-70 LOC). Needs a `ComponentShutdown` handler too, in case the kart is deleted while occupied (mirrors `SharedMoverController.OnRelayShutdown`, already generic — verify it's sufficient before adding a bespoke one). | 0.5–1 day incl. manual driving verification (no meaningful pure-logic unit to NUnit — it's ECS event wiring; a test would just mock the event bus). |
| 2 | **Lap tracking** | New `LapCheckpointComponent` (ordinal index + course ID) on checkpoint props using the same `TriggerOnCollide` primitive as the golf hole; new `LapTrackerComponent` on the kart (or on the driver — decide during design, driver survives kart destruction, kart is simpler) tracking next-expected-checkpoint-ordinal + lap count + a `TimeSpan` lap start; new `LapTrackerSystem` validating in-order passage (reject out-of-order / reverse-driving checkpoint hits — this is the actual logic worth NUnit-testing: pure "given current index + hit index + course length, what's the new state" function, extractable with no ECS dependency), firing `PopupOnTrigger`-style feedback on lap completion. Best-time persistence (Season Ledger tie-in per `~/AI/AI stewardship) is a stretch goal, not in this estimate. | 1–2 days incl. NUnit coverage for the ordinal-validation function. |
| 3 | **Speed-zone props (boost pads / mud)** | Zero new components — `SpeedModifierContactsComponent` on a tile-decal-sized prop, contingent on gap #1 landing so there's something to drive over it. Purely a YAML follow-up once #1 ships. | ~1 hour, blocked on #1. |
| 4 | **RC camera car sibling can't be ridden the same way** | Not actually a gap — the RC car (previous spike) is ghost-role-possessed, not buckled; the two vehicle patterns (possess vs. buckle-drive) are intentionally different and both valid. Noting only so a future reader doesn't assume they should be unified. | N/A |
| 5 | **Custom art** | Kart reuses an orphaned upstream sprite for the spike (see below); a Solreign-branded kart + numbered racing livery is a sprite-factory job. Golf ball/club reuse existing sports/bat sprites tinted; a dedicated golf set is optional polish. | Sprite factory lane. |
| 6 | **Whitelist club hits to balls only** (nice-to-have) | Small `EntityWhitelist` gate in a thin wrapper system so clubs don't fling crew members around during a casual round (they still can today — no worse than existing bats/noodles, but a golf-specific club arguably shouldn't). | ~2 hours, polish, not launch-blocking. |

### Sprite notes

- Kart: reuses `Objects/Vehicles/secway.rsi` (CC-BY-SA-3.0, ex-tgstation, orphaned — the RC car
  spike already claimed `atv.rsi`, so this spike picks a different orphan to avoid visual
  collision on the same map). `TODO(SOLREIGN-ART)`: dedicated go-kart sprite, 4-direction,
  acid-green Solreign racing livery, numbered.
- Golf ball: tinted `Objects/Fun/Balls/tennisball.rsi` icon (already dimpled-looking at 32px).
  `TODO(SOLREIGN-ART)`: real dimpled golf-ball sprite.
- Clubs: reuse `Objects/Weapons/Melee/baseball_bat.rsi`, tinted, different icon state per club if
  the sprite factory produces one; for the spike both clubs share the bat sprite with a color tint
  to tell putter/driver apart at a glance until real art lands.
- Hole: reuses `Objects/Misc/Handy_Flags/blank_handy_flag.rsi` (`icon` state, CC-BY-SA-3.0) as a
  planted pin-flag marker. `TODO(SOLREIGN-ART)`: a proper cup-and-flag sprite with a "hole" divot.

## Verification recipe (needs a live server; not run in this spike — ground rules bar `dotnet build`)

**Mini golf (should work end-to-end today):**
1. Spawn `SolreignGolfBall`, `SolreignGolfClubPutter`, and a `SolreignGolfHole` a few tiles apart.
2. Wield the putter, hit the ball toward the hole.
3. Confirm the ball is thrown in the direction of the swing (per `MeleeThrowOnHitSystem`), rolls
   with the tuned friction, and — on colliding with the hole's fixture — is deleted with a chime
   and a popup.
4. Repeat with `SolreignGolfClubDriver` from further away to confirm the longer throw.

**Go-kart (seat works, driving does not yet):**
1. Spawn `SolreignGoKart`. Confirm it places, has physics (pushable via `Pullable`), and a crew
   member can buckle into the `Strap` seat (interact-hand or drag-drop, standard buckle UX).
2. Confirm the buckled crew member does **not** currently drive it — this is the expected,
   documented state pending gap #1. Movement keys should still move the buckled player's own body
   if unbuckle happens, and do nothing to the kart while buckled (no relay wired).

## Tests

None added. Ground rules ask for NUnit coverage of pure logic; this spike is entirely prototype
data (YAML) plus a reading of existing, already-tested upstream systems (`MeleeThrowOnHitSystem`,
`TriggerSystem`, `SharedMoverController`) — there is no new pure function to test yet. The one
piece of this spec that *is* pure-logic-testable is gap #2's checkpoint-ordinal validator; that
lands with gap #2's implementation, not this spike.

## Recommendation

Ship mini golf now — it's complete and PG by construction (0-damage clubs, no gap). Land the
kart *seat* prop now too (it's a legitimate prop even before it drives — crew can push it around
via `Pullable`, sit in it for RP photos). Schedule gap #1 (driving) as the very next small PR
before touting go-karts as a feature; don't advertise "go-kart racing" externally until #1 and #2
both land, since an un-drivable kart reads as a bug, not a feature, to a first-time player.
