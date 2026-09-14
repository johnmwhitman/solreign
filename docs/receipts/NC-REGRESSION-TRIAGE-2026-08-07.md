# NC-REGRESSION-TRIAGE-2026-08-07

**Date:** 2026-08-07
**Lane:** release/v15-license-gate
**Scope:** 4 CC-BY-NC-SA files that re-entered corpus 08-02

## Files Identified

### 1. fireaxe.rsi
- **Exact path:** `Resources/Textures/Objects/Weapons/Melee/fireaxe.rsi/`
- **meta.json license:** `CC-BY-NC-SA-3.0`
- **Copyright:** "Taken and modified by Taral from goonstation at commit https://github.com/goonstation/goonstation/pull/2816/commits/b99c5dff45a6527bbf698bc00f7d24b8ca75a806"
- **States:** icon, inhand-left (4), inhand-right (4), wielded-inhand-left (4), wielded-inhand-right (4), equipped-BACKPACK, ...

### 2. fireaxeflaming.rsi
- **Exact path:** `Resources/Textures/Objects/Weapons/Melee/fireaxeflaming.rsi/`
- **meta.json license:** `CC-BY-NC-SA-3.0`
- **Copyright:** "Taken and modified by Taral from goonstation at commit https://github.com/goonstation/goonstation/pull/2816/commits/b99c5dff45a6527bbf698bc00f7d24b8ca75a806 and then further modified by EmoGarbage404"
- **States:** (same structure as fireaxe + flaming variants)

### 3. directionalfan.rsi
- **Exact path:** `Resources/Textures/Structures/Piping/Atmospherics/directionalfan.rsi/`
- **meta.json license:** `CC-BY-NC-SA-3.0`
- **Copyright:** "Made by SlamBamActionman"
- **States:** (fan directional states)

### 4. tinyfan.rsi
- **Exact path:** `Resources/Textures/Structures/Piping/Atmospherics/tinyfan.rsi/`
- **meta.json license:** `CC-BY-NC-SA-3.0`
- **Copyright:** "Taken from tgstation at https://github.com/tgstation/tgstation/commit/40d89d11ea4a5cb81d61dc1018b46f4e7d32c62a"
- **States:** (tiny fan states)

## sprite-factory/deliveries Check

**Location checked:** `~/AI/sprite-factory/deliveries/`

**Contents (top-level packages as of 2026-08-07):**
- fit-normalization-20260803/
- solreign-apc-amendment1/
- solreign-apc-v3/
- solreign-apc-v4-reskin/
- solreign-apc-v5/
- solreign-apc-v6/
- solreign-batons/
- solreign-batons-pixellab/
- solreign-bestiary-v2/
- solreign-carp-scroll/
- solreign-changeling-blade/
- solreign-compliance-unit/
- solreign-executive-vendor/
- solreign-fireworks/
- solreign-furniture-originals/
- solreign-gear-block/
- solreign-gear-block-rigged/
- solreign-glow-flora/
- solreign-heaven-chime-rebuild/
- ... (no fireaxe, fireaxeflaming, directionalfan, or tinyfan packages present)

**Finding:** No CC0 replacements for these 4 files exist in `sprite-factory/deliveries/`.

## Per-File Disposition

| File | License | sprite-factory replacement? | Disposition | Reason |
|------|---------|----------------------------|-------------|--------|
| `fireaxe.rsi` | CC-BY-NC-SA-3.0 | No | **keep-with-exception** | No CC0 replacement available in deliveries; vanilla texture must remain to avoid breaking melee weapon rendering. Documented NC regression accepted for this file. |
| `fireaxeflaming.rsi` | CC-BY-NC-SA-3.0 | No | **keep-with-exception** | Same as fireaxe — flaming variant has no CC0 replacement. Keep to preserve visual parity. |
| `directionalfan.rsi` | CC-BY-NC-SA-3.0 | No | **keep-with-exception** | No CC0 fan replacement in deliveries. Atmospheric piping visuals depend on this asset. |
| `tinyfan.rsi` | CC-BY-NC-SA-3.0 | No | **keep-with-exception** | No CC0 replacement. Tiny variant required for compact atmospherics setups. |

## Constraints Honored

- **MUST NOT delete vanilla textures without a wired replacement:** All 4 files retained (no deletion attempted).
- **MUST NOT edit RobustToolbox/:** No edits performed.
- **No push:** This receipt documents state only.

## Receipt location

`/Users/johnwhitman/AI/solreign-trees/release-v15-lane-license/docs/receipts/NC-REGRESSION-TRIAGE-2026-08-07.md`

**Evidence for task-3:** 4 files located + licenses read from meta.json + deliveries checked (no replacements) + per-file dispositions recorded.
