# TRIPLE-MERGE receipt battery — 2026-07-16

**Verdict: GREEN** (all six gates clean)

- **Tree:** `/Users/johnwhitman/AI/solreign-trees/preview-triple` (detached HEAD)
- **HEAD:** `7411740654` — "Merge feat/regen-final @ fed6001425" (preview merge of
  `feat/fx-w5-confidentiality` + `feat/regen-wave6` + `feat/regen-final` onto GAME master `da83722e43`)
- **Run:** 2026-07-16, verification agent, all commands foreground/blocking, sequential (no concurrent dotnet invocations)

## 1. Build

```
dotnet build SpaceStation14.slnx -c Release
```

**Result: clean — 50 projects, 0 errors, 8 warnings** (all pre-existing NU1903/NU1510 NuGet
advisory/prune warnings from RobustToolbox and legacy pinned packages; zero compiler errors).
Build was incremental (tree already compiled at this HEAD; completed in ~3 s, up-to-date).

## 2. Full unfiltered unit suite

```
dotnet test Content.Tests -c Release --no-build
```

**Result: Passed! Failed: 0, Passed: 2216, Skipped: 3, Total: 2219** (3 s)

Skips (all known/benign): `TestAlertManager`, `WindowsCleanupFailureResidueRetainsRestrictedAcl`,
`WindowsStageAclAllowsOnlyCurrentServiceIdentity` (Windows-only ACL tests on macOS).

## 3. Solreign integration suite

```
dotnet test Content.IntegrationTests -c Release --no-build --filter "FullyQualifiedName~Solreign"
```

**Result: Passed! Failed: 0, Passed: 302, Skipped: 1, Total: 303** (4 m 30 s)

Skip (known): `PersistentBlockSurvivesRoundRestartAndPreventsOffer`.

## 4. Map-load suite

```
dotnet test Content.IntegrationTests -c Release --no-build --filter "FullyQualifiedName~GameMapsLoadableTest"
```

(Command per prior receipts, e.g. `LICENSE-REGEN-WAVE3B-2026-07-15.md`, `audio-w1/AUDIO-W1-2026-07-16.md`.)

**Result: Passed! Failed: 0, Passed: 246, Skipped: 0, Total: 246** (1 m 22 s)

## 5. YAML linter

```
dotnet run --project Content.YAMLLinter -c Release
```

**Result: "No errors found in 29390 ms."** (only pre-existing NU1510 NuGet warnings during build)

## 6. NC license census

```
grep -rl '"license": *"CC-BY-NC' Resources/Textures --include=meta.json | wc -l
```

**Result: 0** ✅

Cross-checks:
- Broad sweep `grep -rl 'CC-BY-NC' Resources --include=meta.json | wc -l` → 64, but every hit is a
  `"copyright"` attribution note documenting *replaced* NC art (e.g. "replaces CC-BY-NC art",
  "Replaces the /tg/station-derived CC-BY-NC-SA-3.0 original") — none are license fields.
- Tally of all `"license"` field values across `Resources/**/meta.json`: only `CC-BY-SA-3.0`,
  `CC-BY-SA-4.0`, and `CC0-1.0`. Zero NC licenses remain.

## Summary

| Gate | Command | Result |
|---|---|---|
| Build | `dotnet build SpaceStation14.slnx -c Release` | 0 errors |
| Units | `dotnet test Content.Tests -c Release --no-build` | 2216/0/3 skip |
| Solreign integration | `--filter "FullyQualifiedName~Solreign"` | 302/0/1 skip |
| Map loads | `--filter "FullyQualifiedName~GameMapsLoadableTest"` | 246/0/0 |
| YAML linter | `dotnet run --project Content.YAMLLinter -c Release` | No errors found |
| NC census | grep meta.json license fields | **0** |

**Overall: GREEN.** Preview merge `7411740654` passes the full canonical receipt battery.
Receipt written by the verification agent; not committed (orchestrator adopts).
