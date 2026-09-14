# Branch `art/kolton-delighters-20260731` — gate-verified, ready for review

**Author:** the idea-raid chat (not the SOLREIGN engineering chat). Raised 2026-07-31.
**Base:** `origin/master` @ `6ed2aed43a4`. **Worktree:** `~/AI/solreign-trees/kolton-delighters-20260731`.

> **To the engineering lane:** built in its own worktree so it could not touch your tree or index.
> Nothing is merged. Both gates you pointed me at are now green — `SolreignSpriteStateExistsTests`
> and `SolreignOrphanReachabilityTest`, 22/22 — and running them beat every hand-audit I did.
> It touches two upstream files (`Spawners/Random/maintenance.yml`, `VendingMachines/Inventories/salvage.yml`),
> both with the same `# Solreign` comment convention the existing eggs use.

## What this is

A "delighter" slice built off the 2026-07-31 idea raid. The raid killed a proposed `.rsi`
marketplace (no buyers; SS14 art is CC-BY-SA 3.0 so there is no repeat sale) but surfaced that we
already own the whole production side — `pxl` + `sprite-factory` + ~3,900 pre-paid PixelLab
generations — and that the most-requested unbuilt thing from the 2026-07-30 playtest was character
and world flavour.

## Contents

| Path | What |
|---|---|
| `Resources/Textures/_Solreign/Delighters/solreign_star_chart.rsi` | new art — icon + auto-rigged 4-dir in-hands, `validate --profile new-asset: passed` |
| `Resources/Textures/_Solreign/Delighters/solreign_star_in_a_jar.rsi` | same, passed |
| `Resources/Textures/_Solreign/Delighters/_staged/solreign_salvage_cache.rsi` | icon only — staged, needs 5 more states before use (see below) |
| `Resources/Prototypes/_Solreign/Entities/delighters_treasure_hunt.yml` | 2 entities, both gates green (cache cut — see the file) |
| `Catalog/VendingMachines/Inventories/salvage.yml` · `Spawners/Random/maintenance.yml` · `_Solreign/Entities/contracts_executive_vendor.yml` | reachability wiring |

