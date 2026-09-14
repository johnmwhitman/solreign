# GAME repeatable kart heats refresh receipt

Date: 2026-07-27

Branch: `codex/game-repeatable-kart-heats-refresh-20260727`

Canonical base: `37ede566c65b68e8a428a518744a1868ecd6e10a`

Refreshed implementation commit: `e8fab7feb0`

Lifecycle hardening commit: `02addd7d0bd69371d866c8d9bec3751b069ff953`

Qualified code tree: `dd09000ba5ef7869d21245924cbce385df6ffcc3`

Verdict: **QUALIFIED / DEFAULT OFF / ACTIVATION HOLD**

## Outcome

The current GAME composition now has a fully qualified candidate for
server-timed, repeatable kart heats. It remains controlled by the server-only
`solreign.kart_repeatable_heats_enabled` CVar, whose real default and effective
test-server value are both asserted `false`.

The refreshed lifecycle fixture additionally proves the previously unbound
incomplete-heat policy:

1. a valid ordinal-zero crossing starts a timed heat;
2. disabling before the next checkpoint preserves ordinal progress but clears
   optional timing and driver identity;
3. re-enabling alone cannot reinterpret the partial circuit as timed; and
4. the remaining checkpoint completes under the legacy finish boundary, with
   no elapsed time or finishing identity retained.

No map, prototype, asset, Ledger, website, Director, RobustToolbox, launcher, or
hub file changed.

## Exact-composition verification

| Gate | Result |
|---|---:|
| strict `Content.IntegrationTests` build | **0 errors / 148 existing warnings** |
| focused pure kart rules | **14 passed / 0 failed / 0 skipped** |
| focused real ECS/buckle lifecycle | **1 passed / 0 failed / 0 skipped**, 13s |
| full `Content.Tests` | **3,006 passed / 0 failed / 3 expected skips**, 5s |
| canonical `_Solreign` integration band | **427 passed / 0 failed / 4 expected skips**, 17m43s |
| `git diff --check` against master | **pass** |
| gitleaks over the three-commit branch range | **no leaks found** |
| map/prototype changed-path scan | **no matches** |

The initial sandboxed unit attempt built successfully but its test runner was
aborted because the restricted environment denied the local VSTest loopback
socket. The authoritative focused and full results above were rerun outside
that sandbox against the same local checkout.

## Independent review

External Grok reviewed the exact qualified code tip and returned **GO**, with
no P0-P2 finding. A separate local adversarial reviewer found no P0/P1 code
defect and correctly held the inherited package because its 2026-07-26 handoff
contained obsolete identities. This dated receipt and its companion handoff
replace that consume path without rewriting the historical record.

Disclosed P3 gaps:

- no direct count assertion around `DispatchGlobalAnnouncement`;
- no end-to-end Fluent singular/plural render assertion;
- a driver swap during an active heat credits the current driver;
- a kill-switch normalization at the post-lap/pre-line boundary is silent; and
- the stored finishing driver uses the existing ephemeral entity identity.

The `Finished` early return structurally prevents checkpoint-driven duplicate
announcements, and the lifecycle fixture proves frozen result state remains
immutable. The gaps above remain non-blocking for default-off integration but
belong in a live two-driver activation canary.

## Authority boundary

This receipt qualifies local integration only. The CVar must remain `false`.
No push, package publication, deploy, restart, feature activation, credential
use, live-player data access, public mutation, or production mutation occurred.
