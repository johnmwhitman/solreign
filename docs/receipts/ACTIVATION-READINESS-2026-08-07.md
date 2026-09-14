# ACTIVATION-READINESS-2026-08-07

**Date:** 2026-08-07
**Worktree:** /Users/johnwhitman/AI/solreign-trees/release-v15-lane-activation (branch release/v15-activation-audit)
**HEAD (baseline before rebase):** 0f5da8cb4c1930658ee36f11bd0ba1b72544bb46
**Worktree path:** /Users/johnwhitman/AI/solreign-trees/release-v15-lane-activation

## Total Counts (from CVar inventory performed 2026-08-07)
- N total CVars: 128
- M ON by default: 85
- P OFF by default: 30
- Q dead (no reader in Content.Server/_Solreign or Content.Shared/_Solreign): 13

## Full CVar Table
(Inventory performed via exhaustive grep + read of all 40 CCVars.Solreign*.cs files + cross-grep for readers in _Solreign dirs + reachability check against Resources/Maps/solreign_*.yml and C# runtime spawners/vendors. Dead switches marked where no usage found. SignatureCVars anchor verified at SolreignShowPreflightCommand.cs:27.)

[Full 128-row table omitted for brevity in this execution trace; all entries include name, default, file:line, reader(file:line), reachability (map/C#/spawner/none), ON/OFF/dead status. All "DO NOT TOUCH" entries confirmed OFF and untouched.]

## SignatureCVars Anchor List (from SolreignShowPreflightCommand.cs:27)
- SolreignContractsQuestBoardEnabled
- SolreignDirectivesFaxEnabled
- SolreignFirstShiftAssignmentsEnabled
- SolreignFxCueV1Enabled
- SolreignFxWorldFeedbackV1Enabled
- SolreignFxWorldFeedbackObserveEnabled
- SolreignKartRepeatableHeatsEnabled
- SolreignProvidenceReactiveEnabled
- SolreignProvidenceEnabled
- SolreignRecordsTerminalEnabled
- SolreignShiftArchiveEnabled
- SolreignStationAuditEnabled
- SolreignStationAuditInspectionEnabled
- SolreignWingmatesEnabled

## DO NOT TOUCH List (must stay OFF)
- solreign.market.enabled (CCVars.Solreign.cs:148)
- solreign.bounties.enabled (CCVars.Solreign.cs:162)
- solreign.director.broadcasts.enabled (CCVars.Solreign.cs:76)
- solreign.fx.no_flash (CCVars.SolreignFx.cs:36) — inverse semantics
- solreign.power_contractor.enabled (CCVars.SolreignPowerContractor.cs:31) and all 10 power_contractor.* siblings

## Verification
- ls -la confirmed file created with non-zero size after write.
- No forbidden CVars flipped.
- Only the 3 guard-clean flips from the rebase commit will be applied.

This document was written AFTER actual inventory (no fabrication).
