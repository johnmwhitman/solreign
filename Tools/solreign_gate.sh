#!/usr/bin/env bash
# SOLREIGN standing pre-push gate.
#
# STATUS: MUTATION-PROVED BOTH DIRECTIONS 2026-07-26 — this is the STANDING pre-push gate.
# Receipt: docs/receipts/GATE-MUTATION-PROOF-2026-07-26.md. Watched RED on: a bogus parity FQN
# (zero-match guard fired), a bogus unit filter (zero-match guard fired), and a planted
# server-only [NetworkedComponent] (parity test failed, named the component and the first
# shifted NetId). Watched GREEN on the unmutated tree before and after. If you change the
# deciding logic here, re-prove it — a gate nobody has watched fail is decoration.
#
# WHY THIS EXISTS AS A SCRIPT: this repo's recurring failure is "the check that would have
# caught it existed and was not in the gate" — four separate incidents, including two total
# production outages. Keeping the gate as prose in a handoff let it drift. This file IS the gate.
#
# Hardened 2026-07-26 after an adversarial static review (grk lane, verified by hand):
#   * The unit stage had NO executed-test proof — a --filter matching nothing exits 0, so a
#     renamed namespace would have turned half the gate into a silent green no-op.
#   * Green never required "Failed: 0" — a runner that launders exit codes could pass.
#   * The parity filter was a substring match — any passing namesake could satisfy it. Now exact.
#   * The summary grep was tied to the English VSTest shape — locale is now pinned instead.
#   * A parity run that FAILED with Passed:0 was misdiagnosed as "filter matched nothing",
#     inviting the dangerous fix (loosening the guard). Diagnosis order corrected.
#
# Run from the repo root. Exits non-zero if any stage failed.
#
#   ./Tools/solreign_gate.sh
#
# NOT covered here (deliberate, so nobody mistakes green for shippable):
#   * The fail-closed six-shard SOLREIGN integration gate runs under its unchanged 20-minute
#     watchdog. Run it explicitly on any content change:
#       python3 -B Tools/_Solreign/IntegrationGate/solreign_integration_gate.py
#   * Release-config packaging (analyzer RA0049 is a warning in Debug, an ERROR in the Release
#     config Content.Packaging uses) and the client sandbox scan. Those live in
#     OPS deploy/build_verify.py and must run before any deploy.
#   * A YAMLLinter gutted to a no-op Main would still pass its stage (exit-code only). Accepted
#     residual: the linter's own fail-closed behavior is guarded by its anchor guard in-repo.

set -uo pipefail

# The summary assertions below read the English VSTest console summary. Pin the locale so a
# non-English CLI cannot turn a real pass into a false red (or worse, a format we can't read).
export DOTNET_CLI_UI_LANGUAGE=en

# Refuse to run anywhere but a repo root that has the projects this gate claims to test.
# Running from the wrong directory (or the wrong clone) must be a loud error, not a green.
for required in Content.Tests/Content.Tests.csproj \
                Content.IntegrationTests/Content.IntegrationTests.csproj \
                Content.YAMLLinter/Content.YAMLLinter.csproj; do
    if [ ! -f "$required" ]; then
        echo "GATE ERROR — $required not found. Run from the solreign repo root." >&2
        exit 2
    fi
done

LOGDIR="${SOLREIGN_GATE_LOGDIR:-.gstack/gate}"
mkdir -p "$LOGDIR" || { echo "GATE ERROR — cannot create $LOGDIR" >&2; exit 2; }
FAILED=0

