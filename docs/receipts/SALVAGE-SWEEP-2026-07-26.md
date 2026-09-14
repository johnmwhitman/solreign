# Salvage sweep receipt — 2026-07-26 (night)

Scope: every unmerged origin branch except `auto-value/*` (97, excluded: 66 carry a stale
shadow.yml that would regress the lungs fix; out of goal) and `upstream/*` (vanilla).
Method: mechanical dossier (merge-base age, `git cherry` unique-commit count, net
three-dot diff vs master) → grk triage → hand verification of every verdict acted on →
merge/cherry → standing gate + full integration suite. Fleet output was treated as a
draft throughout; two of grk's initial top-3 were falsified on inspection.

## MERGED (all gated: unit 2573/0, parity 1/0, linter clean)

| what | from | how | note |
|---|---|---|---|
| FX anchor key boxing-free `Equals` + 132-line tests | `fix/codex-fx-anchor-allocation` | full merge | 3 files +257/−1, surgical |
| Diagnostics v2: sample-gated metrics, `Subs.CVar` lifecycle, +310-line tests | `cdx/system-recovery-v2` | merge, conflicts→MASTER on both Enabled CVars | branch predated the 07-24 activation ruling; its tests asserted the dead policy — 3 tests reconciled to `true, SERVER` |
| Compliance hunter population scaling (+ integration test) | `agx/chain-triage` | path cherry (5 files) | the ONE clean delta of a 31-commit chain |
| World-feedback FX v1.1: observe gate, dispatch rules, metrics, truth-table tests | `feat/codex-game-world-feedback-fx-v1-1` | selective merge, noise stripped | integration test reconciled to activated defaults; observe ships dormant; delivery NOT re-darkened |

## DEAD — verified, do not re-triage

| branch | why (verified how) |
|---|---|
| `wf/rootcause-198` | its 75 files are mostly the never-merged sr-w-033..057 stub chain — merging would ADD the do-not-merge debt; the 4 files master has are the already-salvaged systems (checked file-by-file with `git cat-file -e`) |
| `agx/chain-triage` (the chain itself) | grk deep review: 20+ no-caller stub systems, crew-goals hardcoded `activePop=10`, YAML `departmentGoal` kind with no serializer in tree, fleet CVar re-darkening. Cherry taken; chain dropped |
| `feat/codex-game-antag-rotation-v1` | antag rotation already wired on master (standing verdict) |
| `cdx/system-recovery` | superseded by v2 (older greenfield dump) |
| `lab/effect-profiler` | tip byte-identical to `feat/effect-profiler-bench` |
| `fix/content-client-compile-and-salvage-load` | packaging fix obsolete — master shipped a fully-gated release today |
| `feat/agx-harvest-oracle-ledger` | week-old base; payload includes committed `diff.txt` and a `solreign_season_ledger.db` binary — contamination |
| `codex/solreign-{activation-contract-reconcile,game-exact-head-qualification-fx,saltern-map-yaml}-20260725` | ALREADY MERGED (empty `master..branch`); §10's triage table is stale for all three |

## PARKED — real content, needs work or a human

| branch | why |
|---|---|
| `art/wire-landed-families` | 144 files of RSI/binaries — art direction is John's call |
| expedition shuttle + command gating (inside `agx/chain-triage`) | MERGE-AFTER-FIXES per review: needs prototypes/map placement/callers before it is honestly shippable |
| `feat/effect-profiler-bench` | lab harness, LOW value; harmless, unmerged |
| `feat/lobby-compliance-welcome` | effectively 1 line of `LobbyGui.xaml` inside a profiler twin; take the line if lobby work ever opens |
| `chore/rt-upgrade-v283`, `codex/solreign-fx-round-lifecycle-20260725` | ENGINE bumps — John's engine-target pick |
| `test/codex-sr-w084-license-regression-gate` | D6: the merge IS John's board decision |
| license-regen family, `salvage/corporate-restyle-b`, records-hud/station-library (schema), unicorn family, v135-wireup (dup), throwaway/staging/steam | standing verdicts unchanged |

## Housekeeping executed

- **39 of 41 listed worktrees pruned** (52 → 13), each re-verified at prune time: clean,
  still on listed branch, tip == origin. `--force` used ONLY because git refuses any
  worktree containing a submodule; the checks above ran first.
- 2 kept: `codex-game-effect-observatory-v2` (6 unpushed commits) and
  `codex-game-operator-show-preflight-v0-20260726` (1) — **both branches pushed to origin
  as-is** (Wave-0 preservation precedent) before deciding anything else about them.
- Untouched on purpose: `~/AI/agx-work/server-sr-w-065` (another lane's),
  `~/AI/Kolton-SS14/server` (34 uncommitted files), detached `/private/tmp` trees.

## Lessons this sweep re-earned

1. **A triage table is stale the moment a salvage wave runs** — three "reviewable"
   §10 branches were already merged; `git log master..branch` before reviewing anything.
2. **Branches outlive policy.** Three separate branches carried dark-by-default CVar
   assertions/defaults that would have silently re-darkened LIVE features had a merge
   taken the branch side. The activation ruling lives on master; conflicts resolve to it.
3. `pgrep -f dotnet | wc -l` is not a quietness check (MCP node processes and the Steam
   launcher match); read %CPU of real build processes.
