# Receipt: FONT-DELETE — Animal Silence removal + NotoEmoji OFL notice

- **Date:** 2026-07-17
- **Branch:** `chore/font-delete` (from `origin/master`)
- **Source:** orch-ops `docs/STEAM-PREP-AUDIT-2026-07-17.md` §1a (Animal Silence provenance research) + recommendation #1 and #6.
- **Lane rules:** foreground, no merges, no pushes.

## What was deleted

1. **`Resources/Fonts/Animal Silence.otf`** — Chequered Ink (NAL fonts) freeware: *personal use only*; commercial use requires a paid license (£12–150). Not shippable in a paid Steam build. It was unused dead weight (evidence below).
2. **The `AnimalSilence` font prototype** in `Resources/Prototypes/fonts.yml` (was lines 37–39: `id: AnimalSilence`, `path: /Fonts/Animal Silence.otf`).
3. **Attribution/license sidecars:** none exist. Verified — no file in `Resources/Credits/`, no `attributions.yml`, no credits/license text anywhere in the tree mentions Animal Silence, Chequered Ink, NAL, or dafont. Nothing to remove beyond the two items above.

## Zero-reference re-verification (independent, whole tree incl. RobustToolbox submodule, 2026-07-17)

All greps run over the full worktree (only `.git/` excluded); the RobustToolbox engine submodule was checked out and included.

| Grep | Hits |
|---|---|
| `AnimalSilence` (prototype id) | **1** — `Resources/Prototypes/fonts.yml:38` (the definition itself) |
| `Animal Silence` / `animal[ _-]*silence` (case-insensitive) | fonts.yml, the .otf itself, and one prose mention in `docs/steam-release/STEAM-RELEASE-PLAN.md:38` ("resolve or replace" — this change is the resolution; doc line left as-is, historical) |
| `.ftl` rich-text `[font=...]` tags in `Resources/Locale` | **0** matching AnimalSilence/silence |
| `.xaml` / `.xaml.cs` | **0** font references (only unrelated "SilenceCheckBox" atmos-alarm UI code) |
| C# (`Content.Client`/`Server`/`Shared`, engine) | **0** |

Conclusion: nothing resolves the id or the path. Residual risk ~zero — RobustToolbox font prototypes are reachable only by id; a player-typed `[font="AnimalSilence"]` tag simply fails to resolve.

## What was left alone, and why

- **`Resources/Fonts/Boxfont-round/Boxfont Round.ttf` + `BoxRound` prototype** — LEFT IN PLACE. CC0 (in-repo sidecar `Boxfont-round/credits.txt`), so no license risk. **Audit correction:** the audit's "BoxRound is also unused" claim is only true of the *prototype id* (`BoxRound` has zero consumers). The **font file itself IS used** — loaded by direct resource path (`GetFont("/Fonts/Boxfont-round/Boxfont Round.ttf", …)`) in 6 places: `Content.Client/Wires/UI/WiresMenu.cs:148,149,567`, `Content.Client/UserInterface/Systems/Atmos/GasTank/GasTankWindow.cs:93`, `Content.Client/Stylesheets/Sheetlets/WindowSheetlet.cs:107`, `Content.Client/Stylesheets/StyleNano.cs:529`. Deleting the file would break the wires menu, gas-tank window, and Nano stylesheet. Do NOT delete it in any future sweep; the unused `BoxRound` prototype entry could be pruned, but that's cosmetic and out of this lane's scope.
- **`docs/steam-release/STEAM-RELEASE-PLAN.md:38`** — prose mention of Animal Silence ("resolve or replace") left untouched; this receipt is the resolution record.

## NotoEmoji OFL notice (audit recommendation #6)

- **Claim verified against the font's own embedded metadata** (TTF `name` table): nameID 0 = "Copyright 2013, 2022 Google Inc."; nameID 13 = "This Font Software is licensed under the SIL Open Font License, Version 1.1…"; nameID 14 = http://scripts.sil.org/OFL; version 2.001 "Noto Emoji Regular". The OFL claim holds; the notice was indeed missing in-repo.
- **In-repo pattern followed exactly:** every other font family carries its license *inside its own directory* (`NotoSans/LICENSE.txt`, `NotoSansDisplay/LICENSE_OFL.txt`, `RobotoMono/LICENSE.txt`, `Boxfont-round/credits.txt`). There is no in-repo precedent for a notice beside a *loose* top-level font file, so rather than invent a naming convention, NotoEmoji was brought into the pattern:
  - `Resources/Fonts/NotoEmoji.ttf` → `Resources/Fonts/NotoEmoji/NotoEmoji.ttf` (git mv)
  - `Resources/Fonts/NotoEmoji/LICENSE_OFL.txt` added — verbatim copy of `NotoSansDisplay/LICENSE_OFL.txt` (the generic SIL OFL 1.1 text, same license, same upstream project, same file name convention)
  - `fonts.yml` `Emoji` prototype path updated to `/Fonts/NotoEmoji/NotoEmoji.ttf`
- **Move-risk check:** `/Fonts/NotoEmoji.ttf` was referenced in exactly ONE place in the whole tree (`fonts.yml:47`, updated). The `Emoji` prototype id itself has zero consumers (C#/yml/ftl/xaml/engine), so even the id is currently dead — path change cannot break anything. Verified by full build + tests below.

## Verification (Release, all blocking, all green)

| Check | Result |
|---|---|
| `dotnet build -c Release` (full solution) | 50 projects, **0 errors** (57.7 s) |
| `dotnet build Content.Client -c Release` | 19 projects, **0 errors** (fonts are client-loaded) |
| `dotnet test Content.Tests --no-build -c Release` | **Passed: 2401, Failed: 0, Skipped: 3** — exact baseline (2401/3skip) |
| Content.YAMLLinter (Release) | **"No errors found"** (63.2 s) — covers the fonts.yml edit |

## Diff summary

- deleted: `Resources/Fonts/Animal Silence.otf`
- edited: `Resources/Prototypes/fonts.yml` (removed `AnimalSilence` prototype; `Emoji` path updated)
- moved: `Resources/Fonts/NotoEmoji.ttf` → `Resources/Fonts/NotoEmoji/NotoEmoji.ttf`
- added: `Resources/Fonts/NotoEmoji/LICENSE_OFL.txt`, this receipt

**Post-change font gate:** every font under `Resources/Fonts/` is now Apache-2.0 / OFL-1.1 / CC0 with in-repo license evidence beside it. The only unlicensed font in the tree is gone.
