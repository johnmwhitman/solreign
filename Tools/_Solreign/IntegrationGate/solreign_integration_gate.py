from __future__ import annotations

import argparse
from dataclasses import dataclass
import hashlib
import json
import math
import os
from pathlib import Path
import re
import subprocess
import sys
import time
from typing import Callable, Sequence, TextIO

EXPECTED_FIXTURE_COUNT = 92
# 2026-08-02 aggregate re-pin, in the STRICT direction (470+3 -> 473+0). The 3 "expected skips"
# were never Assert.Ignore skips: they were dirty-dispose WARNINGS that NUnit's default mapping
# displayed as Skipped. Two root causes were fixed on rel/integration-gate-verify-20260802 —
# an unfaithful StationAudit round-start test seam (84140938c66) and a leaked always-fail
# persistent-block writer stub in the wingmate fixture (9bb3d3e1cb1) — after which the full
# suite runs 473 passed / 0 skipped / 0 failed. Tolerating zero skips is a tightening; any
# future skip is a warning someone has not looked at.
# 2026-08-02 (later): 473 -> 475. FirstShiftSpawnPromptSystem added two integration tests
# (SpawnPrompt_RealSpawnClaims_ThenOncePerAccountEver, SpawnPrompt_SuppressedOnSilent_CVarOff_
# AndVeteran) in the existing FirstShiftSystemIntegrationTest fixture — fixture count stays 92.
# Both were watched RED under deliberate mutations (guard-order inversion; Silent-guard deletion)
# before this pin moved.
# 2026-08-14: 475 -> 478. Post-pin mainline added the G6 deleted-anchor regression test
# (9608a17a96b) and two map-pool population cases (88ced2e2522). The canonical six-shard run
# executed 478 passed / 0 skipped; the integration candidate itself adds no test cases.
EXPECTED_PASSED = 478
EXPECTED_SKIPPED = 0
SHARD_COUNT = 6
SHARD_TARGET_SECONDS = 720
RECEIPT_SCHEMA = "solreign.integration-gate.receipt/v1"
# Hosted evidence showed count-blind round-robin overloaded shard 1. Keep the
# base algorithm stable and document only the smallest measured correction.
SHARD_ASSIGNMENT_OVERRIDES = {
    "Content.IntegrationTests.Tests._Solreign."
    "ProvidencePowerContractorSystemIntegrationTest": 3,
    # The same hosted shard varied from 619s to 1135s; XML timing attributed
    # the dominant growth to these co-located real-round fixtures. Separate
    # them onto the two lowest static-load shards.
    "Content.IntegrationTests.Tests._Solreign."
    "SolreignVampireFeedingCycleIntegrationTest": 4,
    "Content.IntegrationTests.Tests._Solreign."
    "SolreignRoundStartJobSpreadGateTest": 5,
}
PROJECT = Path("Content.IntegrationTests/Content.IntegrationTests.csproj")
TEST_ROOT = Path("Content.IntegrationTests/Tests/_Solreign")
LOG_ROOT = Path(".gstack/integration-gate")
EXTERNAL_SOLREIGN_SELECTOR = (
    "(FullyQualifiedName~_Solreign&"
    "FullyQualifiedName!~Content.IntegrationTests.Tests._Solreign.)"
)


class IntegrationGateError(RuntimeError):
    """The gate cannot establish trustworthy integration-test evidence."""


CLASS_DECLARATION = re.compile(
    r"\b(?P<access>public|internal)\s+"
    r"(?P<modifiers>(?:(?:abstract|sealed|static|partial|new)\s+)*)"
    r"class\s+(?P<name>[A-Za-z_]\w*)\b",
)
NAMESPACE_DECLARATION = re.compile(
    r"\bnamespace\s+(?P<name>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s*;"
)
NON_CODE = re.compile(
    r'@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|//[^\n]*|/\*.*?\*/',
    re.DOTALL,
)
SUMMARY_VALUE = re.compile(r"\b(?P<key>Passed|Failed|Skipped|Total):\s*(?P<value>\d+)\b")
TERMINAL_FAILURE = re.compile(
    r"\b(?:aborted|error|failed|incomplete|cancelled|canceled|terminated|crash(?:ed)?)\b",
    re.IGNORECASE,
)
PARTIAL_SUMMARY = re.compile(r"\b(?:passed|failed|skipped|total):", re.IGNORECASE)
POISON_EVIDENCE = re.compile(
    r"\b(?:watchdog|hard[- ]?stop|pool[- ]?poison(?:ed|ing)?)\b",
    re.IGNORECASE,
)
COMMIT_SHA = re.compile(r"[0-9a-f]{40}\Z")
SHA256 = re.compile(r"[0-9a-f]{64}\Z")
RECEIPT_FIELDS = {
    "schema",
    "candidate_sha",
    "inventory_sha256",
    "build_sha256",
    "shard",
    "fixtures",
    "filter",
    "elapsed_seconds",
    "summary",
}
SUMMARY_FIELDS = {"passed", "failed", "skipped", "total"}


