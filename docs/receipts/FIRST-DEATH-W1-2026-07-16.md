# Receipt — FIRST-DEATH W1+W2 (core + scene), 2026-07-16

**Lane:** FIRST-DEATH-W1 builder (worktree `~/AI/solreign-trees/first-death-w1`, branch
`feat/first-death-w1` off GAME master `e29185f6a8`).
**Spec:** `~/AI/solreign-trees/orch-ops/docs/specs/FIRST-DEATH-SPEC-2026-07-16-DRAFT.md`
(sections 2, 3, 7, 8 — FD-W1 core + FD-W2 scene ONLY).
**Explicitly NOT built:** FD-W3 (crypt/daemon — no wire change, no daemon code, no
`SolreignCryptSystem` edit) and FD-W4 (Discord — the webhook/min_players CVars are DEFINED but no
sending code exists behind them; `data/first_deaths.jsonl` writer is FD-W4's). Open question 3's
commiseration suppressor was NOT added (double-beat accepted, per lane instruction).

---

## 1. Architecture as built

| Piece | File | Notes vs spec |
|---|---|---|
| `first_death` table | `Content.Server/_Solreign/SeasonLedger/SeasonLedgerStore.cs` (schema battery) | Exactly the §3.1 DDL. Added inside the existing `CREATE TABLE IF NOT EXISTS` battery (the spec's stated location). **No `user_version` bump** — purely additive, an older binary ignores the unknown table (the `standing_total` additive-migration precedent). |
| Store methods | `SeasonLedgerStore.FirstDeath.cs` (new partial) | `TryClaimFirstDeathAsync` (atomic `INSERT … ON CONFLICT(user_id) DO NOTHING` + rowcount, the SetAnnouncedTitleAsync write-before-dispatch idiom), `GetFirstDeathAsync`, `TryMarkRehireShownAsync` (conditional UPDATE + rowcount guard). Same `_lock`/`OpenAsync` discipline as every existing method. |
| System delegations | `SeasonLedgerSystem.FirstDeath.cs` (new partial) | Thin pass-throughs so `ProvidenceFirstDeathSystem` never stands up a second store against the same SQLite file (the `GetCareerStatsAsync` precedent). |
| Cause classifier | `Providence/FirstDeathCauseClassifier.cs` | Pure, table-driven, 5 buckets per §3.3 + the barotrauma resolution (below). Tie preference VACUUM > BURN > MISADVENTURE, insertion-order-insensitive. |
| Epitaph picker | `Providence/FirstDeathEpitaphPicker.cs` | Pure, deterministic, §8B plate library verbatim, `(tours + title.Length + causeOrdinal) % count` index, 90-char cap → short-variant fallback, `{title}` the only variable, `{name}` never on a plate. |
| Round guard | `Providence/FirstDeathRoundGuard.cs` | ProvidenceWelcomeGate shape; doc comment states explicitly it is the double-async-dispatch stopper, NOT the exactly-once mechanism. |
| Beat queue | `Providence/FirstDeathBeatQueue.cs` | ProvidenceFirstShiftPersonalQueue idiom (drain-before-handing-back), one queue for both beat kinds (`DeathScene` / `Rehire`), never stores a session or entity. |
| Copy tables | `Providence/FirstDeathCopy.cs` | Pure key selection: cause-matched eulogy map, rehire pick (R2 waiver forced at tours 0), closed-vocabulary constants. |
| Scene system | `Providence/ProvidenceFirstDeathSystem.cs` | Broadcast `MobStateChangedEvent` subscriber (directed pair untouched — owned by `SolreignDeathTelemetrySystem`), §3.2 gate chain in order (CVar → `HumanoidProfileComponent` → mind→account → round guard mark-pre-await), synchronous snapshot, async claim → career stats → compose → T+4s beat; `PlayerSpawnCompleteEvent` rehire side with `ev.Silent` early-return, write-first stamp, +6s private beat. Fire-time guards: CVar re-check, `GameTicker.RunLevel == InRound`, session re-resolution, ghost/dead/deleted checks on the rehire target. |
| CVars | `Content.Shared/CCVar/CCVars.Solreign.cs` (append-at-end) | `solreign.first_death.enabled` **default TRUE** (spec's recommended posture), `solreign.first_death.webhook` empty + CONFIDENTIAL (inert — no consumer), `solreign.first_death.min_players` 2 (inert — no consumer). |
| Copy pack | `Resources/Locale/en-US/_solreign/first-death.ftl` | §8 pack VERBATIM: 8 eulogies, §4.2 private line, `45cr` fee literal, 6 rehire lines. Variables strictly ⊆ {$name,$tours,$title,$fee}. |

Scene delivery details (all per spec): eulogy via `ChatSystem.DispatchGlobalAnnouncement`, sender
loc `PROVIDENCE`, acid green `#39FF14` (the HR-ceremony brand color), `playSound: false` so the
default chime never stacks over the sting; sting via `ProvidenceVoiceSystem.PlayLine(DeathCommiseration)`
(self-no-ops when voice off → text-only degradation — the scene is deliberately independent of the
voice CVar, opposite branch from commiseration, per §3.2 point 1); private lines via
`IChatManager.DispatchServerMessage` (the LowPopLobbyReminder precedent) + `SharedPopupSystem` for
the rehire popup. The fee is fiction: nothing in this lane writes any ledger value.

## 2. Barotrauma verification (spec §3.3 / §9 Q8 — RESOLVED)

The spec's suspicion is **confirmed: crew barotrauma deals BLUNT, not a vacuum-flavored type.**

* `Resources/Prototypes/Body/species_base.yml:171-174` — `BaseSpeciesMob`'s `Barotrauma` block is
  `damage: types: Blunt: 0.50`. (Slimes override with Blunt 0.50 + Heat 0.2; the generic
  `MobAtmosStandard` base for NPC fauna uses Blunt 0.15.)
* `Content.Shared/Atmos/Atmospherics.cs:341` — `LowPressureDamage = 4`;
  `BarotraumaSystem.Update` applies `barotrauma.Damage * Atmospherics.LowPressureDamage` under
  hazard low pressure → **2 Blunt/s on crew in hard vacuum**, which routinely out-accumulates the
  respirator's Asphyxiation.
* Also verified while there: `BaseSpeciesMob` carries `- type: HumanoidProfile`
  (species_base.yml:35), so the §3.2-step-2 crew-body gate holds for every crew species.

**Resolution chosen** (instead of the spec's sketched "add Blunt to the VACUUM row", which would
misclassify every no-attacker crushing/fall as a spacing): `Blunt-dominant AND Asphyxiation present
(> 0) AND no attacker → VACUUM`. A genuine spacing always also accrues Asphyxiation (there is no
air to breathe). [Review F6 correction: a pressurized-hall crushing that passes through crit ALSO
accrues Asphyxiation (crit suffocation), so the crit-then-die crushing path classifies VACUUM —
an accepted flavor-only over-trigger; only an instant Blunt kill stays MISADVENTURE.] Blunt-dominant without asphyxiation stays
MISADVENTURE. Both directions are unit-tested, and the integration suite pins the complement on a
real death (Blunt-to-threshold, no asphyxiation → `MISADVENTURE` in the claim row).

## 3. Deviations from spec (all flagged)

1. **Epitaph plates live as C# constants in the picker, not in the .ftl.** An epitaph is never
   rendered through the game's Loc pipeline in this lane — it is persisted by plate id and (in
   FD-W3) shipped as composed text to the daemon. Keeping the library pure lets the 90-char cap and
   determinism be unit-tested to the character. The .ftl holds every string that actually renders
   in-game (eulogies, private line, fee, rehire lines, sender).
2. **§8B "cause override" interpreted as pool-join, not replacement.** A literal override at
   tours > 0 would make plates 03-08 unreachable; a tours-family-only read would make 09-13
   unreachable. As built: tours 0 → ORIENTATION only (day-one rule verbatim); tours > 0 → candidate
   pool = tours family + the cause plate, deterministic index over the pool. Every plate is
   reachable (unit-tested).
3. **Eulogies are always cause-matched (E3-E7).** The spec's "cause-matched if one exists, else
   generics" fallback never triggers because the closed 5-cause set is fully covered. E1/E2/E8 ship
   in the .ftl (pack adopted verbatim) as staged variety, referenced by `FirstDeathCopy.
   GenericEulogyKeys` so the copy-integrity tests keep them honest; no selection path reaches them
   in v1.
4. **Attacker predicate** (§3.2 step 5's [needs verification]): mirrored `DeathAttribution.
   TryResolveAttackerGuid` exactly (so this channel and the crypt channel can never disagree about
   whether a killer existed), PLUS a self-kill exclusion (resolved attacker guid == victim account
   guid → not VIOLENCE). A suicide is not "a workplace dispute".
5. **Title/tours come from CAREER stats** (`GetCareerStatsAsync` + `TitleRules.Compute`), per the
   spec §2 timeline — noting that display titles elsewhere are season-scoped
   (`LoadTitle`/`GetStatsAsync`). The claim row is a career event, so career totals are the
   consistent choice; a player's `title_at_death` may therefore differ from their current
   season-scoped examine title.
6. **Fire-time `RunLevel == InRound` guard added** (not explicit in §3.2) — the
   `AnnounceTitleCeremony` precedent: a death at round end must not eulogize over the post-round
   summary. [Review F4 correction: at-most-once semantics — a DEATH-SCENE beat swallowed by a round
   boundary is recovered only insofar as the rehire beat still reads the claim row on a later
   spawn; a swallowed REHIRE beat (die-again-within-6s / restart-within-6s / disconnect) is
   permanently consumed and never re-queued (ProvidenceFirstDeathSystem comment, ~line 367).
   Additionally, the claim path now carries its own InRound gate (review F1 fix), so a PostRound
   death no longer consumes the claim at all.]
7. **Integration tests kill for real** (Blunt to the `MobThresholdSystem` Dead threshold,
   `RejuvenateSystem` between deaths) rather than raising a synthetic `MobStateChangedEvent`: a
   synthetic event with hand-built states trips `mob_threshold`'s alert system into
   `No alert alert for mob state Invalid` ERROR logs, which fail the pair at teardown. This is the
   changeling/Defibrillator "real damage path" technique and exercises strictly more of the real
   pipeline than the synthetic dispatch the spec sketched.
8. **Offline-law scenario (§7 row 8) is satisfied structurally rather than by a dedicated test:**
   this lane contains zero Director-channel code (no crypt wire change — FD-W3), and every
   integration scenario ran with the Director CVars at their default (off/empty) — the full scene
   completed with no outbound HTTP possible. The dedicated offline test belongs with FD-W3's wire
   change.
9. **`docs/WORKTREE-MAP.md` registration** (spec §10 Phase-0 rule): no such file exists in the GAME
   repo tree (it lives in orch-ops); registration left to the orchestrator.

## 4. Test evidence (all Release, foreground, 2026-07-16)

| Gate | Result |
|---|---|
| `dotnet build -c Release` (full solution, 50 projects) | **0 errors** |
| `dotnet test Content.Tests -c Release --no-build` | **2281 passed / 3 skipped (known)** — baseline 2216 + **65 new** |
| `dotnet test Content.IntegrationTests -c Release --no-build --filter FullyQualifiedName~Solreign` | **306 passed / 1 skipped (known)** — baseline 302 + **4 new** |
| `dotnet run --project Content.YAMLLinter -c Release` | **"No errors found"** |

New unit tests (65, `Content.Tests/_Solreign/FirstDeath*.cs`):
* `FirstDeathStoreTests` (9) — claim exactly-once incl. **cross-store-instance concurrent races**
  (two stores, same file, `Task.WhenAll` → exactly one win, so the guarantee is SQLite's, not the
  process-local lock's); season bump does NOT reset; rehire conditional-update semantics incl.
  racing double-marks; full column round-trip; distinct accounts independent.
* `FirstDeathCauseClassifierTests` (21) — all 5 buckets, attacker precedence (incl. over the vacuum
  signature), empty/null/all-zero → UNKNOWN, both barotrauma directions, tie-breaks incl.
  enumeration-order insensitivity, ordinal contract pinned.
* `FirstDeathEpitaphPickerTests` (13 incl. cases) — determinism, day-one-always-ORIENTATION for
  every cause, family-by-tours pool membership at every boundary (1/4/5/19/20), cause-plate
  reachability sweep, title rendering, 90-char cap → short variant, every plate id resolvable +
  unique, closed-vocabulary/denylist rail.
* `FirstDeathRoundGuardTests` (5), `FirstDeathBeatQueueTests` (6) — gate/queue mechanics.
* `FirstDeathCopyTests` (11) — every referenced .ftl key exists AND no orphan keys (both
  directions), variable set ⊆ {name,tours,title,fee}, attacker-vocabulary denylist over the whole
  pack, every eulogy contains $name, fee is the literal `45cr`, R2-waiver rule, deterministic
  rehire pick, null-title tolerance.

New integration tests (4, `Content.IntegrationTests/Tests/_Solreign/FirstDeathSceneIntegrationTest.cs`):
* `FreshAccount_Dies_ExactlyOneSceneAndOneClaim_LaterDeathsNothing` — real death → exactly one
  scheduled scene + durable claim row (cause pinned `MISADVENTURE` for the Blunt-only kill);
  fire delivers without throwing and drains; second same-round death → nothing (guard); simulated
  next-round death → nothing (claim row). Two deaths → one scene, proven three ways.
* `EnabledFalse_IsZeroBehavior_NoBeat_NoClaim` — kill switch means not even the claim row is written.
* `Rehire_FiresOnNextSpawn_ExactlyOnce_AndSilentSpawnsAreSkipped` — Silent spawn consumes nothing;
  first eligible spawn stamps write-first then delivers popup+chat against the revived body; second
  spawn re-delivers nothing.
* `Coexistence_TheSameDeathStillFeedsEveryPreExistingDeathBeat` — one real death dispatches cleanly
  through commiseration (default-on) + EarlyDeath + the directed telemetry pair, and the authored
  scene still claims (additive-only proof; the rare double-beat accepted per open question 3).

QueueDel/`WaitRunTicks(1)` discipline: no test in this lane deletes entities before asserting
`Deleted`; the revive/kill cycles run `WaitRunTicks(6)` (the house threshold-processing wait) and
every fire-time guard is exercised through the systems' own re-resolution paths.

## 5. Operational notes for the orchestrator

* **Rollback:** `solreign.first_death.enabled=false` restores today's behavior exactly (integration-
  proven, including no claim-row writes). Pre-merge, the branch is delete-safe; nothing outside the
  two spec-sanctioned shared-file touches (`SeasonLedgerStore.cs` schema battery,
  `CCVars.Solreign.cs` append-at-end) edits existing code.
* **The council's kill test rides on top** (memo test 4: authored first deaths → neutral-or-positive
  chat mentions, ≥3/5 second sessions within a week): operational measurement, not CI — collect it
  after the Grand Opening cohort.
* **FD-W3/FD-W4 hooks in place:** the claim row persists `epitaph_id` (resolve text via
  `FirstDeathEpitaphPicker.TryGetPlateTemplate`), the webhook/min_players CVars exist and are inert,
  and `FirstDeathCopy`'s tables are the single source for every player-facing key.
