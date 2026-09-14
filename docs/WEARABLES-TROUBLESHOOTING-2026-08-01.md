# Visually troubleshooting wearables

_2026-08-01. There is a tool for this and it already works against this checkout._

## The tool

```bash
cd ~/AI/sprite-factory
python3 -c "from spritefactory.cli import main; raise SystemExit(main())" \
  worn <delivery-dir> --game-root <game-checkout> --out <outdir> --scale 6
```

`spritefactory worn` composites every `equipped-*` state in a delivery onto a real humanoid body
taken from the game checkout and writes one PNG per state, 4 directions each. It is the difference
between "the icon looks fine" and "it looks fine **on a person**".

**It scans for worn states.** Point it at a dir of `.rsi` folders; anything with no `equipped-*`
state produces 0 previews (that is not an error — it is the answer).

Two flags do the real work:
- `--game-root` — where the species part sprites come from.
- `--parts` — **override the body.** This is how you check a garment against a non-human species.

Verified on this checkout: 18 Solreign wearables → **31 previews**, one per equipped state per
species variant. Contact-sheet them and misalignment is obvious at a glance.

## What it found — species-variant coverage is inconsistent

SS14 garments carry per-species equipped states (`equipped-MASK-vox`, `equipped-EYES-moth`, …)
because species heads and bodies are different shapes. When a variant is absent the engine falls
back to the base state, so **nothing errors and nothing is logged — it just looks wrong**, and only
for players of that species. That is exactly the kind of bug that survives a human playtest.

Measured: the **vox head is 3 px taller than the human head** (`parts.rsi` head bbox y 3–12 / 72 px
vs y 3–9 / 45 px). A mask drawn for a human head cannot cover a vox beak.

~~**Rendered proof:** `fowl_mask`, which HAS a `-vox` variant, sits correctly on a vox head.
`feathered_mask`, which does not, renders detached and wrong-sized.~~ **RETRACTED — this was the
broken compositor, not the art. See the correction below.** The head-height measurement above is
still sound (it comes from the PNGs directly, not from a render); the *conclusion drawn from it*
was not.

### The gap list — 7 real gaps once non-playable species are excluded

Playable species on SOLREIGN (`roundStart: true`): Arachnid, Diona, Dwarf, Human, Moth, Reptilian,
Shadow, SlimePerson, Vox, Vulpkanin. **`hamster`, `kangaroo` and `monkey` are not species at all** —
they are pet-carrier and transformation states, which removes `egg20_omni_gauntlet` and
`egg07_pale_needle` from the list entirely. By direct measurement **reptilian is within 1px of human
on both torso and head**, so a missing `-reptilian` variant is cosmetically free.

Original 9-item list, kept for the record:

"Commonly provides" = a species suffix that ≥8 upstream items in that same slot supply.

| Item | Slot | Missing | Has |
|---|---|---|---|
| `feathered_mask` | MASK | hamster, vox, vulpkanin | reptilian |
| `egg18_antiviral_herb` | HELMET | hamster, reptilian, vox, vulpkanin | base only |
| `egg03_chestplate` | OUTERCLOTHING | reptilian, vox | base only |
| `flight_jacket` | OUTERCLOTHING | reptilian, vox | base only |
| `nanoware_trenchcoat` | OUTERCLOTHING | reptilian, vox | base only |
| `greenshield_armor` | OUTERCLOTHING | reptilian | vox |
| `egg16_hazard_exosuit` | OUTERCLOTHING | reptilian | vox |
| `egg20_omni_gauntlet` | HAND | kangaroo | base only |
| `egg07_pale_needle` | SUITSTORAGE | cat, dog, fox, hamster, kangaroo, pig, possum, puppy, sloth | base only |

Upstream's own per-slot expectations, for reference:

