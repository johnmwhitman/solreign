# Unwired / unplaced content audit — 2026-07-26

Scope: every Solreign-authored entity prototype, measured against the **7 rotation maps**
(`leviathan, meridian, nocturne, oasis, perihelion, terminus, verdant`).

**Read §1 first. It corrects two claims that are currently steering work, including the #1 item
in `HANDOFF-SOLREIGN-ENGINEERING-2026-07-26.md`.**

---

## 1. 🔴 TWO CORRECTIONS — both were "verified" findings, both wrong

### 1a. The Continuity Garden is NOT dead. It ships on all 7 maps, every round.

The handoff §5 item 1, the memory entry, and a `/goal` START HERE instruction all say: *"`echo` is
ON and inert — `SolreignMarkGarden` is placed on ZERO maps, so Echo projects grave markers into a
garden that does not exist."*

The grep is right and the conclusion is wrong. **Map placement was never the mechanism.**

Verified in the artifact:
* `SolreignMarkGardenSystem.MaterializeAndProject` runs on `StationPostInitEvent` whenever
  **either** `solreign.mark.enabled` or `solreign.echo.enabled` is live.
* It does `ResolveGarden(station) ?? SpawnFallbackGarden(station)` — the §4.3 runtime fallback
  spawns the garden on the first safe tile in a candidate ring around the station's
  `SolreignWingmateBeacon`.
* `SolreignWingmateBeacon` is placed on **all 7 rotation maps** (exactly one `- proto:` entry each).
* Both CVars default **true** (`CCVars.SolreignEcho.cs:23`, `CCVars.SolreignMark.cs:22`).
* Asserted by `MarkGardenIntegrationTest.FallbackPlacement_NoMapGarden_SpawnsBesideTheWingmateBeacon`:
  *"the §4.3 fallback must spawn exactly one garden when a beacon exists and no fixture does."*
* `SolreignMarkGarden` is on `orphan_allowlist.yml` with exactly this reason. The allowlist was
  right; the grep-based reading of it was not.

**Consequence: "place the Continuity Garden" was the top-ranked open item and is largely a
phantom.** A map-placed fixture (MG-W5) is a *polish* upgrade — deterministic, art-directed
placement instead of a beacon-relative ring — not the difference between working and dead.

### 1b. The three zoo creatures ARE placed. The "zero live instances" finding is wrong.

`findings/unplaced-zoo-fauna-2026-07-26` reports `SolreignZooMothroach`, `SolreignZooExhibitCrab`
and `SolreignZooExhibitBat` as having zero live instances anywhere. They are placed on
**`solreign_meridian.yml`** — uids `900002/900003/900004` at `(43.5,7.5)/(45.5,7.5)/(47.5,7.5)`,
grid 30, beside `SolreignSignMeridianZoo`. Real `- proto:` entries with real transforms.

How two independent sources agreed and were both wrong:
1. The prototype file's own comment said *"zero live instances anywhere"* — **true when written
   2026-07-11, false since the Meridian placement landed.**
2. The live-map read was done against **Perihelion**, which is neither the map with the fauna
   (Meridian) nor the map with the empty pens (Oasis).

The stale comment is corrected in this commit. **A comment asserting a repo-wide negative is a
claim with an expiry date.**

**What survives from that finding:** the Oasis zoo wing really is built and empty. That part is
confirmed and is a genuine, actionable gap — see §3.

---

## 2. THE MEASUREMENT — and why the existing gate is blind to this

`SolreignOrphanReachabilityTest` asks a **binary** question: is this prototype referenced by *any*
map/spawner/rule? The operational question is different: **in a given rotation round, can a player
encounter it?** That gap produces errors in *both* directions, and this audit found one of each:

| | binary gate says | reality |
|---|---|---|
| `SolreignMarkGarden` | unreachable (allowlisted away) | live on 7/7 via C# fallback |
| `SolreignZooExhibitBat` | **reachable** ✅ | live on **1/7** — absent from 6 of 7 rounds |

Per-rotation-map coverage, 121 Solreign protos placed somewhere:

| bucket | count | reading |
|---|---|---|
| 7/7 full rotation | 46 | fine |
| station-flavour (map name in the id) | 31 | 1-map **by design** |
| partial, not station-flavour | **44** | audit candidates → §3 |

Most of the 44 are station-flavour *by theme* rather than by name and are also fine: the GoKart /
RC-car cluster is Terminus's Grand Prix, the glow-plants and synergy fruit are Verdant's botany,
the vampire chapel set is Oasis, `SolreignSodaCan` is the Nocturne bar. Those are deliberate
per-station identity, not defects.

---

## 3. GENUINE GAPS — ranked

1. **Oasis zoo wing is built and empty.** Oasis has `Zoo Wing Entrance`, `Zoo Keeper Door`,
   `Zoo Wing APC`, `SolreignZooContainmentConsole`/`Emitter`/`BioFeeder`/`RecoveryPad`, and three
   `Zoo Habitat Divider` ReinforcedWindows at `(-16.5,-27.5)/(-14.5,-26.5)/(-13.5,-25.5)` on grid
   31 — and no animals. The fauna exist and work; they are on the wrong map.
   ⚠️ **The dividers are the walls BETWEEN pens, not the pens.** Resolve the enclosed tiles before
   wiring any spawn coordinate — do not treat the divider positions as spawn points.
2. **`SolreignSignAhelpReminder` — 6/7, missing on Oasis.** Generic new-player help signage that
   is on every other rotation map. Almost certainly an oversight, and it is the cheapest fix here.
3. **`SolreignSignContractsHowTo` — 5/7, missing on Nocturne and Verdant.** Contracts is a core
   loop; its explainer sign is absent on two maps.
4. **`SolreignSignZooWarning` is on Oasis + Verdant** — i.e. zoo *signage* on two maps, zoo *pens*
   on Oasis, zoo *fauna* on Meridian. Three different footprints for one feature. Verdant has a
   zoo warning sign and neither pens nor animals.
5. `SolreignPoolNoodle` (Oasis) and `SolreignEgg01`/`SolreignEgg08` (Oasis/Perihelion) — previously
   flagged as re-orphaned; both are single-map. Worth a design call on whether the egg set was
   meant to be split across maps.

## 4. METHOD NOTE — the discipline this audit needed

Every claim here was checked against the artifact, not against a comment, a doc, or another
agent's summary. Three separate "verified" statements failed that check this session: the
zoo-fauna finding, the garden's inertness, and the prototype file's own header. In each case the
grep was accurate and the *interpretation* was not — the error was reading "not placed on maps" as
"does not exist" and "placed on a map" as "reachable in a round".

**Before acting on any unplaced-content claim: grep all 7 maps, then check for a C# spawn path and
the `orphan_allowlist.yml` reason.** Placement is one of three reachability mechanisms, not the
definition of the word.
