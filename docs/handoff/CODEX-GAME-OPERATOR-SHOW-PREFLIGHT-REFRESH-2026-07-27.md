# Codex GAME operator show-preflight refresh handoff

Date: 2026-07-27

Branch: `codex/game-operator-preflight-refresh-20260727`

Base: GAME `master` at
`a4fbb1758644d5827a4d92348c0767e94481ff62`

Historical requirements source:
`0b48fd8ee1c5ece6f1bbc2a4fe51d408de28ecda`

Disposition: **LOCAL REVIEW CANDIDATE. NOT MERGED, PUSHED, DEPLOYED, OR
ACTIVATED. NOT READINESS AUTHORITY.**

## Outcome

This slice rebuilds the useful historical admin-only
`solreignshowpreflight` command on current clean GAME master. It reports one
bounded, sanitized, observation-only snapshot:

- selected map identifier and display name;
- connected-player count;
- active game-rule count, with rule identities withheld; and
- an exact allowlist of fourteen server-owned SOLREIGN boolean feature flags.

The command does not start or stop rules, mutate CVars, inspect player
identities, query the Season Ledger, invoke Director, perform I/O, or decide
whether a show may proceed. Its second output line is:
`OBSERVATION ONLY — NOT GO/READINESS AUTHORITY`.

## Current-master allowlist reconciliation

The historical implementation was based on GAME
`19e83353b91d25f148c0f19f0ee0005efd1c26c2` and allowlisted ten flags. Current
master adds two show-relevant SERVERONLY boolean gates, both now included:

- `solreign.fx.world_feedback_observe`
- `solreign.kart_repeatable_heats_enabled`

Independent review then found that observing only
`solreign.fx.world_feedback_observe` could present an incomplete FX state.
World feedback depends on a three-gate tuple, now reported together:

- master: `solreign.fx.cue_v1`
- delivery: `solreign.fx.world_feedback_v1`
- observation: `solreign.fx.world_feedback_observe`

The master gate is `CVar.SERVER | CVar.REPLICATED`; delivery and observation
are `CVar.SERVERONLY`. The allowlist contract therefore requires every entry
to be server-owned (`SERVER` or `SERVERONLY`) while rejecting `CLIENTONLY` and
`CONFIDENTIAL`.

The implementation still uses an explicit list. It does not enumerate
configuration. Tests require the exact fourteen names, uniqueness, the
server-owned/non-client/nonconfidential contract above, and rejection of token,
URL, path, Director-token, and Ledger-path names.

## Safety and mutation controls

- `AdminFlags.Admin` is required by the command registration attribute.
- Any argument is rejected before injected dependencies are read.
- Output is capped at 32 lines and 160 characters per line.
- Control/whitespace runs are collapsed and Unicode format characters are
  removed, preventing newline, bidi, and zero-width presentation tricks.
- Invalid negative population and rule counts are clamped to zero.
- Rule identities are never passed to the formatter; only the count is used.
- The paired-server integration test snapshots run level, round ID, selected
  map, player count, every active rule UID, and all fourteen CVar values before
  execution, then proves they are unchanged afterward.

## Strict TDD evidence

RED, before production code:

```text
Content.Tests/_Solreign/Operations/SolreignShowPreflightCommandTests.cs(1,32):
error CS0234: The type or namespace name 'Operations' does not exist in the
namespace 'Content.Server._Solreign'

Content.Tests/_Solreign/Operations/SolreignShowPreflightCommandTests.cs(13,16):
error CS0246: The type or namespace name 'SolreignShowPreflightCommand' could
not be found
```

GREEN, focused unit target:

```text
dotnet test Content.Tests/Content.Tests.csproj -c DebugOpt --no-restore -m:1 \
  -nodeReuse:false -p:UseSharedCompilation=false \
  --filter FullyQualifiedName~SolreignShowPreflight -- NUnit.ConsoleOut=0

Passed: 6, Failed: 0, Skipped: 0, Total: 6
```

Corrective RED for the independently reported incomplete FX tuple:

```text
SignatureCVarAllowlistIsExactServerOwnedAndContainsNoClientOrConfidentialConfiguration
Expected is System.String[14], actual is System.String[12]
First non-matching item at index [3]: "solreign.fx.cue_v1"

Failed: 1, Passed: 5, Skipped: 0, Total: 6
```

Adding only the master and delivery definitions to the production allowlist
closed this failure. The final focused unit result remains 6/6.

The first GREEN attempt compiled but the sandbox denied the test runner's
localhost control socket with `SocketException (13): Permission denied`.
The exact command was reproduced once outside the sandbox and passed; no
retry loop was used.

GREEN, focused paired-server integration target:

```text
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj \
  -c DebugOpt --no-restore -m:1 -nodeReuse:false \
  -p:UseSharedCompilation=false \
  --filter FullyQualifiedName~SolreignShowPreflight -- NUnit.ConsoleOut=0

Passed: 1, Failed: 0, Skipped: 0, Total: 1, Duration: 15 s
```

The fresh worktree used the exact current RobustToolbox submodule SHA
`960edb32c4dd417496e4667177625d8c3cb14f7e`, seeded locally from the clean
`rel-build` checkout. No submodule network retry occurred.

## Root-owned broad qualification and Crashpad hold

After the focused lane evidence above, the root integrator completed the full
`Content.Tests` target:

```text
Passed: 3012, Failed: 0, Skipped: 3
```

This is root-owned evidence. The lane did not independently reproduce it.

A sandboxed restore in this worktree started at 04:01:31.587Z:

```text
dotnet restore Content.IntegrationTests/Content.IntegrationTests.csproj --ignore-failed-sources -p:UseSharedCompilation=false
```

It became silent after `Determining projects to restore...` and was cancelled
in the approximately 04:04:17Z–04:05:06Z window. The command omitted both
`-m:1` and `--disable-parallel`. Storage placed the Crashpad storm at
04:01:32Z–04:05:03Z. The timing and worktree/PWD correlation make this
restore the high-confidence correlated producer. They do not prove which
managed exception caused the CoreCLR aborts.

The root integrator subsequently started exactly one broad SOLREIGN
integration command at 04:08:05Z, after the crash storm had ended, with no
concurrent build or retry loop:

```text
SOLREIGN_INTEGRATION_WATCHDOG_MINUTES=30 dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c DebugOpt --no-build --no-restore -m:1 -nodeReuse:false -p:UseSharedCompilation=false --filter FullyQualifiedName~_Solreign -- NUnit.ConsoleOut=0
```

The broad integration command was cancelled after the urgent storage
advisory. Expected skips had been observed before cancellation, but the run
did not complete. Its later start time establishes that it was not the
Crashpad-storm producer. Therefore:

- there is **no broad-integration pass or fail claim**;
- cancellation is an operational safety action, not test evidence;
- no dotnet/MSBuild producer from this lane remains active; and
- further dotnet execution or retry is under a **HARD HOLD pending root-cause
  diagnosis**.

Crashpad was not inspected or mutated by this lane.

### Storage correlation addendum

Storage performed a read-only sample of Crashpad dump
`d91afd1e-6520-47bb-8aa5-d7a02f1c022d.dmp` (PID 11503, mtime
2026-07-26 23:05:03-0500, 12 threads). Its native chain was:

```text
__pthread_kill -> abort -> libcoreclr!PROCAbort -> TerminateProcess
-> SfiNextWorker -> DispatchExSecondPass -> DispatchRethrownManagedException
-> IL_Rethrow
```

This establishes a CoreCLR abort while dispatching a rethrown managed
exception. The dump lacks the managed symbols and data needed to identify the
exception or its managed frames, so it does not establish a root cause.

At the same PID and time, PID 11503 had sandbox-denied
`notification_center`/`logd` lookups but no `securityd` `-25294` event.
Observed keychain bursts occurred later under different PIDs. A keychain or
security-service causal explanation is therefore unproven and retired from
this handoff.

This correlation does not lift the **HARD HOLD**: do not retry dotnet work
until the managed-exception cause can be diagnosed safely. This lane performed
no dotnet execution and no Crashpad inspection or mutation for this addendum.

## Owned files

- `Content.Server/_Solreign/Operations/SolreignShowPreflightCommand.cs`
- `Content.Tests/_Solreign/Operations/SolreignShowPreflightCommandTests.cs`
- `Content.IntegrationTests/Tests/_Solreign/SolreignShowPreflightCommandIntegrationTest.cs`
- this handoff

## Residual evidence boundaries

- Full `Content.Tests` completed under the root integrator at 3012 passed,
  0 failed, 3 skipped. Complete SOLREIGN integration remains unknown because
  the single broad command was cancelled under the Crashpad safety hold.
- The paired-server test does not capture output through a remote admin
  session. Authorization is proven here by the admin command contract rather
  than a two-session remote-console test.
- Active-rule count can correlate with hidden round state in a very small
  population. This is accepted only inside the admin-only surface; identities
  remain withheld.
- This report is review evidence, not merge, release, deploy, activation, or
  show-readiness authority.

## Independent-review P2 disposition

**CLOSED in the second branch commit.** Preflight no longer presents the
observation flag without the coupled FX master and delivery gates. The
correction changes only the explicit read-only allowlist, its behavioral test,
and this handoff; it does not change any CVar declaration, default, or value.