@dataclass(frozen=True)
class VSTestSummary:
    passed: int
    failed: int
    skipped: int
    total: int


@dataclass(frozen=True)
class GateResult:
    summaries: tuple[VSTestSummary, ...]
    full_suite_accepted: bool


def discover_fixtures(test_root: Path) -> list[str]:
    """Return unique fully-qualified non-abstract ``*Test`` fixture classes."""
    fixtures: list[str] = []
    for path in sorted(test_root.rglob("*.cs")):
        contents = path.read_text(encoding="utf-8")
        if "[Test" not in contents:
            continue
        code = NON_CODE.sub("", contents)
        namespaces = [match.group("name") for match in NAMESPACE_DECLARATION.finditer(code)]
        if len(namespaces) != 1:
            raise IntegrationGateError(
                f"[Test] file must have exactly one file-scoped namespace: {path}"
            )
        declarations = [
            f"{namespaces[0]}.{match.group('name')}"
            for match in CLASS_DECLARATION.finditer(code)
            if "abstract" not in match.group("modifiers").split()
            and match.group("name").endswith("Test")
        ]
        if not declarations:
            raise IntegrationGateError(
                f"[Test] file has no non-abstract public/internal *Test fixture: {path}"
            )
        fixtures.extend(declarations)

    duplicates = sorted(name for name in set(fixtures) if fixtures.count(name) > 1)
    if duplicates:
        raise IntegrationGateError(
            f"duplicate integration fixture classes: {', '.join(duplicates)}"
        )
    return sorted(fixtures)


def assign_shards(fixtures: list[str]) -> list[list[str]]:
    """Assign fixtures deterministically, including measured load corrections."""
    shards = [[] for _ in range(SHARD_COUNT)]
    for index, fixture in enumerate(sorted(fixtures)):
        shards[index % SHARD_COUNT].append(fixture)
    for fixture, target_index in SHARD_ASSIGNMENT_OVERRIDES.items():
        if fixture not in fixtures:
            continue
        source = next(shard for shard in shards if fixture in shard)
        source.remove(fixture)
        shards[target_index].append(fixture)
    for shard in shards:
        shard.sort()
    return shards


def inventory_for_repository(repository_root: Path) -> list[list[str]]:
    fixtures = discover_fixtures(repository_root / TEST_ROOT)
    if len(fixtures) != EXPECTED_FIXTURE_COUNT:
        raise IntegrationGateError(
            f"expected exactly {EXPECTED_FIXTURE_COUNT} integration fixtures, found {len(fixtures)}"
        )
    shards = assign_shards(fixtures)
    assigned = [fixture for shard in shards for fixture in shard]
    if len(assigned) != len(set(assigned)) or set(assigned) != set(fixtures):
        raise IntegrationGateError("shard assignment does not cover every fixture exactly once")
    if any(not shard for shard in shards):
        raise IntegrationGateError("integration shard inventory contains an empty shard")
    return shards


