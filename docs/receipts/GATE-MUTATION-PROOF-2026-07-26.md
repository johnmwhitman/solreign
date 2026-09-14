# Tools/solreign_gate.sh — mutation proof receipt (2026-07-26)

Tree: master `f666626ea7` (pushed) + the hardened gate script in this commit.
Machine: 15-core, no competing dotnet processes during any run.

## Why the gate was hardened first

An adversarial static review (grk lane, findings verified by hand against the script text —
raw output in `.gstack/lanes/grk-gate-review.txt`, gitignored) found the draft could lie green:

1. **P0 — unit stage had no executed-test proof.** `dotnet test --filter` matching NOTHING
   exits 0, so a renamed `_Solreign` namespace would have silently skipped half the gate.
   The draft guarded this disease on the parity stage and reintroduced it on the unit stage.
2. **P0 — parity filter was a substring match** (`FullyQualifiedName~`). Any passing namesake
   (helper class, stub) would satisfy the guard while the real test was renamed away. Now exact.
3. **P1 — green never required `Failed: 0`.** A runner that launders exit codes could pass with
   failures in the log. Both test stages now parse the final summary: `Failed: 0` AND `Passed >= 1`.
4. **P1 false-red — summary grep was tied to the English VSTest shape.** Locale is now pinned
   (`DOTNET_CLI_UI_LANGUAGE=en`) instead of hoping.
5. **P2 — a parity run that FAILED with `Passed: 0` was misdiagnosed as "filter matched
   nothing"**, inviting the dangerous fix (loosening the zero-match guard). `assert_test_summary`
   checks Failed before the Passed floor, so a real failure is reported as a real failure.
6. Repo-root assertion added: wrong CWD is now exit 2, not a confusing red (or a wrong-repo green).

Accepted residual (documented in the script header): a YAMLLinter gutted to a no-op `Main`
still passes its stage; the linter's own fail-closed behavior is guarded in-repo by its
anchor guard.

## The proof runs — all watched, exits tested directly

| run | mutation | expected | observed | exit |
|---|---|---|---|---|
| baseline | none | GREEN | GREEN — unit 2515/0, parity 1/0 (exact FQN matched), linter ok | 0 |
| (a) | parity FQN → `NetworkedComponentParityTestRENAMED.NoSuchTest` | RED, zero-match message | `FAIL — no test summary found: the NetId parity filter matched no executed test.` | 1 |
| (a2) | unit filter → `_SolreignNoSuchNamespaceXX` | RED, zero-match message | `FAIL — no test summary found: the unit battery filter matched no executed test.` | 1 |
| (b) | `[NetworkedComponent]` planted on server-only `SolreignCuriosityExamineComponent` | RED, parity-broken message | `FAIL — NetId parity: 1 test(s) FAILED` — log names `Registered on SERVER only (1): SolreignCuriosityExamine` and `First component whose NetId shifts because of it: SolreignHotPotato` | 1 |
| restore | mutation reverted | GREEN | GREEN (see caveat below) | 0 |

Every mutation was reverted; `git status` confirmed a clean `Content.Server/` before the
restore run. The gate decides from log files and `$?` tested directly — no pipes on any
deciding command (the gate-pipe-swallows-exit-code scar).

## ⚠️ A scar re-confirmed during the proof: mtime-preserving restore = stale binaries

Restoring the mutated file with `mv file.bak file` preserved the file's ORIGINAL mtime, so
MSBuild considered the MUTATED build up-to-date and the first restore run stayed RED against
stale binaries. `touch` after restore fixed it. Lesson: after reverting a file by rename/copy
that preserves timestamps, `touch` it — incremental build trusts mtimes, not content.
(This is also the standing residual risk #8 from the static review: the gate cannot see
stale-binary lies; it builds by default, which is the mitigation. Never add `--no-build`.)

## Standing usage

```
bash Tools/solreign_gate.sh    # from repo root; exit 0 = pushable (unit + parity + linter)
```

NOT covered, run separately: full `Content.IntegrationTests --filter _Solreign` on any content
change (mind the 20-minute pool watchdog — see below), and OPS `build_verify.py` before deploys.

## Related fact established the same session: the 20-minute integration cliff

`Content.IntegrationTests/PoolManagerTestEventHandler.cs` hard-codes a 20-minute total
watchdog; at 21 minutes it `Environment.FailFast`s. Any run slower than 20m produces the
signature previously misread as regression twice: ~140–160 paired NullReference ("Pool manager
has not been initialized") + InvalidOperation failures, first failure slow then an instant
cascade, ~0 real asserts. A 142-fail (contended) and a 162-fail (semi-quiet, 20m31s) run both
had this shape; the content tree was byte-identical to one that ran 422/0/4 in 15m52s.
Before reading integration failures as content, check total duration against the cliff.
