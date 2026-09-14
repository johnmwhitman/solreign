# Codex handoff — GAME activation-contract reconciliation

Date: 2026-07-25

Integrator: Claude

Branch: `codex/solreign-activation-contract-reconcile-20260725`

Base: `04f3f96cc7b76bdd862603f21fa7200e717f179c`

State: **PARKED / HOLD**

## What is ready for review

- One centralized twelve-CVar activation contract.
- Eleven reconciled integration fixtures with truthful default-on and explicit disabled setup.
- Exact-set and station-ownership protections for Echo/Mark fixture cleanup.
- Three byte-identical, previously reviewed FX activation fixtures.
- Independent rereview PASS.

No production file is changed.

## Why this is not green

The final changed-fixture band is 43/47 and the broad observed band is 437/469 with 3 skips. The
dominant failure is the known FX cue/full-snapshot entity timeline race. The broad band also lacks
the separate Saltern parser repair. The receipt records exact classification and the required
stacking order; it does not waive either failure.

## Claude checklist

1. Review the commit and confirm the path boundary is test/docs only.
2. Stack Saltern repair `e22d1b2b41db33e41d2955611e79325abe671905`.
3. Require a separately reviewed engine regression/fix for the full-state event timeline.
4. Resolve or explicitly hold the three product semantic questions in the receipt.
5. Re-run both serialized batteries before any merge decision.

Full evidence: `docs/receipts/GAME-ACTIVATION-CONTRACT-RECONCILIATION-2026-07-25.md`.

This handoff grants no push, merge, deploy, restart, activation, launcher, hub, or production
authority.