def candidate_sha_for_repository(repository_root: Path) -> str:
    """Resolve the checked-out commit that receipt evidence must attest."""
    result = subprocess.run(
        ["git", "rev-parse", "--verify", "HEAD^{commit}"],
        cwd=repository_root,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        check=False,
    )
    candidate = (result.stdout or "").strip()
    if result.returncode != 0 or COMMIT_SHA.fullmatch(candidate) is None:
        raise IntegrationGateError("cannot resolve a normalized checked-out candidate commit")
    status = subprocess.run(
        [
            "git",
            "status",
            "--porcelain=v1",
            "--untracked-files=all",
            "--ignore-submodules=all",
        ],
        cwd=repository_root,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        check=False,
    )
    if status.returncode != 0:
        raise IntegrationGateError("cannot verify candidate worktree cleanliness")
    if (status.stdout or "").strip():
        raise IntegrationGateError("candidate worktree has tracked or nonignored source changes")
    submodule_status = subprocess.run(
        [
            "git",
            "submodule",
            "foreach",
            "--quiet",
            "--recursive",
            "git status --porcelain=v1 --untracked-files=all --ignore-submodules=all",
        ],
        cwd=repository_root,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        check=False,
    )
    if submodule_status.returncode != 0:
        raise IntegrationGateError("cannot verify candidate submodule cleanliness")
    if (submodule_status.stdout or "").strip():
        raise IntegrationGateError("candidate has dirty source inside a bound submodule")
    return candidate


def inventory_sha256(shards: Sequence[Sequence[str]]) -> str:
    canonical = json.dumps(
        {"fixture_count": EXPECTED_FIXTURE_COUNT, "shards": shards},
        sort_keys=True,
        separators=(",", ":"),
    ).encode("utf-8")
    return hashlib.sha256(canonical).hexdigest()


def build_manifest_sha256(build_manifest: Path) -> str:
    if not build_manifest.is_file():
        raise IntegrationGateError(f"build manifest does not exist: {build_manifest}")
    try:
        contents = build_manifest.read_bytes()
    except OSError as error:
        raise IntegrationGateError(f"cannot read build manifest: {error}") from error
    if not contents:
        raise IntegrationGateError("build manifest is empty")
    return hashlib.sha256(contents).hexdigest()


def _strict_json_object(pairs: list[tuple[str, object]]) -> dict[str, object]:
    parsed: dict[str, object] = {}
    for key, value in pairs:
        if key in parsed:
            raise IntegrationGateError(f"receipt JSON contains duplicate key: {key}")
        parsed[key] = value
    return parsed


def _test_filter(fixtures: Sequence[str], include_external: bool) -> str:
    filters = [f"FullyQualifiedName~{fixture}." for fixture in fixtures]
    if include_external:
        filters.append(EXTERNAL_SOLREIGN_SELECTOR)
    return "|".join(filters)


def parse_vstest_summary(output: str) -> VSTestSummary:
    """Parse the final complete VSTest summary line, rejecting partial output."""
    summaries: list[tuple[VSTestSummary, int]] = []
    offset = 0
    for line in output.splitlines(keepends=True):
        values = {
            match.group("key").lower(): int(match.group("value"))
            for match in SUMMARY_VALUE.finditer(line)
        }
        if {"passed", "failed", "skipped", "total"} <= values.keys():
            summary = VSTestSummary(
                passed=values["passed"],
                failed=values["failed"],
                skipped=values["skipped"],
                total=values["total"],
            )
            if summary.total != summary.passed + summary.failed + summary.skipped:
                raise IntegrationGateError(f"malformed VSTest summary: {line}")
            summaries.append((summary, offset + len(line)))
        offset += len(line)
    if not summaries:
        raise IntegrationGateError("no complete VSTest summary found in child output")
    summary, summary_end = summaries[-1]
    tail = output[summary_end:]
    terminal_failure = TERMINAL_FAILURE.search(tail)
    if terminal_failure is not None:
        raise IntegrationGateError(
            f"terminal VSTest output contains failure evidence: {terminal_failure.group(0)}"
        )
    if PARTIAL_SUMMARY.search(tail):
        raise IntegrationGateError("terminal VSTest output contains an incomplete summary")
    return summary


def _build_command() -> list[str]:
    return [
        "dotnet",
        "build",
        str(PROJECT),
        "-c",
        "DebugOpt",
        "-m:1",
        "-nodeReuse:false",
        "-p:UseSharedCompilation=false",
    ]


