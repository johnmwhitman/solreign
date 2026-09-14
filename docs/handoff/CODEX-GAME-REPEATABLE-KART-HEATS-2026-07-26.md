# Codex handoff — repeatable timed kart heats

Date: 2026-07-26

Status: **IMPLEMENTATION COMMITTED / FOCUSED GREEN / FULL UNIT GREEN / BROAD INTEGRATION RERUN REQUIRED**

## Parked candidate

- Worktree:
  `/Users/johnwhitman/AI/solreign-trees/codex-game-repeatable-kart-heats-20260726`
- Branch:
  `codex/game-repeatable-kart-heats-20260726`
- Base and current local GAME `master` at qualification:
  `f666626ea780af644b4982d2103d1bd2fd74327e`
- RobustToolbox:
  `960edb32c4dd417496e4667177625d8c3cb14f7e`
- Implementation commit:
  `c99d94c81ceff83563b2f76db1bce467e882ae16`
- Implementation tree:
  `8872f9575256ae4f10279cd1eb3e164b0c8d4ba8`
- Conflict-free synthetic merge tree against qualified `master`:
  `8872f9575256ae4f10279cd1eb3e164b0c8d4ba8`
- Integrator: Claude only
- Kill-date: 2026-08-09

No push, merge, deploy, package, restart, CVar activation, event activation, live-box access, or
production mutation occurred.

## Outcome

The existing kart lap tracker can now run repeatable, server-timed heats behind the new
server-only `solreign.kart_repeatable_heats_enabled` CVar. It ships `false`, preserving the
existing one-race-per-kart behavior until separately reviewed and activated.

When enabled:

- a valid initial ordinal-0 crossing arms authoritative server timing;
- the final timed circuit finishes at the physical ordinal-0 line;
- the result freezes and credits the actual driver from the existing driver-seat system;
- the credited driver cannot erase the result by re-strapping;
- a different driver can reset the kart and begin a clean heat; and
- timed and untimed announcements render singular/plural lap copy.

The live-toggle policy is deliberate and tested:

- enabling during a legacy heat does not arm timing or move that heat's finish boundary;
- disabling during an incomplete timed heat discards optional timing and driver identity;
- disabling after the final circuit but before the ordinal-0 line silently normalizes to the
  legacy finished state, so a kart never owes an extra circuit;
- after optional identity is discarded, re-enabling treats that completed result as unowned, so
  the same physical driver may reset it through a real unstrap/restrap; and
- invalid `totalLaps <= 0` cannot arm timing and falls back to legacy behavior.

The integration fixture uses `SharedBuckleSystem.TryBuckle`/`Unbuckle`, not fabricated
`StrappedEvent` payloads. It covers seat-driver wiring, same/different driver behavior, both CVar
transition directions, the physical finish boundary, and immutable elapsed time after finish.

## Owned files

- `Content.Shared/CCVar/CCVars.SolreignKartHeats.cs`
- `Content.Server/_Solreign/Recreation/SolreignKartHeatRules.cs`
- `Content.Server/_Solreign/Recreation/SolreignLapTrackerComponent.cs`
- `Content.Server/_Solreign/Recreation/SolreignLapTrackerSystem.cs`
- `Content.Tests/_Solreign/SolreignKartHeatRulesTests.cs`
- `Content.IntegrationTests/Tests/_Solreign/SolreignKartHeatLifecycleIntegrationTest.cs`
- `Resources/Locale/en-US/_solreign/recreation.ftl`

No map, prototype, sprite, sound, asset metadata, Ledger, website, Director, engine, or launcher
file changed.

## Verification

Exact implementation tree evidence:

- strict `Content.IntegrationTests` build:
  **0 errors / 269 existing warnings**;
- focused pure rules:
  **14 passed / 0 failed / 0 skipped**;
- focused real ECS lifecycle:
  **1 passed / 0 failed / 0 skipped** in 24 seconds;
- full `Content.Tests`:
  **2,948 passed / 0 failed / 3 platform-specific skips**;
- `git diff --cached --check`: clean before implementation commit;
- scope scan: no `Resources/Maps` or `Resources/Prototypes` paths;
- Gitleaks 8.30.1 staged scan:
  **no leaks found**.

The first broad `FullyQualifiedName~Solreign` integration run was intentionally cancelled after an
extended bounded wait. It had emitted one known skip and no failures, but never returned a result;
this is **inconclusive**, not green. A retry using the canonical namespace filter could not start
because the local execution approval service reported its account usage limit. Do not infer full
integration health from the focused result.

## Review disposition

Two independent adversarial passes found no remaining P0-P2 issue after correction. They confirmed
closure of:

- disable-at-finish forcing an extra circuit;
- enable-mid-legacy changing the finish boundary;
- fabricated buckle-event coverage;
- invalid lap totals hanging the enabled path; and
- timer/driver state surviving the kill switch.

Non-blocking P3 gaps:

- the integration fixture does not capture the rendered announcement to assert the Fluent
  singular/plural output; and
- it proves frozen result state but does not count global announcements to prove duplicate
  suppression directly.

The final Grok 4.5 and MiniMax M3 external probes were stopped by the environment/account
usage-limit gate. Their final verdicts therefore came from local independent source review; no
claim is made that those final reviews were provider-generated.

## Claude landing gate

1. Revalidate current GAME `master` and path ownership.
2. Review and cherry-pick implementation commit
   `c99d94c81ceff83563b2f76db1bce467e882ae16`.
3. Retain the separate handoff commit following it if this evidence record is wanted.
4. Run a strict integration-project build.
5. Rerun the 14 focused rules and the focused lifecycle fixture.
6. Run full `Content.Tests`.
7. Run the complete canonical
   `FullyQualifiedName~Content.IntegrationTests.Tests._Solreign` integration band and require zero
   failures before landing.
8. Keep the CVar `false`; merge approval does not authorize activation. A live two-driver kart
   canary and operator rollback check are separate gates.

This branch is prepared for review, not authorized for merge or activation.
