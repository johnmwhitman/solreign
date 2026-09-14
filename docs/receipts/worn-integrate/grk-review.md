I'll pull the full prompt and the complete diff so the worn-sprite review covers every entity, not just the truncated section.Continuing through the truncated middle of the diff so the review covers every entity change.Checking a few local parent prototypes so the sabre Clothing and Greenshield patterns match real SS14 idiom.## Verdict: Looks correct

No YAML errors, no duplicate component types in a single `components` list, no Sprite/Clothing desync that would leave worn art on vanilla while ground art is Solreign (or the reverse).

### (1) YAML / inheritance
Overrides are idiomatic child-entity field merges: only `Clothing.sprite` (or Sprite/Item) restated; slots and combat stats stay on parents. Indentation and field names (`sprite`, `storedSprite`, `slots`, `quickEquip`) are valid.

### (2) Sprite ↔ Clothing pairing
| Pattern | Entities |
|---|---|
| Clothing-only fix (Sprite already Solreign) | eggs 07/10/16/18/20, pry bar, necktie, trenchcoat, lenses, jackets, masks, heavenzone cloak |
| Sprite + Clothing (+ Item where needed) | Halcyon hoverpack, Verge satchel, Greenshield uniform/armor, navy sabre |
| Sprite-only (not worn gear) | founders seal, foam crossbow (`Item.sprite` for held/inv) |

**Intentional leftover:** `SolreignNavyOfficerSabre` still uses vanilla `captain_sabre_storage_64x.rsi` for bag `storedSprite` — documented, not a worn mismatch.

### (3) Balance / mechanics
- Greenshield parents (`ClothingUniformJumpsuitDetective`, `ClothingOuterArmorBasicSlim`) keep armor/stats; starting gear only swaps entity IDs.
- Foam crossbow: tint → baked RSI; no stats change.
- Satchel storage grid unchanged.
- **Sabre:** new `Clothing` with `slots: [Belt]` and `quickEquip: false` enables belt equip (BaseSword had none). Matches captain-sabre idiom for `equipped-BELT` art — intentional, not a damage/reflect buff.

### (4) Idiom
Greenshield entities and sabre Clothing match surrounding Solreign reskin style (Sprite + storedSprite + Clothing.sprite, thin parents, suffixes).

**Non-blocking notes only:** heavenzone cloak applies `color` on Sprite but not Clothing (possible worn tint gap); egg18 oversized HELMET and dark Greenshield at 32px are already owner-eye flags. No blockers.