def _test_command(
    fixtures: Sequence[str],
    repository_root: Path,
    include_external: bool = False,
) -> list[str]:
    return [
        "dotnet",
        "test",
        str(PROJECT),
        "--no-build",
        "--no-restore",
        "-c",
        "DebugOpt",
        "-m:1",
        "-nodeReuse:false",
        "-p:UseSharedCompilation=false",
        "--filter",
        _test_filter(fixtures, include_external),
        "--",
        "NUnit.ConsoleOut=0",
        "NUnit.MapWarningTo=Failed",
        "NUnit.TestOutputXml=logs",
        f"NUnit.WorkDirectory={repository_root / LOG_ROOT / 'test_results'}",
    ]


def _execute(
    command: list[str],
    repository_root: Path,
    execute: Callable[..., object],
) -> object:
    child_environment = dict(os.environ)
    child_environment["DOTNET_CLI_UI_LANGUAGE"] = "en"
    return execute(
        command,
        cwd=repository_root,
        env=child_environment,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        check=False,
    )


def _validated_shard_summary(index: int, result: object, output: str) -> VSTestSummary:
    return_code = getattr(result, "returncode", None)
    if return_code != 0:
        raise IntegrationGateError(f"shard {index} exited with status {return_code}")
    poison = POISON_EVIDENCE.search(output)
    if poison is not None:
        raise IntegrationGateError(f"shard {index} output contains failure evidence: {poison.group(0)}")
    summary = parse_vstest_summary(output)
    if summary.failed != 0:
        raise IntegrationGateError(f"shard {index} reported {summary.failed} failed tests")
    if summary.passed == 0:
        raise IntegrationGateError(f"shard {index} reported zero passed tests")
    if summary.total == 0:
        raise IntegrationGateError(f"shard {index} reported zero executed tests")
    return summary


def _write_shard_receipt(
    receipt_directory: Path,
    candidate_sha: str,
    inventory_digest: str,
    build_digest: str,
    index: int,
    fixtures: Sequence[str],
    elapsed_seconds: float,
    summary: VSTestSummary,
) -> None:
    receipt_directory.mkdir(parents=True, exist_ok=True)
    destination = receipt_directory / f"shard-{index}.json"
    temporary = receipt_directory / f".shard-{index}.json.tmp"
    receipt = {
        "schema": RECEIPT_SCHEMA,
        "candidate_sha": candidate_sha,
        "inventory_sha256": inventory_digest,
        "build_sha256": build_digest,
        "shard": index,
        "fixtures": list(fixtures),
        "filter": _test_filter(fixtures, index == SHARD_COUNT),
        "elapsed_seconds": float(elapsed_seconds),
        "summary": {
            "passed": summary.passed,
            "failed": summary.failed,
            "skipped": summary.skipped,
            "total": summary.total,
        },
    }
    temporary.write_text(
        json.dumps(receipt, sort_keys=True, separators=(",", ":")) + "\n",
        encoding="utf-8",
    )
    temporary.replace(destination)


