# RC Vehicles Spike — what upstream gives us, what it doesn't

**Date:** 2026-07-11
**Lane:** Recreation & vehicles pack (RC camera cars, go-karts, mini golf)
**Status:** Spike complete. One YAML-only prototype shipped; controller-in-hand driving specced for Wave 2.

## Executive summary

Upstream SS14 has **no vehicle system** — rideable/drivable vehicles were removed years ago
and only orphaned sprites remain. It also has **no `RemoteControl` anything** (zero grep hits).
But it has three primitives that compose into an RC camera car **today, with zero C#**:

1. **Ghost-role driven mobs** (every bot in `silicon.yml`) — a player can *be* the car.
2. **The input-relay pair** `RelayInputMoverComponent` / `MovementRelayTargetComponent` +
   `SharedMoverController.SetRelay()` — one entity's movement keys drive another entity.
   Used by mech piloting, station AI, and "piloted clothing" (the hamster-in-a-hat).
3. **The surveillance camera stack** — and crucially the **xenoborg precedent**: upstream
   already puts `SurveillanceCamera` on *moving mobs* ("they act like cameras for the
   mothership", `base_borg_chassis.yml:305`). Mobile camera feeds are proven, not a hack.

Shipped: `SolreignMobRcCameraCar` — a ghost-role toy car whose roof camera streams to the
entertainment camera network (televisions + wireless camera monitors). The "kid holds a
controller and drives it" fantasy needs one small new Solreign C# system (Wave 2, ~1 day).

## What exists upstream (verified in this repo)

### Vehicles: removed, only fossils left
- `grep -ri vehicle` across Content.* yields **only comments**
  (`Content.Client/Physics/Controllers/MoverController.cs:59`,
  `Content.Server/Polymorph/Systems/PolymorphSystem.cs:203`). No `VehicleComponent`,
  no buckle-to-ride, no keys, nothing.
- Orphaned sprites survive at `Resources/Textures/Objects/Vehicles/`
  (atv, janicart, motorbike, secway, syndicatesegway, unicycle, wheelchair — all
  CC-BY-SA-3.0 ex-tgstation, 4-direction animated `vehicle` states, **zero prototypes
  reference them**). Free art for this lane.

### Movement + possession primitives
- `Content.Shared/Movement/Components/RelayInputMoverComponent.cs` +
  `MovementRelayTargetComponent.cs`, wired in
  `Content.Shared/Movement/Systems/SharedMoverController.Relay.cs` (`SetRelay(uid, relayEntity)`,
  handles prediction, cleanup on shutdown, can-move relaying). This is the real "remote control"
  primitive.
- Proof-of-pattern consumers: `Content.Shared/Clothing/EntitySystems/PilotedClothingSystem.cs`
  (pilot inside a storage-clothing item drives the wearer — closest existing analogue to
  "controller drives car"), `SharedMechSystem`, `SharedStationAiSystem`, `CardboardBoxSystem`.
- Ghost roles: `GhostRole` + `GhostTakeoverAvailable` on any mob with `InputMover` makes it
  player-drivable (all of `silicon.yml`). `BaseControllable` already carries Eye, MindContainer,
  Input, Fixtures.

### Surveillance camera stack (complete and healthy)
- Shared/Server/Client split under `*/SurveillanceCamera/`. Cameras answer router pings over
  DeviceNetwork; monitors give viewers a PVS view subscription onto the camera entity
  (`SurveillanceCameraComponent.ActivePvsViewers`).
- **Moving cameras are upstream-supported**, twice:
  - `SurveillanceWirelessCameraMovableBase` (`wireless_surveillance_camera.yml`) — an
    unanchored, carryable streaming camera.
  - Xenoborgs: `SurveillanceCamera` (+ `nameSet`/`networkSet`/`useEntityNameAsCameraId`)
    on live player mobs, viewed from the mothership console.
- Cameras with `nameSet: true` + `networkSet: true` need **no setup UI** and answer pings
  immediately (`SurveillanceCameraSystem.OnPacketReceived` doesn't gate on setup state).
- Wireless entertainment chain already exists end-to-end:
  camera (rx `SurveillanceCameraEntertainment`, tx `SurveillanceCamera`) →
  `SurveillanceCameraWirelessRouterEntertainment` → `WallmountTelevision` /
  `ComputerSurveillanceWirelessCameraMonitor`.

### Remote control: nothing
- `grep -r RemoteControl` — zero hits outside _Solreign. No handheld-controller-drives-entity
  system exists. (The signal-trigger "remote signaller" is a different thing — one-shot
  DeviceLinking pulses, not driving.)

## What shipped in this spike

**`Resources/Prototypes/_Solreign/Entities/rc_car.yml`** — `SolreignMobRcCameraCar`
(+ `Resources/Locale/en-US/_Solreign/rc-vehicles.ftl` for ghost-role/petting strings):

- Parent `MobSiliconBase` → movement, robotic mob state (dead at 120), welder-repairable,
  pullable, robotic speech/emotes. Silicon door access **removed** (a toy earns no doors).
- Ghost role, harmless free agent (`MindRoleGhostRoleFreeAgentHarmless`) — the car is
  player-driven, PG by construction: no combat mode, no weapon, nonlethal, "be delightful" rules.
- Roof camera per the xenoborg pattern: streams to the entertainment network, name shows as
  the entity name on monitors. Microphone matches upstream entertainment cams (TV audiences
  hear nearby chatter). Little acid-green headlight. Petting popups.
- Sprite: reuses orphaned `Objects/Vehicles/atv.rsi` at 0.55 scale (flagged
  `TODO(SOLREIGN-ART)` for the sprite factory).

**In-game verification recipe** (needs a live server; not run in this spike):
1. Spawn `SolreignMobRcCameraCar`, a powered `SurveillanceCameraWirelessRouterEntertainment`,
   and a powered `WallmountTelevision` (all in-range, ≤200 tiles).
2. As a ghost, claim the "Solreign Camera Car" role; drive around.
3. Open the television UI → "solreign camera car" appears in the entertainment subnet →
   select it → live moving feed.

**Tests:** none added — the spike is pure YAML/data with no new logic; there is nothing
unit-testable. Wave 2's bind/range/unbind logic is where NUnit lands.

## Gap list — the full "RC car with controller in your hands" fantasy

| # | Gap | What's needed | Honest effort |
|---|-----|---------------|---------------|
| 1 | **Handheld controller drives the car** | New Solreign system: `RcControllerComponent` (item) + `RcControllableComponent` (car). On use-in-hand: `SetRelay(user, car)`; unbind on drop/unequip, holder damage, range exceeded, car destroyed. `PilotedClothingSystem` is the ~150-line template; add a range check on update. | 150–250 LOC C# + NUnit for bind/range/unbind rules. ~0.5–1 day. |
| 2 | **Driver sees through the car** | While bound, either (a) open the camera monitor UI from the controller (reuse `SurveillanceCameraMonitorSystem` — the controller becomes a tiny wireless monitor, likely YAML-mostly), or (b) eye-swap to the car's Eye (how mechs/AI do it — cleaner feel, more C#). Recommend (a) first. | (a) ~0.5 day, (b) 1–2 days with prediction testing. |
| 3 | **Pause the driver's body** | While driving, holder shouldn't also walk (relay already redirects movement input — verify interactions/combat are blocked or accept "distracted toddler" mode). `PilotedByClothingComponent` shows the pattern. | Included in #1. |
| 4 | **Buy/build path** | Toy vendor catalog entry + optional lathe recipe. Touches shared upstream catalog files — coordinate, don't collide with Wave 4 agents. | ~1 hour when file locks clear. |
| 5 | **Rideable vehicles (go-karts)** | Upstream removed buckle-ride vehicles entirely. Real seats-and-keys karts mean porting/rebuilding a vehicle system (buckle + relay + keys + collision fun) — do NOT clone from AGPL forks; build on `SetRelay` + `Buckle` ourselves. | 2–4 days, own spike. Mini golf is unrelated (projectile physics, cheaper). |
| 6 | **Custom art** | Toy-car RSI (4-dir, animated, dead state) via sprite factory; camera-bump silhouette so it reads on TVs. | Sprite factory lane. |

## Recommendation

Ship the ghost-role camera car now (it's a complete delighter: kids drive it, everyone else
watches ToyTV), then do Wave 2 = gap #1 + #2(a) as one small PR — that's the moment it becomes
a *remote-controlled* car instead of a *possessed* one. Defer go-karts to their own spike.
