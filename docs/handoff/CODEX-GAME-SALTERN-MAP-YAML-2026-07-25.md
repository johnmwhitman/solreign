# Codex GAME Saltern map YAML handoff

## State

- Branch: `codex/solreign-saltern-map-yaml-20260725`
- Worktree:
  `/Users/johnwhitman/AI/solreign-trees/codex-solreign-saltern-map-yaml-20260725`
- Base: `04f3f96cc7b76bdd862603f21fa7200e717f179c`
- Push/merge/deploy/activation: none
- Integrator: Claude
- Verdict: **HANDOFF-READY**

## Candidate

The candidate repairs the existing Saltern resource by moving its YAML document terminator after
the final entity group and reconciling `meta.entityCount` with the 11,454 committed UIDs. No entity
or gameplay data changes.

Durable evidence:

- `docs/plans/2026-07-25-codex-saltern-map-yaml.md`
- `docs/receipts/GAME-SALTERN-MAP-YAML-2026-07-25.md`

## Verification

- Parser RED before: document-start failure at `Resources/Maps/saltern.yml:70872:1`.
- Parser GREEN after: one document, one terminator, 11,454 UIDs, matching metadata.
- Saltern map load: 1/1 passed.
- Antag ghost-role band: 28/28 passed.
- Independent review: PASS, no P0–P3 findings.

## Claude integration guidance

Review and cherry-pick the final branch commit only. Do not import generated `bin/` or `obj/`
artifacts; they are ignored and uncommitted. This correction is independent of the parked FX engine
candidate and the separate activation-test reconciliation.

After synthetic integration, re-run the Saltern map-load case and antag ghost-role band before any
broader package or release gate. No production action is authorized by this handoff.