def _receipt_summary(
    receipt: object,
    expected_shard: int,
    expected_fixtures: Sequence[str],
    expected_candidate: str,
    expected_inventory: str,
    expected_build: str,
) -> VSTestSummary:
    if not isinstance(receipt, dict) or set(receipt) != RECEIPT_FIELDS:
        raise IntegrationGateError(f"shard {expected_shard} receipt has an invalid schema shape")
    if receipt["schema"] != RECEIPT_SCHEMA:
        raise IntegrationGateError(f"shard {expected_shard} receipt has an unsupported schema")

    candidate = receipt["candidate_sha"]
    if not isinstance(candidate, str) or COMMIT_SHA.fullmatch(candidate) is None:
        raise IntegrationGateError(f"shard {expected_shard} receipt has an invalid candidate commit")
    if candidate != expected_candidate:
        raise IntegrationGateError(f"shard {expected_shard} receipt is for a different candidate")

    inventory = receipt["inventory_sha256"]
    if not isinstance(inventory, str) or SHA256.fullmatch(inventory) is None:
        raise IntegrationGateError(f"shard {expected_shard} receipt has an invalid inventory digest")
    if inventory != expected_inventory:
        raise IntegrationGateError(f"shard {expected_shard} receipt has a different inventory")

    build = receipt["build_sha256"]
    if not isinstance(build, str) or SHA256.fullmatch(build) is None:
        raise IntegrationGateError(f"shard {expected_shard} receipt has an invalid build digest")
    if build != expected_build:
        raise IntegrationGateError(f"shard {expected_shard} receipt has a different build")

    shard = receipt["shard"]
    if type(shard) is not int or shard != expected_shard:
        raise IntegrationGateError(f"shard {expected_shard} receipt filename and body disagree")

    fixtures = receipt["fixtures"]
    if (
        not isinstance(fixtures, list)
        or any(not isinstance(fixture, str) for fixture in fixtures)
        or fixtures != list(expected_fixtures)
    ):
        raise IntegrationGateError(f"shard {expected_shard} receipt fixtures do not match inventory")

    expected_filter = _test_filter(expected_fixtures, expected_shard == SHARD_COUNT)
    if receipt["filter"] != expected_filter:
        raise IntegrationGateError(f"shard {expected_shard} receipt filter does not match inventory")

    elapsed = receipt["elapsed_seconds"]
    if (
        isinstance(elapsed, bool)
        or not isinstance(elapsed, (int, float))
        or not math.isfinite(elapsed)
        or elapsed < 0
        or elapsed > SHARD_TARGET_SECONDS
    ):
        raise IntegrationGateError(f"shard {expected_shard} receipt has invalid elapsed time")

    raw_summary = receipt["summary"]
    if not isinstance(raw_summary, dict) or set(raw_summary) != SUMMARY_FIELDS:
        raise IntegrationGateError(f"shard {expected_shard} receipt has an invalid summary shape")
    if any(type(raw_summary[field]) is not int or raw_summary[field] < 0 for field in SUMMARY_FIELDS):
        raise IntegrationGateError(f"shard {expected_shard} receipt has invalid summary counts")
    summary = VSTestSummary(
        passed=raw_summary["passed"],
        failed=raw_summary["failed"],
        skipped=raw_summary["skipped"],
        total=raw_summary["total"],
    )
    if summary.total != summary.passed + summary.failed + summary.skipped:
        raise IntegrationGateError(f"shard {expected_shard} receipt summary total is inconsistent")
    if summary.failed != 0 or summary.passed == 0 or summary.total == 0:
        raise IntegrationGateError(f"shard {expected_shard} receipt does not prove a green execution")
    return summary


def verify_receipts(
    receipt_directory: Path,
    repository_root: Path,
    build_manifest: Path,
) -> GateResult:
    """Fail closed unless six artifact receipts prove one complete candidate run."""
    if not receipt_directory.is_dir():
        raise IntegrationGateError(f"receipt directory does not exist: {receipt_directory}")
    expected_names = {f"shard-{index}.json" for index in range(1, SHARD_COUNT + 1)}
    actual_entries = list(receipt_directory.iterdir())
    actual_names = {entry.name for entry in actual_entries}
    if actual_names != expected_names or any(not entry.is_file() for entry in actual_entries):
        raise IntegrationGateError("receipt directory must contain exactly shard-1.json through shard-6.json")

    shards = inventory_for_repository(repository_root)
    candidate = candidate_sha_for_repository(repository_root)
    inventory_digest = inventory_sha256(shards)
    build_digest = build_manifest_sha256(build_manifest)
    summaries: list[VSTestSummary] = []
    for index, fixtures in enumerate(shards, start=1):
        path = receipt_directory / f"shard-{index}.json"
        try:
            receipt = json.loads(
                path.read_text(encoding="utf-8"),
                object_pairs_hook=_strict_json_object,
            )
        except (OSError, UnicodeError, json.JSONDecodeError) as error:
            raise IntegrationGateError(f"cannot parse shard {index} receipt: {error}") from error
        summaries.append(
            _receipt_summary(
                receipt,
                index,
                fixtures,
                candidate,
                inventory_digest,
                build_digest,
            )
        )

    passed = sum(summary.passed for summary in summaries)
    skipped = sum(summary.skipped for summary in summaries)
    total = sum(summary.total for summary in summaries)
    if (passed, skipped, total) != (
        EXPECTED_PASSED,
        EXPECTED_SKIPPED,
        EXPECTED_PASSED + EXPECTED_SKIPPED,
    ):
        raise IntegrationGateError(
            "receipt aggregate does not match required "
            f"{EXPECTED_PASSED} passed + {EXPECTED_SKIPPED} skipped = "
            f"{EXPECTED_PASSED + EXPECTED_SKIPPED}: got "
            f"{passed} passed + {skipped} skipped = {total}"
        )
    return GateResult(tuple(summaries), True)


