#!/usr/bin/env python3
"""Loudness-normalise mono Vorbis VO to the SOLREIGN voice-pack target (I=-16 LUFS).

Why not the pipeline's `ffmpeg -c:a libvorbis`: this machine's ffmpeg has no
libvorbis, and its native `vorbis` encoder is STEREO-ONLY while the voice pack is
mono 24 kHz. Encoding through it produces zero-byte files. libsndfile (via the same
soundfile venv gemini_audio.py already uses to write these files) handles mono Vorbis
fine, so measurement stays with ffmpeg's EBU R128 and only the gain + re-encode moves.

Linear gain to a measured integrated loudness is what loudnorm's linear mode does;
a true-peak guard keeps it from clipping.
"""
import subprocess
import sys
from pathlib import Path

import numpy as np
import soundfile as sf

TARGET_LUFS = -16.0
TRUE_PEAK_CEILING_DB = -1.5


def measure_lufs(path: Path) -> float:
    out = subprocess.run(
        ["ffmpeg", "-hide_banner", "-nostats", "-i", str(path),
         "-af", "ebur128=framelog=quiet", "-f", "null", "-"],
        capture_output=True, text=True,
    ).stderr
    marker = "Integrated loudness"
    idx = out.rfind(marker)
    if idx == -1:
        raise RuntimeError(f"no loudness reading for {path}")
    for line in out[idx:].splitlines():
        if line.strip().startswith("I:"):
            return float(line.split(":")[1].replace("LUFS", "").strip())
    raise RuntimeError(f"no I: line for {path}")


def main() -> int:
    src_dir, dst_dir = Path(sys.argv[1]), Path(sys.argv[2])
    dst_dir.mkdir(parents=True, exist_ok=True)
    failures = 0

    for src in sorted(src_dir.glob("*.ogg")):
        measured = measure_lufs(src)

        # ffmpeg does the DSP -- the EXACT loudnorm the voice-pack pipeline specifies,
        # which limits rather than applying flat gain, so it reaches target on
        # wide-dynamic-range lines that pure gain cannot. Only the ENCODE moves to
        # libsndfile, because ffmpeg here cannot write mono Vorbis.
        proc = subprocess.run(
            ["ffmpeg", "-hide_banner", "-loglevel", "error", "-i", str(src),
             "-af", f"loudnorm=I={TARGET_LUFS}:TP={TRUE_PEAK_CEILING_DB}:LRA=11",
             # loudnorm resamples to 192 kHz internally; without this the output
             # carries that through and the file is 8x the voice pack's 24 kHz.
             "-ar", "24000",
             "-f", "wav", "-"],
            capture_output=True,
        )
        if proc.returncode != 0:
            print(f"FAIL {src.name}: {proc.stderr.decode()[:120]}")
            failures += 1
            continue

        import io
        data, rate = sf.read(io.BytesIO(proc.stdout), always_2d=False)
        if data.ndim != 1:
            data = data.mean(axis=1)

        dst = dst_dir / src.name
        sf.write(str(dst), data, rate, format="OGG", subtype="VORBIS")
        after = measure_lufs(dst)
        flag = "" if abs(after - TARGET_LUFS) <= 1.0 else "   <-- OFF TARGET"
        print(f"{src.name}: {measured:+.1f} -> {after:+.1f} LUFS{flag}")
        if abs(after - TARGET_LUFS) > 1.0:
            failures += 1

    print(f"\n{'FAILURES: ' + str(failures) if failures else 'all files on target'}")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