| Slot | Variants upstream commonly ships |
|---|---|
| MASK / HELMET | hamster, reptilian, vox, vulpkanin |
| OUTERCLOTHING | reptilian, vox |
| EYES | arachnid, moth |
| INNERCLOTHING | monkey |
| FEET | vox |
| HAND | kangaroo |
| SUITSTORAGE | cat, dog, fox, hamster, kangaroo, pig, possum, puppy, sloth |

## 🔴 CORRECTION — the original finding did not survive its own instrument check

Everything above the correction was written off renders produced by a **broken tool**, and the
conclusion it reached was wrong. Recorded rather than deleted, because the failure is the lesson.

**The tool was the defect.** `preview.HUMANOID_PARTS` named `torso_m`/`head_m`. Vox's `parts.rsi`
ships plain `torso`/`head`. The limbs *do* share names, so `worn --parts <vox>` composited legs and
arms, `drawn` was non-zero, and `humanoid_base` returned a **headless, torsoless body**. The garment
then floated in empty space — and I read that as "the ART is broken on vox." It was the instrument.
Fixed in sprite-factory `766cf7d` (`PART_ALIASES` + `CORE_PARTS` refusal, 4 tests, suite 7481→7485).

**Re-measured with a working compositor, the impact is negligible.** Garment overhang against the
torso it sits on, in pixels:

| item | garment x-extent | overhang vs HUMAN | overhang vs VOX |
|---|---:|---:|---:|
| `egg03_chestplate` | 2–29 | **17** | 19 |
| `flight_jacket` | 8–23 | 5 | 7 |
| `nanoware_trenchcoat` | 8–23 | 5 | 7 |
| `greenshield_armor` | 10–20 | 0 | 2 |
| `egg16_hazard_exosuit` | 10–20 | 0 | 2 |

The vox torso is 10–20 → 11–19, i.e. **exactly 2px narrower**. So every garment's *vox-specific*
penalty is a uniform **~2px**, and the items that overhang a vox overhang a **human** by more —
`egg03_chestplate` by 17px, because it is oversized pauldrons by design.

### Verdict: do NOT spend art budget on species variants

The 7 coverage gaps are real and the fallback is cosmetically fine. Two pixels of shoulder overhang
on one of ten playable species is not worth a single PixelLab generation. **Spend the pool on the
collection replacement instead** (`ART-COLLECTION-TABLE-2026-08-01.md`).

The one thing genuinely worth keeping from this investigation is the tool fix — `worn` will now
refuse to render a partial body rather than produce a confident-looking lie.

## Honest limits of this evidence

1. **Missing ≠ broken.** The fallback is sometimes acceptable — a flat pendant or a belt item may
   read fine on any body. The table is a **prioritised suspect list**, not a confirmed bug list.
   Render it and look before fixing.
2. **The vox body render is partly a harness artefact.** `--parts` forces a species `parts.rsi` that
   `worn` composites generically; the limbs in that render are not exactly what a client draws. The
   *comparison between the two masks is fair* — same body, same tool, one variant present and one
   absent — but treat it as a strong indicator, not a screenshot of the game.
3. **Which species are actually playable on SOLREIGN matters more than upstream's list.** If vox are
   not enabled, `-vox` gaps are theoretical. Check the enabled species roster before spending art on
   this; that is a cheaper question than any of the fixes.
4. `SUITSTORAGE`'s nine variants are pet-carrier states, not species heads — `egg07_pale_needle`'s
   nine "missing" entries are almost certainly noise, and are the clearest example of why item 1
   matters.

## Recommended order — all three steps now done, outcome above

1. ✅ Enabled-species roster checked. `hamster`/`kangaroo`/`monkey` are not species; 9 gaps → 7.
2. ✅ Rendered — and the render was wrong, which is how the tool bug surfaced. Fixed, then re-run.
3. ✅ Measured instead of eyeballed. Uniform ~2px vox penalty. **Nothing to fix.**

**The standing lesson for next time:** when a render says the art is broken, check that the
*renderer* drew a whole body first. `humanoid_base` now refuses partial bodies, so this specific
trap is closed — but the general form is not, and the same three steps apply to any visual check.
