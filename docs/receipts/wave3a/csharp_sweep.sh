#!/bin/bash
# WAVE 3A C# hardcoded-path sweep -- the killsign lesson: YAML grep alone is not enough,
# some RSI paths/state names are referenced directly from C# (SpriteSystem.LayerSetRsi,
# hardcoded state lookups, etc). Grep every one of the 25 target asset paths + their state
# names across all Content.* C# source (not just Prototypes/Maps).
set -e
cd "$(dirname "$0")/../../.."
REPO=$(pwd)
echo "repo: $REPO"

PATHS=(
  "Objects/Power/powersink"
  "Objects/Tools/handdrill"
  "Objects/Tools/handdrilldiamond"
  "Objects/Fun/Balls/football"
  "Objects/Fun/Balls/basketball"
  "Objects/Fun/Balls/beach_ball"
  "Objects/Fun/Balloons/nanotrasen"
  "Objects/Fun/Balloons/corgi"
  "Objects/Fun/Balloons/syndicate"
  "Objects/Fun/rubber_chicken"
  "Objects/Fun/pondering_orb"
  "Objects/Fun/clownrecorder"
  "Objects/Fun/Plushies/lamp"
  "Objects/Fun/capgun"
  "Objects/Fun/Foam/foam_crossbow"
  "Objects/Fun/Foam/foam_grenade"
  "Objects/Fun/Foam/foam_blade"
  "Objects/Weapons/Guns/Basic/energy_crossbow"
  "Objects/Weapons/Melee/machete"
  "Objects/Weapons/Melee/singularityhammer"
  "Objects/Weapons/Melee/chainsaw"
  "Objects/Weapons/Melee/cult_halberd"
  "Objects/Weapons/Melee/mjollnir"
  "Objects/Weapons/Melee/cutlass"
  "Objects/Weapons/Melee/incomplete_bat"
)

CS_DIRS="Content.Client Content.Server Content.Shared Content.IntegrationTests Content.Server.Database Content.Shared.Database"

echo "=== per-path RSI reference hits (path substring) ==="
for p in "${PATHS[@]}"; do
  hits=$(grep -rnE "$p" $CS_DIRS --include="*.cs" 2>/dev/null || true)
  if [ -n "$hits" ]; then
    echo "--- $p ---"
    echo "$hits"
  fi
done

echo ""
echo "=== bare state-name hardcodes worth a human eyeball (bolt-open/closed/capbullet/storage/foambox/primed/foam_icon/foam_storage) ==="
for s in "bolt-open" "bolt-closed" "capbullet" "foambox" "primed" "foam_icon" "foam_storage"; do
  hits=$(grep -rn "\"$s\"" $CS_DIRS --include="*.cs" 2>/dev/null || true)
  if [ -n "$hits" ]; then
    echo "--- \"$s\" ---"
    echo "$hits"
  fi
done
echo "sweep done"
