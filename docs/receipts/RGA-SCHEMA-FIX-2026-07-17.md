# RGA-SCHEMA-FIX — 2026-07-17

**Lane:** RGA-SCHEMA-FIX (foreground/blocking, content-only — no merges, no pushes).
**Branch:** `fix/rga-schema` off `origin/master`, worktree `~/AI/solreign-trees/rga-schema-fix`.
**Repo:** `johnmwhitman/kolton-ss14` (fork of `space-wizards/space-station-14`).

## TL;DR

Fixed the 8 `attributions.yml` files identified by the CI-triage lane
(`docs/receipts/CI-TRIAGE-2026-07-17.md`, currently on branch `fix/ci-triage`,
not yet on `master`) as the one genuine CI red once GitHub billing (J3) is
restored. Two schema-shape defects, both fixed with the same technique — no
provenance information lost, moved into `copyright` instead:

1. **`source` prose instead of URL/"NA"** (6 files, 37 entries) — Solreign's
   in-house-generation convention records provenance as descriptive text
   (`"In-house generation, <date>. See <path>/PROVENANCE.md. ..."`), which the
   schema's `Url` validator rejects (only a bare URL or the literal `"NA"`
   passes). Fix: the full provenance sentence was appended to `copyright`
   (prefixed `Provenance:`), and `source` was set to `"NA"`.
2. **`license` prose instead of enum value** (2 files, 3 entries) — the Sonniss
   GDC Bundle License entries wrote a full descriptive sentence into `license`
   instead of picking the schema's `Custom` enum value (which the schema's own
   comment says "implies that the license is described in the copyright
   field"). Fix: the descriptive license sentence was appended to `copyright`
   (prefixed `License:`), and `license` was set to `"Custom"`.

Result: **88/88 `attributions.yml` files pass** the RGA schema validator
(reimplemented locally with `yamale` + the repo's own `rga_validators.py`,
same method the CI-triage lane used — Actions itself still can't run while
J3/billing is unresolved).

## Files fixed (8/8)

| File | Entries fixed | Defect class |
|---|---|---|
| `Resources/Textures/_Solreign/attributions.yml` | 11 | source prose → NA |
| `Resources/Textures/_Solreign/Parallaxes/attributions.yml` | 9 | source prose → NA |
| `Resources/Textures/Tiles/attributions.yml` | 1 | source prose → NA |
| `Resources/Textures/Tiles/Planet/Concrete/attributions.yml` | 1 | source prose → NA |
| `Resources/Textures/Parallaxes/attributions.yml` | 1 | source prose → NA |
| `Resources/Audio/_Solreign/attributions.yml` | 14 | source prose → NA |
| `Resources/Audio/Effects/attributions.yml` | 1 (`drop.ogg`) | license prose → Custom |
| `Resources/Audio/Effects/Weather/attributions.yml` | 2 (`rain2.ogg`, `rain_heavy.ogg`) | license prose → Custom |

40 entries fixed total (37 source, 3 license). Every fix was a literal,
scripted `str.replace` verified to match exactly once per entry before being
applied (`git diff --stat`: 8 files changed, 80 insertions(+), 80
deletions(-) — 2 lines touched per entry, no incidental changes).

## Schema reference

`RobustToolbox/Schemas/rga.yml` (validator `RobustToolbox/Schemas/rga_validators.py`):

```yaml
attribution:
  files: list(str())
  license: license()
  copyright: str()
  source: url()
```

- `license()` — must exactly match one of a fixed enum (`CC-BY-3.0`,
  `CC-BY-4.0`, `CC-BY-SA-3.0`, `CC-BY-SA-4.0`, `CC-BY-NC-3.0`, `CC-BY-NC-4.0`,
  `CC-BY-NC-SA-3.0`, `CC-BY-NC-SA-4.0`, `CC-BY-ND-3.0`, `CC-BY-ND-4.0`,
  `CC-BY-NC-ND-4.0`, `CC0-1.0`, `MIT`, `Custom`). `Custom` "implies that the
  license is described in the copyright field" (schema comment).
- `url()` — must be `"NA"` (literal) or pass `validators.url(value)`.
- `copyright` — unconstrained `str()`, prose freely allowed. This is where
  all displaced provenance/license detail was moved, per the schema's own
  `Custom` convention.

## Provenance preserved — before/after example

Source-prose fix (`Resources/Textures/_Solreign/attributions.yml`,
`scarlet.rsi`):

