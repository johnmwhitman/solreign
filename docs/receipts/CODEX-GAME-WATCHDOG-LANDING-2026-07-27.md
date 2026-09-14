# GAME watchdog landing receipt

Date: 2026-07-27

Branch: `codex/game-land-watchdog-20260727`

Base: `origin/master` at `fa12e6ee0295a42b8efb97ce2633f2816bbb92ef`

## Composition

The integration-watchdog tranche was replayed independently of repeatable kart
heats and all gameplay features:

- `6154b951f5e` — bounded watchdog configuration and tests
- `f48e8b963d5` — original handoff and receipt
- `ab8e0eb8c02` — local-integration receipt
- `b052a2d5e98` — evidence-precision correction

This branch changes only `Content.IntegrationTests` and documentation. It does
not change production runtime, content, maps, CVars, RobustToolbox, launcher, or
hub behavior.

## Fresh exact-tip evidence

- Focused watchdog contract: **15 passed / 0 failed / 0 skipped**
- Command:
  `dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj --no-restore -m:1 -nodeReuse:false -p:UseSharedCompilation=false --filter FullyQualifiedName~SolreignIntegrationWatchdogConfigurationTest`
- TRX:
  `/private/tmp/solreign-watchdog-results/watchdog-focused-escalated.trx`
- TRX SHA-256:
  `f5264ae78d692495417a6cac03a05ccd76d2361350467b896997e5e3b00ff9fd`
- The initial sandboxed run was aborted only because VSTest could not bind its
  loopback socket. The same built tree passed under normal loopback access.
- Strict serial compilation completed with zero errors as part of the focused
  test command. Existing compiler and offline NuGet-audit warnings remain.
- `git diff --check`: pass
- Gitleaks over `origin/master..HEAD`: 4 commits, no leaks

## Landing and deploy boundary

This is the first and smallest landing tranche. H3 Tier-2 roles and repeatable
kart heats remain separate so failures and rollback stay attributable.

Production deployment remains **HOLD**: the current legacy deployment path does
not transactionally park and restore root runtime binaries with Resources,
client ZIP, and the Season Ledger. A merge result is not deploy authorization
until that rollback composition is reviewed and proven.
