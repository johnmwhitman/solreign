#!/usr/bin/env python3
"""Apply robotic-AI processing chains to a TTS render, then normalise and encode.

Same split as normalize_vo.py and for the same reason: ffmpeg does the DSP because
this machine's ffmpeg cannot ENCODE mono Vorbis (no libvorbis, and the native encoder
is stereo-only). libsndfile writes the file.

The chains below are the standard toolkit for "computer that talks": ring modulation
for the metallic buzz, bit reduction for digital grit, comb/flanger for the doubled
synthetic timbre, and band-limiting for the sound of a PA rather than a person.
"""
import io
import subprocess
import sys
from pathlib import Path

import soundfile as sf

TARGET_LUFS = -16.0

# Each chain ends in the same loudnorm + 24 kHz resample the voice pack uses.
TAIL = f"loudnorm=I={TARGET_LUFS}:TP=-1.5:LRA=11"

CHAINS = {
    # Ring modulation is THE classic robot voice: amplitude-modulating the signal at an
    # audible frequency adds metallic sidebands. Kept gentle so words stay intelligible.
    "ringmod": "tremolo=f=50:d=0.5",

    # Bit-depth reduction: digital grit, the sound of limited hardware.
    "bitcrush": "acrusher=level_in=1:level_out=1:bits=6:mode=log:aa=1",

    # Comb filtering via a very short echo — a doubled, hollow, synthetic timbre.
    "hollow": "aecho=0.8:0.9:5:0.35,flanger=delay=2:depth=3:speed=0.4",

    # Station PA: band-limited like a speaker, with a little room behind it.
    "pa": "highpass=f=300,lowpass=f=3600,aecho=0.8:0.85:40:0.25",

    # The full stack — ring mod + grit + band-limit. Most obviously "not a person".
    "full": ("tremolo=f=50:d=0.35,"
             "acrusher=level_in=1:level_out=1:bits=7:mode=log:aa=1,"
             "highpass=f=250,lowpass=f=4000,"
             "aecho=0.8:0.88:22:0.22"),
}


def render(src: Path, dst: Path, chain: str) -> bool:
    proc = subprocess.run(
        ["ffmpeg", "-hide_banner", "-loglevel", "error", "-i", str(src),
         "-af", f"{chain},{TAIL}" if chain else TAIL,
         "-ar", "24000", "-f", "wav", "-"],
        capture_output=True,
    )
    if proc.returncode != 0:
        print(f"  FAIL {dst.name}: {proc.stderr.decode()[:140]}")
        return False
    data, rate = sf.read(io.BytesIO(proc.stdout), always_2d=False)
    if data.ndim != 1:
        data = data.mean(axis=1)
    sf.write(str(dst), data, rate, format="OGG", subtype="VORBIS")
    return True


def main() -> int:
    src = Path(sys.argv[1])
    out_dir = Path(sys.argv[2])
    out_dir.mkdir(parents=True, exist_ok=True)
    stem = src.stem.replace("base_", "")

    ok = render(src, out_dir / f"{stem}_00_dry.ogg", "")
    print(f"  {'ok' if ok else 'FAIL'} {stem}_00_dry")
    for name, chain in CHAINS.items():
        dst = out_dir / f"{stem}_{name}.ogg"
        if render(src, dst, chain):
            print(f"  ok {dst.stem}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
