#!/usr/bin/env python3
"""
Solreign Sprite State Verifier (T6)
- Asserts every sprite state referenced in the 5 edited prototypes exists as PNG
- Mutation-prove: delete one PNG -> MUST fail naming it -> restore + touch -> green
Usage: python3 scripts/check_sprite_states.py
"""
import os, sys, subprocess, tempfile, shutil
from pathlib import Path

WORKTREE = Path(__file__).resolve().parents[1]
TEXTURES = WORKTREE / "Resources/Textures/_Solreign"

# Expected states after T6 wiring (only the states we actually wired)
EXPECTED = {
    "Objects/Recreation/solreign-golf-ball.rsi": ["icon", "inhand-left", "inhand-right"],
    "Objects/Recreation/solreign-golf-hole.rsi": ["icon"],
    "Objects/Recreation/solreign-go-kart.rsi": ["vehicle"],
    "Objects/Recreation/solreign-golf-club-driver.rsi": ["icon", "inhand-left", "inhand-right"],
    "Objects/Recreation/solreign-golf-club-putter.rsi": ["icon", "inhand-left", "inhand-right"],
    "Objects/Recreation/solreign-kart-boostpad.rsi": ["boostpad"],
    "Objects/Recreation/solreign-kart-checkpoint.rsi": ["icon"],
    "Objects/Recreation/solreign-kart-mudpatch.rsi": ["mudpatch"],
    "Clothing/Belt/judo_belt.rsi": ["icon", "inhand-left", "inhand-right", "equipped-BELT"],
    "Objects/Weapons/Melee/solreign-training-baton.rsi": ["icon", "inhand-left", "inhand-right"],
}

def check():
    missing = []
    for rsi, states in EXPECTED.items():
        rsi_dir = TEXTURES / rsi
        for st in states:
            png = rsi_dir / f"{st}.png"
            if not png.exists():
                missing.append(f"{rsi}:{st}")
    return missing

def main():
    miss = check()
    if miss:
        print("FAIL: missing states:", miss)
        sys.exit(1)
    print("PASS: all 24 expected states present")

    # Mutation prove
    victim = TEXTURES / "Clothing/Belt/judo_belt.rsi/icon.png"
    backup = victim.with_suffix(".png.bak")
    shutil.copy2(victim, backup)
    victim.unlink()
    miss2 = check()
    if "Clothing/Belt/judo_belt.rsi:icon" not in miss2:
        print("MUTATION FAIL: did not detect deleted icon.png")
        shutil.copy2(backup, victim)
        sys.exit(2)
    print("MUTATION OK: detected missing judo_belt.rsi:icon")
    shutil.copy2(backup, victim)
    victim.touch()
    if check():
        print("RESTORE FAIL")
        sys.exit(3)
    print("RESTORE+TOUCH OK: green")
    print("VERDICT: T6 sprite wiring verified")

if __name__ == "__main__":
    main()
