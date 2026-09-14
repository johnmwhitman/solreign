# Replacing a full collection — what it actually costs

_Measured against this checkout on 2026-08-01, not estimated. Every number below is from a scan of
`Resources/Textures/**/*.rsi/meta.json` (3,086 RSIs, 24,560 states) plus the observed PixelLab
burn rate from the three delighter sprites._

## The one number that governs everything

Sprite Factory's `pixellab-intake` emits **`icon` + `inhand-left` + `inhand-right`** and nothing
else. So the question "can we replace this collection?" reduces to: **are the rest of its states
things we can *derive* from one icon, or are they bespoke art?**

- `inhand-*` — derived by `spritefactory rig` at measured hand anchors. Free.
- `equipped-*` — derived by `spritefactory worn` (`measure_worn_profile` → `stamp` →
  `synthesize_sheet`). Free. **⚠️ assumption, see caveats.**
- anything else (`open`/`closed`/`welded`, damage stages, animation frames, `-on`/`-off`) — bespoke.
  Not derivable. Each is its own generation.

**1,092 of 3,086 RSIs (35%) are derivable from a single icon.** The other 65% are machines, doors,
furniture, and multi-stage objects that need real per-state art.

## Budget

| | |
|---|---|
| PixelLab pool remaining | **3,896 generations** |
| Measured burn | **~40 generations per shipped icon** (4,015.9 → 3,895.9 across 3 sprites) |
| Budget | **≈ 97 icons** |

## The table

Collections with ≥25 derivable RSIs, cheapest first. "Synth states" is art we get for free.

| Collection | RSIs | Doable | % | Synth states | Generations | % of pool | Verdict |
|---|---:|---:|---:|---:|---:|---:|---|
| **Clothing/Eyes** | 33 | **32** | **96%** | 139 | 1,280 | 33% | ✅ fits, best ratio |
| Clothing/Mask | 41 | 32 | 78% | 178 | 1,280 | 33% | ✅ fits |
| Clothing/Shoes | 48 | 41 | 85% | 133 | 1,640 | 42% | ✅ fits |
| Objects/Weapons | 241 | 47 | 19% | 131 | 1,880 | 48% | ⚠️ fits, but only 19% of the collection |
| Objects/Consumable | 318 | 47 | 14% | 18 | 1,880 | 48% | ⚠️ fits, but only 14% |
| **Clothing/Back** | 70 | **64** | **91%** | 190 | 2,560 | 66% | ✅ fits, highest visibility |
| Objects/Fun | 121 | 84 | 69% | 218 | 3,360 | 86% | ✅ fits, no headroom |
| Clothing/Neck | 105 | **101** | **96%** | 241 | 4,040 | 104% | ✖ just over — one refill |
| Clothing/OuterClothing | 146 | 122 | 83% | 510 | 4,880 | 125% | ✖ two cycles |
| Clothing/Uniforms | 188 | 164 | 87% | 622 | 6,560 | 168% | ✖ two cycles |
| Clothing/Head | 258 | 179 | 69% | 545 | 7,160 | 184% | ✖ two cycles |

**Clothing is the whole opportunity.** It is 69–96% derivable because a garment is an icon plus
worn/held views of the same garment. Structures are 0% — `Structures/Doors` is 81 RSIs and 1,181
states with *zero* derivable, because a door is an animation, not a picture.

## Recommendation

**Start with `Clothing/Eyes` — 32 items, 1,280 generations, a third of the pool.** Highest
derivable ratio on the board (96%), small enough to finish in one sitting, and glasses sit on a
character's face in every single interaction. If it lands, **`Clothing/Back` (64 items, 66% of
pool)** is the follow-up: backpacks are the most-looked-at item in the game after the uniform.

Do **not** start with `Objects/Consumable` or `Objects/Weapons`. They look attractive on RSI count
and they are traps: 318 and 241 RSIs of which only 47 are doable, so you would ship a collection
that is 86% untouched and looks *inconsistent* rather than *new*.

## Caveats — read before committing the pool

1. 🔴 **The `equipped-*` synthesis path is an ASSUMPTION, not a verified result.** `worn.py` has the
   machinery (`measure_worn_profile`, `stamp`, `synthesize_sheet`, mirror rules) and the `worn` CLI
   ran clean against this checkout — but it wrote **0 previews**, because the delivery I gave it had
   no worn states. **Before spending 1,280 generations, take ONE existing pair of glasses through
   icon → `worn` → `validate` and confirm equipped states come out.** If they do not, every
   percentage in this table drops to the icon+inhand subset and the economics change completely.
2. 🔴 **Consistency is the real risk, not cost.** 32 independently generated pairs of glasses will
   not look like a set. PixelLab has `--palette-lock` and `fetch-state` (variant-of-an-existing-
   character) for exactly this; the collection must be generated as *variants of one seed*, not as
   32 unrelated prompts. This is the thing most likely to make a full-collection swap look worse
   than vanilla.
3. **40 generations/icon is an upper bound** measured on 3 sprites with ~50-candidate boards.
   Smaller boards would cut it materially. Re-measure after the first five.
4. **Replacing upstream art changes what every player sees**, and both gates
   (`SolreignSpriteStateExistsTests`, `SolreignOrphanReachabilityTest`) plus the review queue apply.
   A collection swap is a review-queue event, not a drive-by.
5. The licensing upside is real but is **not** what this table measures: replacing CC-BY-NC assets
   retires attribution debt. Cross-reference `LICENSE-CENSUS-W7-2026-07-16.md` before picking, in
   case a debt-carrying collection deserves priority over the best-ratio one.
