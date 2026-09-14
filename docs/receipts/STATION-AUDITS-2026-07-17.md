# Station Audits — v14 wave-1 #3 (council C1, deltav-008 lineage)

**Branch:** `feat/station-audits`
**Cost:** S
**Status:** dormant on land — `solreign.station_audit.enabled` ships `false`

## Clean-room note

Built from OUR idioms only. No Delta-V code was read, copied, or referenced at any point in this
lane. The single input taken from the public description of Delta-V's Station Report feature was
the *shape*: an in-world paper station report whose text also reaches the round-end summary
screen. Every section, table, event, template, and system in this receipt is assembled fresh from
patterns already shipped in this fork:

- `SeasonLedgerSystem`'s round-end handoff idiom (async void + try/catch, snapshot-before-await)
- `SolreignCorporateRuleSystem.AppendRoundEndText`'s "override the broadcast event" idiom
- `SolreignFinalBalanceSheetRule`'s paper-spawn idiom (`Spawn` + `PaperSystem.SetContent`)
- `FirstDeathEpitaphPicker`'s "pure selection, Loc-resolved separately" idiom
- `SeasonLedgerSystem.EarlyDeath`'s self-contained per-round tracker discipline
- `StationDirectiveCatalog`'s deterministic-by-round-id selection idiom (reused for Item of Concern)

## The feature

PROVIDENCE compiles an end-of-shift **Station Audit** from real, locally-observed round state —
closed template vocabulary throughout, zero free text, zero LLM. Two outputs (round-end screen
text + a printed keepsake paper), plus a compact history row in a new `station_audit_log` Season
Ledger table.

### Section list

