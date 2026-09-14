# GAME integration watchdog receipt

Date: 2026-07-27

Branch: `codex/game-integration-watchdog-20260727`

Base: `fa12e6ee0295a42b8efb97ce2633f2816bbb92ef`

Implementation commit: `790a918eeb`

Verdict: **PARKED / REVIEWABLE / NOT INTEGRATED**

## Outcome

The integration-test harness now has an explicit, fail-closed total-runtime
watchdog contract:

- the default soft watchdog remains 20 minutes;
- the configured hard-stop deadline is exactly one minute after the soft
  watchdog;
- `SOLREIGN_INTEGRATION_WATCHDOG_MINUTES` may extend the soft watchdog to a
  whole number from 20 through 30;
- empty, signed, fractional, scientific-notation, and out-of-range values fail
  before `PoolManager.Startup()`; and
- the harness logs the effective soft deadline, hard deadline, and source.

The change is confined to `Content.IntegrationTests`. It does not change game
runtime code, content, configuration defaults, launcher or hub compatibility.

## Test-driven evidence

| Gate | Result |
|---|---:|
| focused test before implementation | **RED**, `CS0246` for missing `PoolManagerWatchdogConfiguration` |
| focused parser/configuration tests after implementation | **15 passed / 0 failed** |
| valid override smoke, value `25` | **15 passed / 0 failed**; logged soft `00:25:00`, hard `00:26:00`, environment source |
| invalid override smoke, value `19` | **expected fail-closed setup error before pool startup** |
| strict project build, `-m:1 -nodeReuse:false -p:UseSharedCompilation=false --no-restore` | **0 errors / 1,127 pre-existing warnings**, 1m24.62s |
| `git diff --check` | **pass** |
| gitleaks over `Content.IntegrationTests` | **pass** |

The focused tests cover the default, lower and upper bounds, hard-deadline
invariant, whitespace, empty input, signs, floats, scientific notation, and
values outside the allowed range.

## Independent review

Grok reviewed the exact implementation diff and returned **GO**, with no P0 or
P1 finding. It agreed that 20–30 is the safer contract because the override is
extension-only. Its residual observations were:

- the harness's pre-existing fire-and-forget timeout tasks are not cancellable;
  this change does not worsen or claim to solve that P2 concern; and
- additional leading-zero/padded-input assertions would be P3 hardening.

External MiniMax review was attempted repeatedly but the provider returned HTTP
504. No MiniMax verdict is claimed.

## Claim limits

This receipt proves the watchdog configuration contract and a strict build of
the integration-test project. It is not a full `FullyQualifiedName~Solreign`
integration run and does not qualify any game feature.

No push, merge to canonical master, package, deploy, restart, CVar activation,
credential use, live-player data access, or production mutation occurred.
