# Directives Fax Consequence Contract Receipt — 2026-07-26

## Verdict

**GREEN FOR REVIEW — TEST-ONLY, NOT LIVE-CANARY PROOF.**

The branch adds one integration fixture and no production changes. It proves the currently
integrated Directives Fax composition yields one start fax, one stamped end report, and one
durable eligible-player logical Ledger fold under a synthetic duplicate round-end signal.

## Exact state

- GAME base: `6368757ee6b980db829ad06c7c21b08a9f61c6f1`
- Base tree: `e0a2482db0bdfc0dbf0830ac56e21d6d0b08c818`
- RobustToolbox: `960edb32c4dd417496e4667177625d8c3cb14f7e`
- Branch: `codex/game-directives-consequence-v1-20260726`
- Implementation commit: `08004ed853e91b24ccae337d63ef4e6d2eea6cca`
- Implementation tree: `8d02a82f55b7bd1acfc6966eefc5ffa29db71c6d`
- Worktree:
  `/Users/johnwhitman/AI/solreign-trees/codex-game-directives-consequence-v1-20260726`

## Added evidence

`Content.IntegrationTests/Tests/_Solreign/DirectivesFaxConsequenceContractIntegrationTest.cs`
drives the real production path:

- real connected session;
- real five-minute presence threshold;
- real `GameTicker.StartGameRule`;
- real station bank and cargo-order components;
- real `RoundEndTextAppendEvent` handling, invoked twice synthetically;
- real async `SeasonLedgerSystem` write and read API;
- exact artifact classification by directive content and report stamp.

The test captures the account's initial career streak rather than assuming zero, protects the
synthetic player from incidental simulation damage, and supplies enough
Cargo delta/orders to satisfy every current directive catalog clause. It does not rewrite the
selected directive or component state. Paper UIDs that predate the controlled rule are retained
and excluded from artifact assertions; no unrelated paper is deleted.

## Verification

### Baseline existing Directives fixture

Before adding the new fixture:

```text
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj --no-build -c DebugOpt \
  --filter FullyQualifiedName~DirectivesFaxSystemIntegrationTest \
  -m:1 -nodeReuse:false -p:UseSharedCompilation=false
```

Result: **7 passed, 0 failed, 0 skipped**.

### Development corrections

The first build failed because the new fixture lacked the `Robust.UnitTesting` namespace.
After that compile correction, the first run correctly exposed two test-harness mistakes:

- localization was resolved off the server IoC thread;
- the duplicate shared event was incorrectly expected to contain no text from any rule.

Both were corrected in the test. Localization is now resolved on the server thread, and the
duplicate assertion is scoped to the Directives Fax header while permitting unrelated active
rules to append their own blocks. A further polish changed whole-world paper counts to
Directives-specific artifact counts, avoiding false failures from unrelated paper creation.

### Focused consequence fixture

```text
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj --no-restore -c DebugOpt \
  --filter FullyQualifiedName~DirectivesFaxConsequenceContractIntegrationTest \
  -m:1 -nodeReuse:false -p:UseSharedCompilation=false
```

Final review-hardened result:

- **1 passed, 0 failed, 0 skipped**
- duration: approximately 14 seconds

### Combined Directives integration band

```text
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj --no-build -c DebugOpt \
  --filter 'FullyQualifiedName~DirectivesFaxSystemIntegrationTest|FullyQualifiedName~DirectivesFaxConsequenceContractIntegrationTest' \
  -m:1 -nodeReuse:false -p:UseSharedCompilation=false
```

Final review-hardened cold-process runs:

- **8 passed, 0 failed, 0 skipped**
- first duration: approximately 18 seconds
- second duration: approximately 17 seconds
- final assertion-polished build/run: **8 passed, 0 failed, 0 skipped**, approximately 26 seconds

### Directives unit/store band

```text
dotnet test Content.Tests/Content.Tests.csproj --no-build -c DebugOpt \
  --filter FullyQualifiedName~DirectivesFax \
  -m:1 -nodeReuse:false -p:UseSharedCompilation=false
```

Result:

- **45 passed, 0 failed, 0 skipped**
- final duration: 51 ms

## Scope and interpretation

This closes a missing automated composition proof. The final Ledger state proves one logical
per-round fold because the existing store contract is idempotent; it does not claim to measure
the number of internal store method invocations or claim that the engine naturally duplicates
round-end events. It supports review of SR-W-094 moving from
`research` toward `active`; it does not satisfy the backlog's live-player canary, comprehension,
production reachability, or `proven` gates.

No public/player data, production database, live server, credentials, launcher, hub, or
deployment surface was touched.

Independent re-review after deterministic artifact/streak hardening returned **APPROVE** with
no remaining P0-P2 findings. The separate test-evidence audit returned **PASS** and judged the
focused, twice-cold combined, and unit/store bands proportionate for this test-only diff.

## Release-train composition

The implementation commit was replayed onto the exact already-green parked release candidate,
not onto the release branch's later documentation tip:

- release candidate: `aa5a51107bb312d2e7232ec70ca7327e0fe927b2`
- replayed test commit: `f1fcba5b6400af239f48531fe6d4186993d9c7b9`
- tested composition tree: `9968567b90ab40ac96fca97f9760b1b3b93da5c1`
- full branch merge-tree (including this lane's receipt/handoff):
  `546e0a7e789d58cd540fc79821c256124f7c9b57`

The first `--no-restore` invocation in the fresh disposable worktree was a detected no-op because
the worktree had no assets/build output; it is not cited as evidence. The real restore/build run
was:

```text
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c DebugOpt \
  --filter FullyQualifiedName~Solreign \
  -m:1 -nodeReuse:false -p:UseSharedCompilation=false
```

Result:

- **474 passed, 0 failed, 4 skipped, 478 total**
- duration: **10 minutes 32 seconds**
- the four skips were the same held-path categories present in the release-train evidence

This is one additional passing fixture over the release candidate's prior 473/0/4 result and
shows no composed regression.

## Known deferred issue

The store's integer `last_round_id` cannot safely be strengthened to “reject every older ID”
without defining behavior when the main game database resets but the separate Season Ledger
survives. That question is deliberately held for a durable identity/schema design rather than
being guessed in a test-evidence lane.
