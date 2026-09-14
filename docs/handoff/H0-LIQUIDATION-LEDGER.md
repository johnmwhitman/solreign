# SOLREIGN H0 Liquidation Ledger

## 2026-07-24 — S0-02 `_Solreign` test-filter control

**Verdict:** `FILTER BEHAVIOR ESTABLISHED / S0-02 NUMERIC ACCEPTANCE BLOCKED / CURRENT-MASTER LAND CONTROL RED`

This receipt observes GAME `master` at
`78344ca922f705f7d66189d1b8c24d6fe5ba20c6`. The worktree was clean during
the final `--no-build` invocations; their binary was produced earlier with
the temporary workaround described below. No GAME source, test source,
project, CI, manifest, or runtime configuration change is included in this
packet.

### Working filter and measured subset

```sh
dotnet test --no-build -c DebugOpt Content.Tests/Content.Tests.csproj \
  --filter "FullyQualifiedName~Content.Tests._Solreign." \
  -m:1 -nodeReuse:false -p:UseSharedCompilation=false -- NUnit.ConsoleOut=0
```

Observed on 2026-07-24:

- Failed: **1**
- Passed: **2,367**
- Skipped: **2**
- Total: **2,370**
- Result: process exit `1`

The same binary was also run without a filter:

```sh
dotnet test --no-build -c DebugOpt Content.Tests/Content.Tests.csproj \
  -m:1 -nodeReuse:false -p:UseSharedCompilation=false -- NUnit.ConsoleOut=0
```

Observed on 2026-07-24:

- Failed: **1**
- Passed: **2,786**
- Skipped: **3**
- Total: **2,790**
- Result: process exit `1`

The expression therefore selects a strict subset of this exact binary's
**2,790**-test inventory, and its measured subset count is `N = 2,370`.
However, canonical S0-02 literally requires `N < 2,325`. That ceiling was
written against an older 2,325-test full inventory and is now stale, but
this evidence packet cannot silently rebaseline it. S0-02's filter behavior
is established; its numeric acceptance remains blocked pending an
authority-level rebaseline.

The selected run is intentionally not described as green. It exposed one
failing SOLREIGN reachability assertion covering six current-master prototype
orphans:

- `SolreignCorporateProjectConsole`
- `SolreignFleetOperationScheduler`
- `SolreignZooBioFeeder`
- `SolreignZooContainmentConsole`
- `SolreignZooContainmentEmitter`
- `SolreignZooRecoveryPad`

The test binary was locally built from this worktree while diagnosing the
control. Its `Content.Tests.dll` SHA-256 was
`2033f8d9307079b6f7391c4031205681af27badfe8c1e148c7cac05447589908`.
Test sources were unchanged from the observed commit, but a temporary,
uncommitted XAML workaround was required to produce the dependency build.
That workaround was removed before this receipt was authored. Consequently
the count is valid discovery evidence, but the binary is not evidence of a
clean-current-master green land gate.

### Fresh current-master build control

```sh
dotnet build Content.Tests/Content.Tests.csproj -c DebugOpt \
  -m:1 -nodeReuse:false -p:UseSharedCompilation=false --no-restore
```

Observed on the clean worktree:

- Exit: `1`
- Warnings: `266`
- Errors: `2`
- Blocking root error:

```text
Content.Client._Solreign.Corporate.Projects.UI.SolreignCorporateProjectWindow.xaml:
Unable to resolve suitable regular or attached property Wrap on type
Robust.Client.UserInterface.Controls.Label Line 22, position 18.
```

The second reported error is the resulting `CompileRobustXaml` command failure.
NuGet vulnerability-feed warnings were also present because the feeds were
unreachable, but they were not the build failure.

### Disposition

- S0-02's exact filter behavior and measured subset count are established,
  but its literal `N < 2,325` numeric acceptance is not satisfied.
- The clean-current-master land control is **RED**, not green.
- No source repair is bundled into this knowability packet; doing so would
  expand the ratified documentation slice into product repair.
- Any XAML or orphan remediation requires separate authorization and review.
- No merge, push, deploy, restart, activation, publication, hub, launcher, or
  production action was performed.
