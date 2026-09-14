# PROVIDENCE voice candidates — PARKED, awaiting John's pick

Parked 2026-07-22. John asked for a more robotic female voice, GLaDOS-adjacent, then
parked the decision. Everything needed to finish is here.

Play with `ffplay`, **not** `afplay` — macOS CoreAudio does not decode Vorbis and
`afplay` exits instantly without playing:

```
for f in docs/voice-candidates/rob/*.ogg; do echo "▶ $(basename $f)"; \
  ffplay -autoexit -nodisp -loglevel error "$f"; done
```

Every candidate speaks the same line — *"Maintenance requests are reviewed quarterly.
The quarter has not been specified."* — normalised to the same loudness, so nothing
wins by being louder. All verified vorbis / 24 kHz / mono, -15.8..-17.2 LUFS.

## What is here

**`bake/`** — the first pass, natural (unprocessed) voices.
- 8 voices, identical style prompt, voice the only variable: Kore (current), Aoede,
  Leda, Autonoe, Vindemiatrix, Erinome, Charon, Enceladus. "female" was dropped from
  the style string here so the prompt did not fight the voice.
- 5 tone/pace variants on Kore: slow, fast, flat, warm, whisper.

**`rob/`** — the robotic pass. Base prompt is the `ROB` string used in the session:
> synthetic female artificial intelligence, deliberately robotic and machine-like,
> flat affectless monotone with almost no emotional inflection, precise clipped
> diction, unnervingly even pacing, the calm of something that is not alive

- `Kore_00_dry` — robotic prompt, NO processing. The baseline to compare against.
- `Kore_ringmod` / `_bitcrush` / `_hollow` / `_pa` / `_full` — the five chains.
- `Erinome_full`, `Vindemiatrix_full`, `Aoede_full` — same chain, other voices, so the
  voice can be chosen separately from the processing.

**`robotize.py`** — the chain definitions (authoritative; read it for exact filters).
**`normalize_vo.py`** — loudnorm → libsndfile encode.

## Two pipeline facts that will bite whoever picks this up

1. **ffmpeg here cannot ENCODE mono Vorbis.** No `libvorbis`, and the native `vorbis`
   encoder is stereo-only — it silently produces zero-byte files. Both scripts do the
   DSP in ffmpeg and the encode in libsndfile for exactly this reason.
   `gen_voice_pack.sh`'s `-c:a libvorbis` does not work on this machine.
2. **`loudnorm` resamples to 192 kHz internally.** Without an explicit `-ar 24000` the
   output is 8x the voice pack's rate.
3. **Do not fan TTS calls out concurrently.** Rendering 13 lines across two parallel
   lanes produced 2 spurious failures; both succeeded on retry. `gemini_audio.py`'s own
   error path carries a `quota/rate limit` hint, which is what this is. Render in ONE
   sequential loop — re-rendering all 18 lines is ~4 minutes serially and that is fine.
   (The `[warn] ffmpeg failed: Encoder not found` line is NOT a failure: it is the
   expected fallback to libsndfile per point 1. The tool still exits 0. Verified.)

## To finish once John picks

1. Re-render all 18 idle lines with the chosen voice + style, from
   `Resources/Audio/_Solreign/Providence/lines_2026-07-22.tsv` (new 10) and the ops
   repo's `assets/voice-pack/lines.tsv` (original 8 — note that file lives in the OPS
   repo, not this one, despite three code comments citing it).
2. Apply the chosen chain, normalise, verify format + loudness.
3. **Record the exact voice, style string and filter chain in
   `Resources/Audio/_Solreign/Providence/ATTRIBUTION.txt`.** The original pack's
   persona string was never written down, which is the whole reason this bakeoff had
   to reconstruct it. Do not repeat that mistake.
4. Wire the chain into `gen_voice_pack.sh` so it is reproducible.

## Decision recorded

We are **not** cloning GLaDOS. It is a specific protected performance (Ellen McLain)
and this is a public server. The bakeoff reproduces the *technique* — flat affect plus
ring-mod/bit-crush/band-limit processing — which is the standard toolkit for a machine
voice and is what actually does the work.

Worth remembering: what makes that character land is the *writing* — a sincere
corporate line followed by a turn that reveals contempt, delivered without a change in
tone. The existing pack already does this ("the suggestion box is a shredder with
excellent branding"). Processing only sells that it is a machine.
