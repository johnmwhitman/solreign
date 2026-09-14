# License map

This file is the index. The legal texts are `LICENSE` (MIT, SOLREIGN + Space Wizards),
`LICENSE.TXT` (verbatim upstream Space Wizards MIT), and the Creative Commons
legal code at https://creativecommons.org/licenses/by-nc-sa/4.0/legalcode.

| What | License | Where the grant lives |
|---|---|---|
| SOLREIGN original C# / YAML (`Content.*/_Solreign`, `Resources/Prototypes/_Solreign`, `Resources/Locale/**/_Solreign*`) | MIT | `LICENSE` |
| Upstream Space Station 14 code | MIT | `LICENSE.TXT` |
| RobustToolbox engine (submodule) | MIT / GPL-3.0 (engine split) | `RobustToolbox/LICENSE-*.TXT` after `git submodule update --init` |
| SOLREIGN-original textures with a sidecar | whatever `meta.json` `"license"` says (this snapshot: 48× CC-BY-SA-3.0, 25× CC0-1.0 under `Resources/Textures/_Solreign`) | each RSI `meta.json` |
| SOLREIGN-original audio | CC-BY-SA-3.0 (declared) | `Resources/Audio/_Solreign/attributions.yml`, `Resources/Audio/_Solreign/Providence/ATTRIBUTION.txt` |
| SOLREIGN screenshots / room render in `docs/media/` | CC-BY-NC-SA 4.0 | this file + `LICENSE` footer |
| Upstream sprites / sounds | CC-BY-SA 3.0 default; some CC-BY-NC* remain | each `meta.json` / `attributions.yml` |

## SOLREIGN-original assets (default)

Unless a sidecar names a different license, SOLREIGN-original art, audio, and
the four files in `docs/media/` are licensed

**CC-BY-NC-SA 4.0** — https://creativecommons.org/licenses/by-nc-sa/4.0/

You may share and adapt them for non-commercial use, with attribution, under
the same license. Commercial use of those assets is not granted.

In-tree sidecars that already say CC-BY-SA-3.0 or CC0-1.0 are left as-is.
Those grants are broader than CC-BY-NC-SA 4.0 and are not narrowed by this map.

## Upstream CC-BY-NC* that still ships

This snapshot did **not** strip upstream non-commercial assets. They keep the
license written on the file. A Wave-7 census of the live game tree listed 67
`meta.json` files still declaring `CC-BY-NC*` (plus audio `attributions.yml`
entries). Paths include, among others:

- `Resources/Textures/Clothing/Uniforms/Jumpsuits/Color/rainbow.rsi/meta.json`
- `Resources/Textures/Mobs/Aliens/Guardians/guardians.rsi/meta.json`
- `Resources/Textures/Mobs/Animals/{possum_old,raccoon,scurret,ferret}.rsi/meta.json`
- a block of drink / toy / melee sprites under `Resources/Textures/Objects/`

If you want a fully commercial fork, replace or drop those files. SOLREIGN's
own `_Solreign` textures in this snapshot declare no NC licenses.

## Dropped for unknown provenance

These 12 concept PNGs had **no** `meta.json` and were removed from the snapshot
rather than shipped under a guessed license:

```
Resources/Textures/_Solreign/EasterEggs/_concepts/feathered_mask_icon.png
Resources/Textures/_Solreign/EasterEggs/_concepts/flight_jacket_icon.png
Resources/Textures/_Solreign/EasterEggs/_concepts/fowl_mask_icon.png
Resources/Textures/_Solreign/EasterEggs/_concepts/gman_briefcase_icon.png
Resources/Textures/_Solreign/EasterEggs/_concepts/liquid_flame_thermos_icon.png
Resources/Textures/_Solreign/EasterEggs/_concepts/moon_sugar_beaker_icon.png
Resources/Textures/_Solreign/EasterEggs/_concepts/nanoware_trenchcoat_icon.png
Resources/Textures/_Solreign/EasterEggs/_concepts/necktie_icon.png
Resources/Textures/_Solreign/EasterEggs/_concepts/pry_bar_icon.png
Resources/Textures/_Solreign/EasterEggs/_concepts/soda_can_icon.png
Resources/Textures/_Solreign/EasterEggs/_concepts/stealth_box_icon.png
Resources/Textures/_Solreign/EasterEggs/_concepts/tactical_lenses_icon.png
```

See `SNAPSHOT-EXCLUSIONS.md` for everything else that never entered this tree.