**Verified by this repo's own gates, not by hand.** All three RSIs pass `spritefactory validate
--profile new-asset` and are CC-BY-SA-3.0 with license + copyright in `meta.json`. The prototypes
are live in `Resources/Prototypes/`, **both gates pass 22/22**, and the **full `Content.Tests`
suite is green: 3136 total, 3133 passed, 3 skipped, 0 failed.**

### Update 2026-08-01 — the engineering lane was right, and the gates did the work

The first draft of this branch parked the YAML outside the loadable tree because I had hand-audited
three parent RSIs and did not trust it. The engineering lane pointed out that both problems already
have gates here. They were right, and running them was far cheaper and far better than my audit:

`SolreignSpriteStateExistsTests` found **7** defects, named each one, and printed
`that RSI has: icon, inhand-left, inhand-right` next to it. **Two were invisible to my
hand-audit** — `heldPrefix: null` synthesises a state literally named `null-inhand-left`, and the
same for `-right`. It also found **nothing** on `SolreignSalvageCache`, confirming the one place I
had refused to override a sprite was the one place I got right.

Fix: `SolreignStarChart` and `SolreignStarInAJar` now parent **`BaseItem`**, which declares no
Sprite component, so nothing is inherited and the contract is exactly what the file states. The jar
gets an always-on `PointLight` instead of parenting `FlashlightLantern` — a toggleable lantern
needs `-on`/`-off` art this family does not have. Gate re-run: **21/21, exit 0.**

## ⚠️ The defect that was here — FIXED above, kept as the record

I picked parent prototypes by *concept* without honouring their **sprite contracts**. An entity's
sprite is not always a single `icon`; if you override `sprite:` on a parent whose RSI carries more
states, the missing ones break at runtime. Measured in this checkout:

| Parent I chose | Its RSI actually needs |
|---|---|
| `Paper` | **layered** — `paper`, `paper_words`, `paper_stamp-generic` |
| `CrateGenericSteel` | `icon`, `base`, `closed`, `open`, `welded`, `sparking` |
| `FlashlightLantern` | `lantern`, `lantern-on`, `off-inhand-*`, `on-inhand-*`, `burnt`, `flashing`, `*-equipped-BELT` |

Our pipeline emits exactly `icon` + `inhand-left` + `inhand-right`. `spritefactory pixellab-intake`
warns about precisely this — *"`--state`: READ IT OFF THE CONSUMING PROTOTYPE, never a generator
default"* — and I did not.

**Fixed by parenting `BaseItem` (no inherited Sprite), verified by the gate.** For reference, 100 RSIs under `Resources/Textures/Objects/` have exactly the
`{icon, inhand-left, inhand-right}` state set — including `flask_old.rsi`, which is why the shipped
`_Solreign` eggs that parent `DrinkFlaskOld` work correctly. Either (a) re-parent to one of those
proven simple items, or (b) override the parent's whole `Sprite.layers` list with a single layer.

## The design rule these follow

A gift that is *handed* to a player reads as a participation trophy, and a master player detects
that instantly. So: the chart is **found**, and the prize is **earned** — one unit, on a Manager+
shelf whose rank you pay for in Corporate Standing. The
prize carries an always-on `PointLight`, so it is a real light source — it gets carried, so other
players see it. The social layer is the point.

## NOT DONE — what a reviewer should know before touching this

1. ✅ **Spawn wiring is DONE — both gates are green (22/22).** Reachability, verified empirically
   against `SolreignOrphanReachabilityTest` rather than guessed:
   - **`SolreignStarChart`** — stocked in `SalvageEquipmentInventory` (salvagers buy charts), and
     additionally seeded into `MaintFluffTable` at weight 0.3 so it is genuinely *found* in maint.
   - **`SolreignStarInAJar`** — one unit on `SolreignExecutiveVendorInventory`, the Manager+
     rank-gated shelf. Rank is earned from the Corporate Standing contracts pay out, so the prize
     is *earned*, which is the design rule this branch is built on. Not handed over.
   - **`SolreignSalvageCache`** — **cut from the slice**, see the YAML. Not allowlisted.

   🔑 **Measured, not assumed:** the gate follows spawner `prototypes:` lists **one level only**.
   `MaintenanceFluffSpawner` reaches its contents via `tableId: MaintFluffTable`, a *second* hop,
   so an `entityTable` entry alone does **not** satisfy it — adding the chart there left it listed
   as an orphan (3 → 2 → 0 across three runs). `startingInventory` keys **do** satisfy it. If you
   add delighters later, use a vendor shelf or a map, and re-run the gate rather than trusting a
   spawner table.

2. **The chart art does not encode a real map.** The "X" is decorative. A chart that actually
   points at a specific maint room is the version worth shipping; this is the placeholder — and it
   is the reason the cache is cut rather than rushed.

## Markings — investigated, correctly scoped, deliberately NOT built here

The playtest's open request was *"more playable races / markings"* (banditofdoom + `.confusedlemon`).
`_Solreign` currently has **zero** marking prototypes, customization textures, or species.

A marking is **not** a free-standing icon. Measured from `tattoos.rsi/tattoo_hive_chest.png`: a
64×64 sheet (4 × 32×32 directions) carrying **43 opaque pixels**, drawn to register inside the torso
silhouette (`parts.rsi/torso_m.png`, bbox x 10–52 / y 10–55). Freeform PixelLab generation cannot
hit that registration — the YAML is ~11 lines, but the art is the hard part.

**`sprite-factory` already solves the hard half.** `spritefactory/worn.py` exposes
`measure_worn_profile`, `stamp(icon, state, direction, profile, grow=)`, `synthesize_frames` /
`synthesize_sheet`, and mirror/reflect rules — a measured-anchor system for placing art on a
humanoid per direction. The `worn` CLI composites a delivery against real species parts from a game
checkout (`--game-root`, `--parts`); it ran clean against this worktree.

**The actual gap is one artifact:** a body-part profile (chest / head / arms / legs boxes) analogous
to `spritefactory/data/anchors_ss14.json` for hands. `grep -ri marking spritefactory/` returns
nothing, so no marking-specific command exists yet. That is a measurement + JSON job against
`parts.rsi` plus a thin CLI verb — not a pixel-art job and not a blocker.

Receipts for the raid that produced this: `~/AI/HOOL/raids/idea-rsi-sprite-marketplace/`.
