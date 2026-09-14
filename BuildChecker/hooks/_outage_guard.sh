#!/usr/bin/env bash
# Shared body for the SOLREIGN 2026-07-28 outage guard.
# Sourced/invoked by both the pre-commit and pre-merge-commit hooks.
#
# WHAT IT GUARDS
# --------------
# 2026-07-28: an upstream merge PRESERVED two files upstream had DELETED
# (Catalog/uplink_catalog.yml, AmbientMusic/rules.yml). Every uplink prototype was
# then defined twice and no client could boot. `git merge -s ours`, union conflict
# resolution, and — the actual path taken — a modify/delete conflict resolved by
# keeping OUR side all preserve deleted paths. A green `--filter _Solreign` unit
# battery does NOT catch it: it never loads the full prototype set the way boot does.
#
# 🔑 TWO DESIGN SCARS — BOTH PROVEN BY EXPERIMENT 2026-07-29. READ BEFORE EDITING.
#
# SCAR 1 — MERGE_HEAD is absent on automatic merge commits.
#   v1 of this guard opened with:
#       if ! git rev-parse --verify -q MERGE_HEAD; then exit 0; fi
#   It never fired. git only writes MERGE_HEAD when a merge STOPS (conflict, or
#   --no-commit). On a clean automatic merge the hook ran, saw no MERGE_HEAD, and
#   silently passed. A debug hook printed "MERGE_HEAD ABSENT" during `git merge`.
#   => The primary check must need NO refs.
#
# SCAR 2 — pre-merge-commit does NOT fire on a hand-resolved merge.
#   The real 07-28 path is: modify/delete CONFLICT -> human resolves -> `git commit`.
#   That commit runs **pre-commit**, never pre-merge-commit. Proven: with only
#   pre-merge-commit installed, a wrongly-resolved merge committed 6 duplicate ids
#   with total silence. => The guard MUST be installed on pre-commit as well.
#
# Net: the (kind,id) collision scan reads the working tree directly (~0.4s over
# 21,992 prototypes), needs no refs, and catches the SYMPTOM however it arrived.
# No path through this guard exits 0 without having actually checked something.
set -uo pipefail

if [ "${SOLREIGN_SKIP_DELETION_CHECK:-0}" = "1" ]; then
    echo "⚠️  ${GUARD_CALLER:-guard}: outage guard SKIPPED via SOLREIGN_SKIP_DELETION_CHECK=1" >&2
    exit 0
fi

REPO_ROOT="$(git rev-parse --show-toplevel)"
PROTOS="$REPO_ROOT/Resources/Prototypes"

# Not the game repo — nothing to guard.
[ -d "$PROTOS" ] || exit 0

find_script() {
    local name="$1" cand
    for cand in \
        "$REPO_ROOT/Tools/_Solreign/$name" \
        "${SOLREIGN_OPS_DIR:-}/deploy/$name" \
        "$HOME/AI/solreign-trees/rel-ops/deploy/$name" \
        "$REPO_ROOT/deploy/$name" ; do
        if [ -n "$cand" ] && [ -f "$cand" ]; then echo "$cand"; return 0; fi
    done
    return 1
}

COLLISIONS="$(find_script check_prototype_collisions.py || true)"
DELETIONS="$(find_script check_upstream_deletions.py || true)"

# Fail CLOSED — a missing guard is exactly how 2026-07-28 happened.
if [ -z "$COLLISIONS" ]; then
    echo "⛔ REFUSED: cannot find check_prototype_collisions.py — the outage guard." >&2
    echo "   Set SOLREIGN_OPS_DIR to the ops repo, or bypass deliberately with" >&2
    echo "   SOLREIGN_SKIP_DELETION_CHECK=1 if you accept the 2026-07-28 risk." >&2
    exit 1
fi

MERGING=0
git rev-parse --verify -q MERGE_HEAD >/dev/null && MERGING=1

# On an ordinary commit, only scan when prototypes are actually staged. During a
# merge, always scan — that is the dangerous case and it is worth 0.4s.
if [ "$MERGING" = "0" ]; then
    if ! git diff --cached --name-only | grep -q '^Resources/Prototypes/'; then
        exit 0
    fi
fi

echo "${GUARD_CALLER:-guard}: scanning tree for (kind,id) prototype collisions…"
python3 "$COLLISIONS" "$PROTOS"
if [ $? -ne 0 ]; then
    cat >&2 <<'EOF'

⛔ REFUSED: DUPLICATE PROTOTYPE IDS (listed above).

This is the 2026-07-28 outage exactly: a file upstream deleted survived beside its
split replacement, the same (kind, id) is defined twice, and the client cannot boot.
Deletions carry as much signal as additions on a fast-moving upstream.

FIX: remove the surviving stale path(s), then commit again.
  git rm <path>...

If you are certain (you almost never are):
  SOLREIGN_SKIP_DELETION_CHECK=1 git commit ...
EOF
    exit 1
fi

# 🔑 SCAR 3 — do NOT run check_upstream_deletions.py from here. Proven 2026-07-29:
#   it resolves `HEAD`, which at commit time is still the PRE-merge commit, so it
#   reports the stale monolith as surviving even after you correctly `git rm`'d it
#   from the index. It BLOCKED THE CORRECT RESOLUTION — a false positive that
#   punishes the right fix and trains people to reach for the override.
#   That script's documented job is PRE-merge ("afterwards the merge-base becomes
#   the upstream tip and the check goes blind"). Run it before you merge:
#     python3 deploy/check_upstream_deletions.py <pre-merge-master> <upstream-tip> --repo <server>
#   At commit time the collision scan above is the correct instrument: it reads the
#   actual tree being committed and detects the symptom regardless of provenance.

echo "${GUARD_CALLER:-guard}: clean — no duplicate prototype ids."
exit 0