def run_gate(
    repository_root: Path,
    shard: int | None = None,
    build: bool = True,
    receipt_directory: Path | None = None,
    build_manifest: Path | None = None,
    execute: Callable[..., object] = subprocess.run,
    monotonic: Callable[[], float] = time.monotonic,
    progress: Callable[[str], None] = lambda _: None,
) -> GateResult:
    """Build once, then run the requested inventory shards sequentially."""
    if "SOLREIGN_INTEGRATION_WATCHDOG_MINUTES" in os.environ:
        raise IntegrationGateError(
            "SOLREIGN_INTEGRATION_WATCHDOG_MINUTES is set; refusing a loosened gate"
        )
    all_shards = inventory_for_repository(repository_root)
    if shard is not None and not 1 <= shard <= SHARD_COUNT:
        raise IntegrationGateError(f"shard must be between 1 and {SHARD_COUNT}")
    if (receipt_directory is None) != (build_manifest is None):
        raise IntegrationGateError(
            "receipt evidence requires both a receipt directory and a build manifest"
        )
    selected = (
        [(shard, all_shards[shard - 1])]
        if shard is not None
        else list(enumerate(all_shards, start=1))
    )
    if receipt_directory is not None:
        if receipt_directory.exists() and not receipt_directory.is_dir():
            raise IntegrationGateError("receipt destination exists and is not a directory")
        for index, _ in selected:
            destinations = (
                receipt_directory / f"shard-{index}.json",
                receipt_directory / f".shard-{index}.json.tmp",
            )
            if any(path.exists() for path in destinations):
                raise IntegrationGateError(
                    f"shard {index} receipt destination is not fresh; refusing stale evidence"
                )
    candidate = (
        candidate_sha_for_repository(repository_root)
        if receipt_directory is not None
        else None
    )
    inventory_digest = (
        inventory_sha256(all_shards)
        if receipt_directory is not None
        else None
    )
    build_digest = (
        build_manifest_sha256(build_manifest)
        if build_manifest is not None
        else None
    )
    if build:
        progress("integration build started")
        build_result = _execute(_build_command(), repository_root, execute)
        if getattr(build_result, "returncode", None) != 0:
            raise IntegrationGateError(
                f"integration build exited with status {getattr(build_result, 'returncode', None)}"
            )
        progress("integration build accepted")

    log_directory = repository_root / LOG_ROOT
    log_directory.mkdir(parents=True, exist_ok=True)
    summaries: list[VSTestSummary] = []
    for index, fixtures in selected:
        progress(f"shard {index} started ({len(fixtures)} fixtures)")
        started_at = monotonic()
        child = _execute(
            _test_command(
                fixtures,
                repository_root,
                include_external=index == SHARD_COUNT,
            ),
            repository_root,
            execute,
        )
        elapsed_seconds = monotonic() - started_at
        output = getattr(child, "stdout", "") or ""
        if not isinstance(output, str):
            output = str(output)
        (log_directory / f"shard-{index}.log").write_text(output, encoding="utf-8")
        if elapsed_seconds > SHARD_TARGET_SECONDS:
            raise IntegrationGateError(
                f"shard {index} took {elapsed_seconds:.3f} seconds; "
                f"operating target is at most {SHARD_TARGET_SECONDS} seconds"
            )
        summary = _validated_shard_summary(index, child, output)
        summaries.append(summary)
        if receipt_directory is not None:
            assert candidate is not None and inventory_digest is not None and build_digest is not None
            if candidate_sha_for_repository(repository_root) != candidate:
                raise IntegrationGateError("candidate changed while shard evidence was executing")
            if build_manifest_sha256(build_manifest) != build_digest:
                raise IntegrationGateError("build manifest changed while shard evidence was executing")
            _write_shard_receipt(
                receipt_directory,
                candidate,
                inventory_digest,
                build_digest,
                index,
                fixtures,
                elapsed_seconds,
                summary,
            )
        progress(
            f"shard {index} accepted in {elapsed_seconds:.3f}s: "
            f"{summary.passed} passed, {summary.skipped} skipped"
        )

    full_suite = shard is None
    if full_suite:
        passed = sum(summary.passed for summary in summaries)
        skipped = sum(summary.skipped for summary in summaries)
        total = sum(summary.total for summary in summaries)
        if (passed, skipped, total) != (
            EXPECTED_PASSED,
            EXPECTED_SKIPPED,
            EXPECTED_PASSED + EXPECTED_SKIPPED,
        ):
            raise IntegrationGateError(
                "aggregate does not match required "
                f"{EXPECTED_PASSED} passed + {EXPECTED_SKIPPED} skipped = "
                f"{EXPECTED_PASSED + EXPECTED_SKIPPED}: got "
                f"{passed} passed + {skipped} skipped = {total}"
            )
    return GateResult(tuple(summaries), full_suite)