| Section | Source (real, local) |
|---|---|
| Header / round id | `GameTicker.RoundId` |
| Shift duration | Self-tracked `RoundStartingEvent` timestamp vs. `IGameTiming.CurTime` |
| Crew count | Self-tracked `PlayerSpawnCompleteEvent` account set (dedup by GUID) |
| Corporate Directive on file | Reads the real, already-shipped `StationDirectiveRuleComponent` (Station Directive, SR-W-081) — the directive selected this round, if the layer started |
| Directive outcome | **Seam** — see below. Absent today → "Not On Record", never fabricated |
| Deaths | Self-tracked `MobStateChangedEvent → Dead`, dedup by account GUID |
| Whether commemorated | New `ProvidenceFirstDeathSystem.FirstDeathCommemoratedThisRound` (this-shift scope, not career scope) |
| Stipends | `CCVars.SolreignSalaryEnabled` + the same crew set (every crew member who played is salary-eligible per `SalaryRosterPayload.IsSalaryEligible`'s own definition) — zero daemon round-trip |
| Bounties | `CCVars.SolreignBountiesEnabled` + new `SolreignBountySystem.ClaimVerdictsThisRoundForAudit` (counts adjudications, not "wins" — see note below) |
| Notable events | `GameTicker.AllPreviousGameRules.Count` — every game rule (antag + station event) started this round, already tracked by the engine |
| Commendation of the Shift | Self-tracked kill attribution (mirrors `DeathAttribution`'s melee/projectile-shooter resolution, self-kill excluded), handle-named via `Identity.Name` |
| Item of Concern | Deliberately **fictional** deadpan flavor — closed 6-entry set, picked deterministically by round id (`ItemOfConcernPicker`) |

### Why "bounties claimed" became "claims adjudicated"

`SolreignBountySystem`'s `ClaimAckDto.status` field is deserialized but was, before this lane,
never read anywhere in this codebase — only `msg` is shown to the player. There is no in-repo
documentation of what `status` values the Director daemon sends, so guessing at an
accept/reject contract would have been fabricating a table that doesn't exist here. Rather than
invent one, Station Audits counts verdicts *delivered* (the daemon answered), which is honest and
already well-defined, and phrases it accordingly in the .ftl copy ("N claims adjudicated").

## The seam vs. `feat/directives-fax`

At build time `feat/directives-fax` was still at the master tip (`dcb87e1ace`) — no work landed, no
receipt to mirror. Station Directive (SR-W-081, "random corporate round modifiers") is a real,
already-shipped, default-on system, but it is explicitly documented as flavor-only: "no new metric
tracking... just a shared, present, station-wide thing to care about." So today there is no local
computation anywhere for "was the directive fulfilled" — that is presumably exactly what
directives-fax will add.

Station Audits reads the directive's **name** directly (`StationDirectiveRuleComponent.DirectiveIndex`
→ `StationDirectiveCatalog` → a short audit-title mapping in `StationAuditDirectiveTitles`) — real,
already-shipped, local state, zero new tracking. For the **outcome**, it defines
`SolreignDirectiveOutcomeQueryEvent`: a broadcast local event Station Audits raises once at shift
end and reads back. Zero compile-time coupling — Station Audits owns the event, so it never
references a directives-fax type that doesn't exist yet. If a future directive-outcome system
subscribes and calls `.Report(fulfilled)`, the audit picks it up automatically; until then every
audit honestly renders "Directive Outcome: Not On Record" rather than fabricating a verdict.

**Reconciliation note for whoever builds directives-fax next:** if that lane computes a directive
outcome, the only wiring needed on its side is `SubscribeLocalEvent<SolreignDirectiveOutcomeQueryEvent>`
+ one `Report()` call. No changes needed on the Station Audits side.

## Print-point reconciliation note

The paper spawns at the station's (first found) `CommunicationsConsoleComponent` — chosen because
directives-fax had made no print-point decision to mirror (still at the master tip). If
directives-fax later lands its own "where does the station's paperwork appear" choice for a
directive/fax artifact, reconcile the two so a shift doesn't end with two different keepsake-paper
conventions. No console found (e.g. a station missing one) → the physical paper is skipped
entirely; round-end text and the ledger row are unaffected (graceful degradation, not a hard
dependency).

## Rails honored

- **Dormant on land**: `solreign.station_audit.enabled` defaults `false` (`CCVars.SolreignStationAudit.cs`, its own file per the `CCVars.SolreignStationDirective.cs` collision-control idiom).
- **Additive-only**: no existing behavior changed. Every touch to another system (`ProvidenceFirstDeathSystem`, `SolreignBountySystem`, `StationDirectiveRuleComponent`'s doc comment) is a new field/flag with a new reset hook, nothing removed or altered.
- **PG-13**: every template line is corporate-deadpan flavor text, same register as the Corporate Ladder / Station Directive; the Commendation section is the same "kill attribution as productivity" framing those two systems already ship.
- **Closed templates, no free text**: every rendered line is a `Loc.GetString` call against `station-audits.ftl`; the only "generated" values are numbers, names (handles, via `Identity.Name` — the first-death eulogy precedent), and closed-vocabulary ids.
- **Handles only**: the Commendation uses `Identity.Name`, matching the first-death eulogy's own naming idiom.
- **Cheap assembly**: all composition happens once, at shift end, from state tracked continuously through the round (no per-tick scans, zero daemon round-trips).
- **`SeasonLedgerDbPath.Resolve` house law**: no second `SeasonLedgerStore` stood up — `StationAuditSystem` calls `SeasonLedgerSystem.AppendStationAuditAsync`, which delegates to the store already opened via `SeasonLedgerDbPath.Resolve` in `SeasonLedgerSystem.Initialize`.

## Files touched

**New:**
- `Content.Shared/CCVar/CCVars.SolreignStationAudit.cs`
- `Content.Server/_Solreign/StationAudits/StationAuditSystem.cs`
- `Content.Server/_Solreign/StationAudits/StationAuditReport.cs`
- `Content.Server/_Solreign/StationAudits/StationAuditComposer.cs`
- `Content.Server/_Solreign/StationAudits/ItemOfConcernPicker.cs`
- `Content.Server/_Solreign/StationAudits/StationAuditDirectiveTitles.cs`
- `Content.Server/_Solreign/StationAudits/SolreignDirectiveOutcomeQueryEvent.cs`
- `Content.Server/_Solreign/SeasonLedger/SeasonLedgerStore.StationAudits.cs`
- `Content.Server/_Solreign/SeasonLedger/SeasonLedgerSystem.StationAudits.cs`
- `Resources/Prototypes/_Solreign/Entities/station_audits.yml`
- `Resources/Locale/en-US/_solreign/station-audits.ftl`
- `Content.Tests/_Solreign/StationAuditComposerTests.cs` (15 tests)
- `Content.Tests/_Solreign/StationAuditStoreTests.cs` (4 tests)
- `Content.IntegrationTests/Tests/_Solreign/StationAuditSystemIntegrationTest.cs` (2 tests)

**Modified (small, additive):**
- `Content.Server/_Solreign/SeasonLedger/SeasonLedgerStore.cs` — new `station_audit_log` table (schema battery + doc comment)
- `Content.Server/_Solreign/Providence/ProvidenceFirstDeathSystem.cs` — `FirstDeathCommemoratedThisRound` (internal, this-shift-scoped)
- `Content.Server/_Solreign/Bounties/SolreignBountySystem.cs` — `ClaimVerdictsThisRoundForAudit` (internal per-round counter)
- `Content.Server/_Solreign/StationDirective/Components/StationDirectiveRuleComponent.cs` — doc-comment note only (no `Access` grant needed; default "Other" permission is already Read)
- `Resources/_Solreign/orphan_allowlist.yml` — `SolreignPaperStationAudit` (C#-only spawn, same idiom as the Season 1 clue papers)

## Verification evidence (Release, this branch)

| Gate | Result |
|---|---|
| `dotnet build Content.Server -c Release` | 0 errors |
| `dotnet build Content.Tests -c Release` | 0 errors |
| `dotnet build Content.IntegrationTests -c Release` | 0 errors |
| `dotnet build Content.YAMLLinter -c Release` | 0 errors |
| `dotnet run --project Content.YAMLLinter -c Release --no-build` | **No errors found** |
| `dotnet test Content.Tests -c Release --no-build` | **2474 passed / 0 failed / 3 skipped** (baseline ~2455/3 + 19 new) |
| `dotnet test Content.IntegrationTests --filter FullyQualifiedName~Solreign` | PENDING_FULL_RUN |

Targeted `StationAuditSystemIntegrationTest` run (both scenarios): **2 passed / 0 failed** —
`Disabled_ProducesZeroBehavior` (no round-end text contribution, no paper, no ledger row) and
`Enabled_RealRoundEnd_AppendsTextPrintsPaperAndPersistsLedgerRow` (real `RoundEndTextAppendEvent`
on the live event bus → text appended, paper spawned at a comms console with matching content,
ledger row lands and round-trips).

One orphan-reachability finding caught and fixed during verification: `SolreignPaperStationAudit`
is C#-only spawned (never map/spawner placed), so it needed a `Resources/_Solreign/orphan_allowlist.yml`
entry — added, same idiom as the Season 1 clue papers.

Branch: `feat/station-audits`
Tip SHA: PENDING_COMMIT
