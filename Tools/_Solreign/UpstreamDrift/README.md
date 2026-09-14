# SOLREIGN upstream-drift audit

This local operator tool advances SR-W-006, SR-W-007, and SR-W-008 without
performing an upstream sync. It compares two already-present Git commits,
classifies upstream-only changed paths for human review, identifies
conservative path overlap, and records the parent repository's RobustToolbox
gitlink pointers.

It replaces the behavior of the historical
`auto-value/sr-w-006-upstream-sync` rehearsal. That script fetched a remote,
created and checked out a branch, attempted a merge, invoked `dotnet test`,
and could exit before cleanup. Do not use it as a read-only audit.

## Usage

Complete-history audit:

```bash
/usr/bin/python3 -B Tools/_Solreign/UpstreamDrift/upstream_drift.py \
  --repo . \
  --base-ref refs/heads/master \
  --upstream-ref refs/remotes/upstream/master \
  --format json
```

The default fails closed when the repository is shallow. A bounded local
inventory may be requested explicitly:

```bash
/usr/bin/python3 -B Tools/_Solreign/UpstreamDrift/upstream_drift.py \
  --repo . \
  --base-ref refs/heads/master \
  --upstream-ref refs/remotes/upstream/master \
  --allow-incomplete-history \
  --format markdown
```

Incomplete-history output is always marked `INCOMPLETE_HISTORY`, says its
comparison basis is `reachable-local-objects-only`, and cannot satisfy the
quarterly sync-rehearsal acceptance test.

The tool writes successful reports only to standard output and structured
errors only to standard error. It has no fetch, remote query, checkout, merge,
worktree, submodule recursion, build, package, launcher, hub, deploy, or
production path.

## Contract

- Inputs must be full 40- or 64-character object IDs or fully qualified
  `refs/heads/`, `refs/remotes/`, or `refs/tags/` names.
- Both inputs are resolved to commit IDs before analysis.
- Upstream-only means reachable from the upstream commit and not reachable
  from the base commit.
- Merge commits are compared with their first parent.
- Rename detection is disabled so path accounting is deterministic.
- Local `diff.ignoreSubmodules` configuration cannot hide gitlink changes.
- Partial-clone repositories fail closed and Git lazy fetching is disabled.
- Repository paths containing symlink components fail closed.
- The report makes no positive history-completeness claim; it records only the
  shallow bit and its local comparison basis.
- Commits, paths, categories, flags, and rule IDs are sorted.
- JSON uses `solreign.upstream-drift/v1` and contains no runtime timestamp or
  absolute repository path.
- Markdown is rendered from the JSON model and escapes controls, bidi
  controls, backslashes, and table delimiters.
- The default bounds are 5,000 divergent commits, 50,000 cumulative
  divergent-commit file changes, 64 MiB of cumulative Git output, 30 seconds
  per Git child, 120 seconds for the whole audit, and 16 MiB for the rendered
  report. Git output is drained through capped readers and JSON/Markdown is
  bounded before stdout emission. Exceeding any bound terminates the owned Git
  process group where applicable and fails instead of truncating. Git failure
  details and structured error output have separate bounds, and process
  termination itself cannot wait indefinitely.
- RobustToolbox analysis stops at the two parent-repository gitlink objects.
  It does not inspect or compare engine commits.

The built-in path rules are review-routing heuristics. They do not establish
patch safety, compatibility, security correctness, vulnerability absence,
license provenance, build health, launcher or hub compatibility, or deploy
approval. Unclassified paths remain explicit.

## Verification

```bash
/usr/bin/python3 -B -m unittest \
  Tools._Solreign.UpstreamDrift.tests.test_upstream_drift
```

The fixture suite uses real temporary Git repositories. It covers exact
divergence, missing refs, shallow and unrelated history, explicit incomplete
history, path classification, limits, conservative overlap, a real gitlink
delta, deterministic JSON, the CLI, PATH-substituted Git refusal, audit-wide
deadline and cumulative-output limits, strict repository/ref admission, safe
Markdown rendering, and material review flags. It performs no .NET build.
