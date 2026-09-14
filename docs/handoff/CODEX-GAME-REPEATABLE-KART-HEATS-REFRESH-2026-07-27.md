# Codex handoff — repeatable kart heats refresh

## Qualified candidate

- Worktree:
  `/Users/johnwhitman/AI/solreign-trees/codex-game-repeatable-kart-heats-refresh-20260727`
- Branch: `codex/game-repeatable-kart-heats-refresh-20260727`
- Canonical base:
  `37ede566c65b68e8a428a518744a1868ecd6e10a`
- Refreshed implementation: `e8fab7feb0`
- Historical handoff carrier: `66631ac7f5`
- Lifecycle hardening and exact qualified code tip:
  `02addd7d0bd69371d866c8d9bec3751b069ff953`
- Qualified code tree:
  `dd09000ba5ef7869d21245924cbce385df6ffcc3`
- RobustToolbox:
  `960edb32c4dd417496e4667177625d8c3cb14f7e`
- Receipt:
  `docs/receipts/CODEX-GAME-REPEATABLE-KART-HEATS-REFRESH-2026-07-27.md`
- Kill-date: 2026-08-10

## Disposition

The code is eligible for local integration with
`solreign.kart_repeatable_heats_enabled` still `false`.

Exact composition evidence:

- strict integration-project build: 0 errors;
- focused kart rules: 14/0/0;
- focused ECS/buckle lifecycle: 1/0/0;
- full units: 3,006/0/3;
- complete canonical `_Solreign` integration band: 427/0/4;
- diff hygiene and changed-path scope: pass; and
- gitleaks: pass.

External Grok returned GO with no P0-P2 finding. A separate local adversarial
review found no P0/P1 code defect. Both retained the activation gate.

## Consume procedure

1. Revalidate current GAME master and ownership.
2. If master is still
   `37ede566c65b68e8a428a518744a1868ecd6e10a`, fast-forwarding the complete
   branch preserves the exact qualified composition plus this evidence.
3. If master has advanced, regenerate the composition and rerun at least the
   strict build, full units, and canonical `_Solreign` integration band.
4. Keep `solreign.kart_repeatable_heats_enabled` `false`.
5. Treat activation as a separate live two-driver canary with announcement,
   driver-swap, silent kill-switch, and rollback observations.

The 2026-07-26 handoff remains historical evidence for the pre-refresh branch;
do not follow its obsolete commit identities for this candidate.

No push, package, deploy, restart, activation, launcher/hub change, credential
use, live-player data access, public mutation, or production mutation is
authorized by this handoff.
