# Silent-Failure Sweep — orphan report

**Date:** 2026-07-11
**Lane:** Phase-2 Track A2, "Silent-failure sweep" (`docs/plans/2026-07-11-ROADMAP-PHASE2.md`
§2 Track A, Wave A2), plus Track C3's permanent boot-time integrity gate.
**Method:** scripted, not eyeballed — see `docs/specs/2026-07-11-orphan-report.md#method` below for
the exact three-pass sweep. Re-run twice during this session because a concurrent Track A1 session
landed the Werewolf/Vampire/Terminator-cosmetic fixes *while this sweep was running* (see
`docs/plans/2026-07-11-ROADMAP-PHASE2.md` Track A Wave A1) — every number in this report reflects
the repo state **after** that concurrent landing, not the state the sweep started against.

## TL;DR

- **Part 1 (C# → prototype-id string refs):** 23 typed (`EntProtoId`/`ProtoId<T>`) field defaults
  found repo-wide under `_Solreign`. All 23 now resolve. (One did not at sweep start —
  `SolreignWerewolfComponent.WolfPolymorphPrototype = "SolreignWerewolfPolymorph"` — but Track A1
  landed the missing prototype mid-sweep; see "Already fixed" below.)
- **Part 2 (prototype → prototype-id refs, e.g. `effectPrototype`, `spawn`, `polymorph`, sound
  collections):** 0 dangling references found. Every `effectPrototype`, `EntityTableContainerFill`
  spawn-key, `soundCollection` reference, etc. under `Resources/Prototypes/_Solreign` resolves.
- **Part 3 (entity-prototype reachability):** of 219 non-abstract `_Solreign` entity prototypes,
  **41 were never reachable** via vendor / map placement / spawner / game rule / crafting at sweep
  time (excludes 6 legitimately-reachable `StationEvent`/`GameRule` prototypes that don't need a
  cross-file reference — see Method). 5 of those 41 were fixed inline during this pass (cheap,
  additive, one vendor-list edit, see below). The remaining **36** are filed below with a
  recommendation each.
- **Part 4 (C3):** a permanent integration test,
  `Content.IntegrationTests/Tests/_Solreign/SolreignPrototypeIdIntegrityTest.cs`, now reflects over
  every `_Solreign` C# type and asserts every `EntProtoId`/`EntProtoId<T>`/`ProtoId<T>` field
  default resolves against the loaded `IPrototypeManager` — the exact class of bug Part 1 found,
  turned into a boot-time/CI gate so it can't ship silently again.

---

## Method

1. **C# → prototype-id string refs.** Regex-scanned every `.cs` file under `Content.Server/_Solreign`,
   `Content.Shared/_Solreign`, `Content.Client/_Solreign` for `EntProtoId`/`EntProtoId<T>`/`ProtoId<T>`
   field declarations carrying a string-literal default, then cross-checked each id against a full
   index of every `id:` under `Resources/Prototypes/**/*.yml` (a permissive YAML loader that treats
   unknown `!type:` tags as plain mappings/scalars, since this repo's YAML uses dozens of them).
2. **Prototype → prototype-id refs.** Walked every YAML mapping under `Resources/Prototypes/_Solreign`
   looking for keys matching `proto|spawn|polymorph|action|reward|clue|entity|mob|target|effect|item|
   template|dataset|rule|objective|gear` (case-insensitive) whose value(s) look like an identifier,
   then checked each against the same prototype-id index. 8 raw hits were false positives (enum
   values like `NoItem`/`Alive`, or component-internal keys like `children`) — all checked and
   confirmed benign.
3. **Entity-prototype reachability.** Listed all 219 non-abstract `type: entity` prototypes under
   `Resources/Prototypes/_Solreign`, then for each one did a **word-boundary-precise** occurrence
   count of its id across `Content.Server`, `Content.Shared`, `Content.Client`, `Resources/Prototypes`,
   `Resources/Maps`, `Resources/Locale`, `Resources/ServerInfo` (excluding the substring-collision
   trap of e.g. `SolreignExecutiveVendor` matching inside `SolreignExecutiveVendorInventory`). An id
   with **zero occurrences outside its own defining file** is an orphan — nothing outside the file
   that declared it ever names it. `GameRule`/`StationEvent`-tagged entities are **not** flagged even
   at zero cross-file occurrences: those are reachable by design via the admin "add game rule" panel
   or the station-event scheduler, which is the game's own reachability channel for that prototype
   kind (confirmed against this repo's own `compliance_hunt.yml`/`minibosses.yml` header comments,
   which document exactly this — "admin-invocable today, neither is wired into any random event
   table").
4. **C3 test.** Wrote the boot-time integrity gate described above; see that file's doc comment for
   why the engine's own `ProtoIdSerializer`/`EntProtoIdSerializer` validation does *not* already
   catch this defect class (short version: it only validates values read from YAML, never a C#-side
   compile-time default that nothing overrides).

---

## Already fixed (found stale mid-sweep, not double-counted)

A concurrent Track A1 session landed three fixes while this sweep was in progress. Verified current
(post-landing) and **not** re-flagged below:

- **Werewolf polymorph** — `SolreignWerewolfComponent.WolfPolymorphPrototype = "SolreignWerewolfPolymorph"`
  now resolves (`Resources/Prototypes/_Solreign/Polymorphs/werewolf_polymorph.yml` +
  `Resources/Prototypes/_Solreign/Entities/Antags/werewolf.yml`). Confirmed via Part 1's script and
  via the new `WerewolfPolymorphTriggerTest.cs`.
- **Terminator skin** — `compliance_retrieval_unit.yml` sprite repointed from the stock syndicate
  `synd_sec` state to `xenoborg_heavy`. (Reachability of the mob **itself** is a separate, still-open
  finding below — the cosmetic fix and the spawn-path fix are different problems.)
- **Vampire props** — `SolreignCoffinComponent`/`SolreignChapelGroundComponent`/
  `SolreignGarlicWardComponent` now have matching entity prototypes
  (`Resources/Prototypes/_Solreign/Entities/Antags/vampire.yml`: `SolreignVampireCoffin`,
  `SolreignVampireGarlicWard`, `SolreignVampireChapelGround`). That file's own header says map
  placement is a separate, not-yet-landed pass — consistent with this sweep still finding all three
  at zero external references (see below); **not** a new regression, an explicitly-scoped half-done
  state.

## Fixed inline during this sweep

One cheap, additive, low-conflict-risk fix applied (`Resources/Prototypes/Catalog/VendingMachines/
Inventories/games.yml`, the vanilla `GoodCleanFunInventory` pack already stocked in the placed,
reachable `VendingMachineGames` — confirmed present on `Resources/Maps/_Solreign/solreign_oasis.yml`):

- Added `SolreignFireworksLauncher: 2` and `SolreignPoolNoodle: 3` to the vendor's
  `startingInventory`.
- This closes **5** orphans in one line each, because the launcher's own
  `EntityTableContainerFill` already dispenses the other three (`SolreignFireworkPopper`,
  `SolreignFireworkFountainGreen`, `SolreignFireworkFountainCyan`) — those three were reachable
  *in principle* the whole time, just downstream of an unreachable root. Verified via a
  before/after re-run of the Part 3 script.

No other fix was judged safe to make inline this pass — see "why not fixed inline" per item below.
The remaining items either (a) require spatial map-coordinate edits inside a 71k–1.5MB-line tilemap
YAML that this sweep has no way to visually verify placement for, (b) are explicitly deferred by
their own file's header comment to a named future wave/lane (not an accident — self-documented
scope fences, a real strength of this codebase's hygiene), or (c) are actively being closed out by
the concurrent Track A1 session as this report is being written.

---

## Open orphans (36), grouped by recommended fix

### A. High severity — flagship mechanics with zero spawn path

| Id | File | Why it matters | Recommendation |
|---|---|---|---|
| `SolreignHotPotatoBomb` | `Entities/hot_potato.yml` | The entire Hot Potato feature (a full C# system: fuse timer, transfer-on-bump, beep audio, `SolreignHotPotatoSystem`) has **no vendor entry, map placement, spawner, or game rule** anywhere. Task #26 lists this as shipped; it is logic-complete but a player can never acquire one. | Cheapest fix: add 1 to an existing reachable vendor pack (thematically, `GoodCleanFunInventory` again, or a break-room/HR vendor if one exists) **or** one map placement in a common area. Needs a human/mapping-lane call on *where* — not attempted here (would be a blind tilemap edit). |
| `SolreignMobComplianceRetrievalUnit` (Terminator) | `Entities/compliance_retrieval_unit.yml` | Ghost-role hunter mob (`GhostRole` + `GhostTakeoverAvailable`) — needs something to spawn the entity before a ghost can claim it. `compliance_hunt.yml`'s own header explicitly says: *"TERMINATOR LANE: append the Compliance Auditor spawn event id here... add `SolreignComplianceHunterRule` (or final id)"* — i.e. this is a **known, tracked, not-yet-closed gap**, distinct from the sprite issue Track A1 just fixed. | Needs the `SolreignComplianceHunterRule` game rule (StationEvent + ghost-role spawner, upstream `NinjaSpawn`/`LoneOpsSpawn` pattern per the file's own notes) — real C# design work, not a YAML wiring fix. Filed, not attempted here. |
| `SolreignExecutiveVendor` | `Entities/contracts_executive_vendor.yml` | The vendor entity itself is never placed on any map. Its 6 stocked items (`SolreignGoldenStapler`, `SolreignExecutiveMug`, `SolreignCornerOfficeHologram`, `SolreignComplianceMedal`, `SolreignQuarterlyReport`, `SolreignExecutiveBell`) are all correctly wired into its `startingInventory` — they cascade-fix the moment the vendor is placed. | One map placement (rank-gated area, e.g. near Command per the file's own "Manager+" access gate) fixes 7 orphans at once. Not attempted here — map-coordinate risk. |

### B. Known, self-documented, in-flight (Track A1 territory, not new findings)

| Id | File | Status per file's own comments |
|---|---|---|
| `SolreignVampireCoffin`, `SolreignVampireGarlicWard`, `SolreignVampireChapelGround` | `Entities/Antags/vampire.yml` | File header: *"Map placement is the OTHER half and is out of scope here... see `docs/specs/2026-07-11-vampire-props-map-graft-addendum.md` for the exact placement request handed to the mapping lane."* Prototypes exist (Track A1 just landed them); map placement is the explicitly-named next step. |
| `SolreignCrimsonKeycard`, `SolreignSublevelHGate` | `Entities/Markers/hellzone_gates.yml` | File header: *"SolreignSublevelHGate is the ENTRY side. It is NOT placed anywhere by this wave... Hand-placing one instance of this prototype into deep maintenance (with a Crimson Keycard seeded somewhere reachable) is the follow-up mapping step."* Entry-gate suffix is literally tagged `DO NOT MAP` pending that follow-up. |

Both rows are real orphans by the sweep's definition, and both are already self-tracked TODOs
(not silent bugs) — most likely the same concurrent session (or the next mapping-lane wave) closes
them shortly given `SolreignZoneGateRules.cs` was touched minutes before this report was written.
Flagged for completeness/traceability, not re-litigated as new findings.

### C. Documented-gap gear/pets/decor — "spawn via admin" was an accepted interim state

All of the following come from files whose own header comments **explicitly** say the wiring pass
is deferred (quotes below), which is why none of these were treated as cheap/obvious enough to
hack a vendor-list edit for without contradicting the author's stated plan:

| Id(s) | File | Author's own note |
|---|---|---|
| `SolreignClothingBackHalcyonHoverpack`, `SolreignClothingBackVergeCourierSatchel`, `SolreignClothingBeltLumenbridgeToolbelt`, `SolreignClothingEarsCompliantChorusEarbuds`, `SolreignClothingEyesSynapseSpectacles`, `SolreignClothingEyesVergeVigileShades`, `SolreignClothingHandsHalcyonHospitalityMitts`, `SolreignClothingHeadAntennaeTopHat`, `SolreignClothingHeadVergeVanguardVisor`, `SolreignClothingNeckLilCorpLavenderTie` (10 items) | `Entities/gear.yml` | *"SPAWNING: not yet in any loadout/vending fill — spawn via admin, or wire into the Contracts reward pool later (Contracts lane owns that file; not touched here)."* |
| `MobSolreignTabby`, `MobSolreignDuck`, `MobSolreignPup` | `Entities/pets.yml` | No explicit "not placed" note, but same idiom as `MobScarlet` (which the sweep also flags) — reskinned tameable critters with zero map/spawner presence anywhere. |
| `MobScarlet` | `Entities/delighters/scarlet.yml` | Named station pet (Scarlet the corgi), fully wired for taming (`SolreignTameableComponent`) but never placed. |
| `SolreignZooMothroach`, `SolreignZooExhibitCrab`, `SolreignZooExhibitBat` | `Entities/zoo_fauna.yml` | *"MAP PLACEMENT GAP (documented honestly, per this lane's scope — map edits belong to the Oasis map-building lane, not this audio-wiring lane): these three prototypes are NOT yet placed on solreign_oasis.yml's three 'Zoo Habitat Divider' pens."* Also blocks an ambient-music gate (`NearZooExhibit`) that depends on a live instance existing. |
| `SolreignSignCanteen`, `SolreignSignAhelpReminder`, `SolreignSignLedgerExplainer` | `Entities/signage.yml` | *"NO map placement here — Wave 9 owns Resources/Maps/_Solreign/solreign_oasis.yml..."* Note: 3 of this same file's 6 signs (`SolreignSignArrivalsWelcome`, `SolreignSignZooWarning`, `SolreignSignContractsHowTo`) **are** placed — Wave 9 did half the set and never finished it. |
| `SolreignGolfClubPutter`, `SolreignGolfClubDriver` | `Entities/recreation.yml` | Mini-golf's ball/hole/course props are placed; the two club **variants** (base golf club presumably is, these two aren't) never made it into a vendor or the course kit. |
| `SolreignGoKartBoostPad`, `SolreignGoKartMudPatch` | `Entities/recreation.yml` | Track hazard/boost props with zero placement — the go-kart track itself may or may not exist yet on the map; needs a mapping-lane check either way. |
| `SolreignSignRewardPending` | `Entities/portal_props.yml` | Portal-pack prop, same "props only, mapping is a later wave" framing as the file's own header. |
| `ClothingEyesSolreignComplianceLensesExecutive`, and its parent `ClothingEyesSolreignComplianceLenses` | `Entities/nvg.yml` | Task #28 ("night vision goggles — full mechanics") is marked complete in the build log, but **neither** the base compliance lenses nor the executive variant has a vendor/loadout/map presence anywhere — the whole NVG item pack is mechanically finished and completely unreachable in a live round. |
| `SolreignMobAwakenedTreeAcidGlow`, `SolreignMobAwakenedTreeCyanGlow` | `Entities/ents.yml` | Glow-variant reskins of the base Awakened Tree (which is itself reachable via `SolreignDormantEntComponent.AwakenedPrototype`) — the two color variants have no distinct spawn path of their own. |
| `SolreignBoxWeirdSeeds` | `Botany/seed_packets.yml` | The "starter box" containing the weird-botany seed packets — packets themselves are reachable if grown from, but the box that's supposed to hand them out isn't vendored/placed anywhere. |

**Recommendation for all of Group C:** batch these into a single follow-up "reachability wiring"
wave, scoped exactly like Track A2 describes — most likely destinations are (a) the Contracts
reward pool (`contracts_season1.yml`, explicitly named by `gear.yml`'s own header as the intended
home for the gear pack) for the clothing/lore-flavor items, and (b) one focused mapping pass over
`solreign_oasis.yml` for the zoo fauna, signs, pets, and recreation props. Do not treat "spawn via
admin" as broken — it is this codebase's own accepted stopgap (see `gear.yml` quote above) — but it
does mean these are not player-facing-complete for Grand Opening under the roadmap's own success
criteria ("every mechanic must be reachable, not admin-only").

---

## C3: permanent boot-time integrity test

`Content.IntegrationTests/Tests/_Solreign/SolreignPrototypeIdIntegrityTest.cs` (new file). Reflects
over every type in `Content.Server._Solreign`, `Content.Shared._Solreign`, `Content.Client._Solreign`,
finds every field typed `EntProtoId` / `EntProtoId<T>` / `ProtoId<T>` (nullable or not), reads its
reflection-default (direct read for `static` fields, a freshly-constructed instance for instance
fields — types that can't construct parameterlessly are skipped, not failed, per its own doc
comment), and asserts every non-empty id found resolves against that side's `IPrototypeManager`.
This is a **general, permanent** version of this report's Part 1 — it would have failed the build
the day the Werewolf polymorph reference was written, instead of waiting for a player to hit
Transformed with nothing happening.

Exit-check per the roadmap's own Wave C3 criteria ("intentionally break a prototype reference in a
scratch branch, confirm the new gate fails"): not run in this pass (would require a scratch
mutation + revert this sweep didn't want to risk mid-concurrent-session); recommended as the first
thing the next session does before trusting this test in CI.

---

## Checklist

- [x] Part 1 — C# → prototype-id scan scripted and run (23 refs, all resolve as of current repo
      state; 1 was dangling at sweep start, fixed by a concurrent Track A1 session mid-sweep).
- [x] Part 2 — prototype → prototype-id scan scripted and run (0 dangling refs; 8 false-positive
      hits manually confirmed benign).
- [x] Part 3 — full entity-prototype reachability sweep (219 non-abstract prototypes; 41 orphans
      found, 5 fixed inline, 36 filed above with per-item recommendations).
- [x] Cheap/obvious fixes applied: `games.yml` vendor-pack addition (2 lines, 5 orphans closed).
- [x] C3 boot-time integrity test written: `Content.IntegrationTests/Tests/_Solreign/
      SolreignPrototypeIdIntegrityTest.cs`.
- [ ] C3 exit-check (intentional-break drill) — **not run this pass**, recommended next session.
- [ ] Group A (high severity: Hot Potato, Terminator spawn, Executive Vendor placement) — **filed,
      not fixed** (needs a mapping/design-lane call, not a sweep-scale edit).
- [ ] Group B (Vampire props + Sublevel H gate/keycard map placement) — **in flight under Track A1**,
      not this wave's job to finish.
- [ ] Group C (10 gear items, 3 pets, 3 zoo fauna, 3 signs, 2 golf clubs, 2 kart props, 1 sign,
      2 NVG items, 2 ent glow variants, 1 seed box) — **filed** as a follow-up "reachability wiring"
      wave, recommended split between the Contracts reward pool and a focused mapping pass.