# Positive proof a dotnet test run executed and fully passed. Reads the FINAL summary line
# (multi-host runs emit several; an early host's "Passed: 1" must not vouch for a later one).
# Requires: at least min_passed tests passed AND zero failed. A --filter matching NOTHING
# exits 0 with no summary at all — that must read as failure, never as green.
#   assert_test_summary <log> <min_passed> <stage-name>   -> sets FAILED, prints verdict
assert_test_summary() {
    local log="$1" min_passed="$2" name="$3"
    local passed failed
    passed=$(grep -E 'Passed:[[:space:]]+[0-9]+' "$log" | tail -1 | grep -oE 'Passed:[[:space:]]+[0-9]+' | grep -oE '[0-9]+' || echo "")
    failed=$(grep -E 'Failed:[[:space:]]+[0-9]+' "$log" | tail -1 | grep -oE 'Failed:[[:space:]]+[0-9]+' | grep -oE '[0-9]+' || echo "")
    if [ -z "$passed" ]; then
        echo "    FAIL — no test summary found: the $name filter matched no executed test."
        echo "    (Renamed or moved? Fix the filter, do not delete this check.)"
        FAILED=1; return 1
    fi
    if [ -n "$failed" ] && [ "$failed" -ne 0 ]; then
        echo "    FAIL — $name: $failed test(s) FAILED (passed: $passed). See $log"
        grep -E "Failed |Error Message" "$log" | head -5 | sed 's/^/    /'
        FAILED=1; return 1
    fi
    if [ "$passed" -lt "$min_passed" ]; then
        echo "    FAIL — $name: only $passed test(s) executed (floor: $min_passed). See $log"
        FAILED=1; return 1
    fi
    echo "    ok ($passed passed, ${failed:-0} failed)"
    return 0
}

# 1. Unit battery. Fast (~6s) and broad. Exit code AND summary proof — the same zero-match
#    disease guarded on parity applies here: an empty filter match exits 0.
UNIT_LOG="$LOGDIR/unit-solreign.log"
echo "─── unit-solreign"
dotnet test Content.Tests --filter _Solreign > "$UNIT_LOG" 2>&1
UNIT_RC=$?
if [ $UNIT_RC -ne 0 ]; then
    echo "    FAIL (exit $UNIT_RC) — see $UNIT_LOG"
    tail -5 "$UNIT_LOG" | sed 's/^/    /'
    FAILED=1
else
    assert_test_summary "$UNIT_LOG" 1 "unit battery"
fi

# 2. Networked-component NetId parity. THE CATASTROPHE GATE.
#    22 server-only [NetworkedComponent]s once shifted every NetId after the first divergence;
#    the client then deleted TransformComponent from live entities. ~5,593 failing tests, an
#    engine-level stack trace naming no Solreign file, one full day to root-cause.
#    The guard lives in Content.IntegrationTests, which the _Solreign filter above NEVER opens,
#    so it must be invoked as its own project or it does not run at all.
#    EXACT FQN match — a substring (~) filter could be satisfied by a passing namesake while
#    the real guard is renamed away.
PARITY_FQN="Content.IntegrationTests.Tests._Solreign.NetworkedComponentParityTest.ServerAndClientRegisterTheSameNetworkedComponents"
PARITY_LOG="$LOGDIR/netid-parity.log"
echo "─── netid-parity"
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj \
    --filter "FullyQualifiedName=$PARITY_FQN" \
    > "$PARITY_LOG" 2>&1
PARITY_RC=$?
# Diagnosis order matters: a run that executed and FAILED (Passed: 0, Failed: >=1) must be
# reported as PARITY BROKEN — misreporting it as "filter matched nothing" invites loosening
# the zero-match guard, the exact wrong fix. assert_test_summary checks Failed before Passed-floor.
if ! assert_test_summary "$PARITY_LOG" 1 "NetId parity"; then
    : # verdict + FAILED already set
elif [ $PARITY_RC -ne 0 ]; then
    echo "    FAIL (exit $PARITY_RC) — summary read green but the runner exited red; see $PARITY_LOG"
    FAILED=1
fi

# 3. Prototype/YAML validation. Exit-code stage (see NOT covered note on its residual risk).
YAML_LOG="$LOGDIR/yaml-linter.log"
echo "─── yaml-linter"
dotnet run --project Content.YAMLLinter -c DebugOpt > "$YAML_LOG" 2>&1
YAML_RC=$?
if [ $YAML_RC -ne 0 ]; then
    echo "    FAIL (exit $YAML_RC) — see $YAML_LOG"
    tail -5 "$YAML_LOG" | sed 's/^/    /'
    FAILED=1
else
    echo "    ok"
fi

echo
if [ $FAILED -ne 0 ]; then
    echo "GATE RED — do not push. Logs in $LOGDIR/"
    exit 1
fi
echo "GATE GREEN — unit + NetId parity + YAML linter."
echo "Reminder: full integration suite and build_verify.py are NOT in this gate."
exit 0