def _render_inventory(shards: Sequence[Sequence[str]], output: TextIO) -> None:
    for index, fixtures in enumerate(shards, start=1):
        print(f"Shard {index} ({len(fixtures)} fixtures)", file=output)
        for fixture in fixtures:
            print(f"  {fixture}", file=output)


def main(
    argv: Sequence[str] | None = None,
    repository_root: Path | None = None,
    output: TextIO | None = None,
) -> int:
    parser = argparse.ArgumentParser(
        description="Run the fail-closed SOLREIGN integration gate in six serial shards."
    )
    parser.add_argument(
        "--list",
        action="store_true",
        help="print the deterministic fixture inventory without invoking dotnet",
    )
    parser.add_argument(
        "--shard",
        type=int,
        choices=range(1, SHARD_COUNT + 1),
        metavar="N",
        help="run one 1-based shard without claiming full-suite acceptance",
    )
    parser.add_argument(
        "--no-build",
        action="store_true",
        help="reuse an explicit prior build (intended for CI after its solution build)",
    )
    parser.add_argument(
        "--receipt-directory",
        type=Path,
        help="write an atomic machine-readable receipt for every accepted shard",
    )
    parser.add_argument(
        "--verify-receipts",
        type=Path,
        metavar="DIR",
        help="verify six same-candidate shard receipts without invoking dotnet",
    )
    parser.add_argument(
        "--build-manifest",
        type=Path,
        help="bind receipt evidence to the validated checkpointed build manifest",
    )
    arguments = parser.parse_args(argv)
    root = repository_root or Path(__file__).resolve().parents[3]
    stream = output or sys.stdout
    if arguments.verify_receipts is not None and (
        arguments.list
        or arguments.shard is not None
        or arguments.no_build
        or arguments.receipt_directory is not None
    ):
        parser.error("--verify-receipts cannot be combined with execution or inventory flags")
    if arguments.verify_receipts is not None and arguments.build_manifest is None:
        parser.error("--verify-receipts requires --build-manifest")
    if arguments.verify_receipts is None and (
        (arguments.receipt_directory is None) != (arguments.build_manifest is None)
    ):
        parser.error("--receipt-directory and --build-manifest must be supplied together")
    if arguments.list and arguments.receipt_directory is not None:
        parser.error("--receipt-directory requires shard execution")
    try:
        shards = inventory_for_repository(root)
        if arguments.list:
            _render_inventory(shards, stream)
            return 0
        if arguments.verify_receipts is not None:
            verify_receipts(arguments.verify_receipts, root, arguments.build_manifest)
            print(
                f"receipt aggregate accepted: {EXPECTED_PASSED} passed + "
                f"{EXPECTED_SKIPPED} skipped = "
                f"{EXPECTED_PASSED + EXPECTED_SKIPPED}",
                file=stream,
            )
            return 0
        result = run_gate(
            root,
            shard=arguments.shard,
            build=not arguments.no_build,
            receipt_directory=arguments.receipt_directory,
            build_manifest=arguments.build_manifest,
            progress=lambda message: print(message, file=stream, flush=True),
        )
    except IntegrationGateError as error:
        print(f"integration gate refused: {error}", file=stream)
        return 1

    if result.full_suite_accepted:
        print(
            f"integration gate accepted: {EXPECTED_PASSED} passed + "
            f"{EXPECTED_SKIPPED} skipped = {EXPECTED_PASSED + EXPECTED_SKIPPED}",
            file=stream,
        )
    else:
        print(
            f"shard {arguments.shard} verified; full-suite acceptance not claimed",
            file=stream,
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