```diff
-  copyright: "Solreign (AI-generated, human-reviewed). Black cocker spaniel station pet: ...(deterministic pixel-erasure pass, no regeneration)."
-  source: "In-house generation, 2026-07-11. See assets/sprites-batch1/PROVENANCE.md. Wired: Resources/Prototypes/_Solreign/Entities/delighters/scarlet.yml (MobScarlet)."
+  copyright: "Solreign (AI-generated, human-reviewed). Black cocker spaniel station pet: ...(deterministic pixel-erasure pass, no regeneration). Provenance: In-house generation, 2026-07-11. See assets/sprites-batch1/PROVENANCE.md. Wired: Resources/Prototypes/_Solreign/Entities/delighters/scarlet.yml (MobScarlet)."
+  source: "NA"
```

License-prose fix (`Resources/Audio/Effects/Weather/attributions.yml`, `rain2.ogg`):

```diff
-  copyright: '"Natural Environments" by Varazuvi of SONNISS.com.'
-  license: "Sonniss GDC Bundle License (commercial use permitted, no attribution required; ...). Verified 2026-07-16 via https://sonniss.com/gdc-bundle-license/. Filename corrected from stale rain2.wav to the actual on-disk rain2.ogg (Vorbis, referenced in SoundCollections/weather.yml)."
+  copyright: '"Natural Environments" by Varazuvi of SONNISS.com. License: Sonniss GDC Bundle License (commercial use permitted, no attribution required; ...). Verified 2026-07-16 via https://sonniss.com/gdc-bundle-license/. Filename corrected from stale rain2.wav to the actual on-disk rain2.ogg (Vorbis, referenced in SoundCollections/weather.yml).'
+  license: "Custom"
   source: https://gdc.sonniss.com
```

No text was deleted anywhere — every displaced `source`/`license` sentence
was concatenated onto the end of `copyright` verbatim (a space + a
`Provenance:`/`License:` lead-in was the only addition). `files:` values are
untouched in every entry.

## CONCURRENT-LANE OVERLAP FLAG

**`Resources/Audio/Effects/attributions.yml` is also being edited by the
`chore/audio-batch0` lane** (worktree `~/AI/solreign-trees/audio-batch0`,
uncommitted at time of writing). Checked all 8 target files against every
other active worktree's uncommitted changes — this is the **only** overlap.

Audio-batch0's diff to this file touches the `balloon-pop`, `sadtrombone.ogg`,
`chime.ogg`, `tesla.ogg`, `tesla_collapse.ogg`, and `hith_kick.ogg` →
`hit_kick.ogg` entries (swapping several NC sources for CC0 free-swap /
in-house replacements). **This lane's fix touches only the `drop.ogg` entry**
(license prose → `Custom`), which audio-batch0's diff does not touch. The two
diffs are in different, non-adjacent regions of the file (my change is at the
original lines 86–89; audio-batch0's nearest touched region is lines 182+),
so a mechanical 3-way merge is likely to succeed cleanly, but **flagging per
instructions rather than assuming** — recommend the orchestrator do a
manual `git diff` review of this one file at merge time before trusting an
automatic merge, since both branches touch `Resources/Audio/Effects/attributions.yml`.

No other of the 8 target files overlap any other active worktree (checked
`git status --short` across all worktrees under `~/AI/solreign-trees/` for
`attributions.yml` changes).

## Verification performed

- **RGA schema validator** (yamale + `rga_validators.py`, same method as the
  CI-triage lane, run in a fresh venv with `yamale`, `validators`, `pyyaml`
  installed): **88/88 `Resources/**/attributions.yml` files pass** (was
  80/88 before this lane; the exact 8 failing files and error messages
  matched the CI-triage receipt's predictions before the fix, confirming no
  drift between the receipt and current `master`).
- **YAML syntax** (`python3 -c "import yaml; yaml.safe_load(open(f))"` on all
  8 changed files): clean.
- **YAML Linter** (`dotnet run --project Content.YAMLLinter -c Release`):
  `No errors found in 77889 ms.` — clean build, only pre-existing unrelated
  `CS0618` obsolete-API warnings in `Content.IntegrationTests` (not touched by
  this lane).
- **Content.Tests full suite** (`dotnet test Content.Tests/Content.Tests.csproj -c Release`):
  `Total tests: 2458 / Passed: 2455 / Skipped: 3 / Failed: 0` — matches the
  project's ~2455/3 baseline exactly, no regressions. Content is
  `attributions.yml`-only, no C# under test references these specific files
  directly (confirmed via `grep -rln "attributions.yml" --include="*.cs" .`
  before starting — the only two hits, `SeasonLedgerSystem.Ceremony.cs` and
  `CreditsWindow.xaml.cs`, don't touch the 8 changed files), so this run is a
  no-regression confirmation rather than a targeted assertion.

## What this branch changes

8 `attributions.yml` files under `Resources/Textures/` and `Resources/Audio/`
— no code changes, no workflow changes (the CI-triage lane's workflow
consolidation is a separate branch, `fix/ci-triage`, not touched here).
