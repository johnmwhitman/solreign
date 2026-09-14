from __future__ import annotations

import hashlib
import json
import unittest
import io
from pathlib import Path
from tempfile import TemporaryDirectory
from unittest import mock

from Tools._Solreign.IntegrationGate import solreign_integration_gate as gate


class IntegrationGateInventoryTests(unittest.TestCase):
    def test_assign_shards_covers_every_fixture_once_deterministically(self) -> None:
        """Catches a shard assignment mutation that omits or duplicates a fixture."""
        fixtures = [f"Fixture{index:03d}Test" for index in range(92)]

        shards = gate.assign_shards(fixtures)

        self.assertEqual(len(shards), 6)
        self.assertEqual(
            sorted(fixture for shard in shards for fixture in shard),
            fixtures,
        )
        self.assertEqual([len(shard) for shard in shards], [16, 16, 15, 15, 15, 15])

    def test_real_inventory_keeps_measured_heavy_fixtures_on_separate_shards(self) -> None:
        """Catches a return to count-blind round-robin that overloaded hosted shard 1."""
        repository_root = Path(__file__).resolve().parents[3]
        prefix = "Content.IntegrationTests.Tests._Solreign."
        expected = {
            f"{prefix}ProvidencePowerContractorSystemIntegrationTest": 3,
            f"{prefix}SolreignVampireFeedingCycleIntegrationTest": 4,
            f"{prefix}SolreignRoundStartJobSpreadGateTest": 5,
        }

        shards = gate.inventory_for_repository(repository_root)

        self.assertEqual([len(shard) for shard in shards], [13, 16, 15, 16, 16, 16])
        for fixture, target in expected.items():
            self.assertIn(fixture, shards[target])
            self.assertEqual(sum(shard.count(fixture) for shard in shards), 1)
        self.assertEqual(
            sorted(fixture for shard in shards for fixture in shard),
            gate.discover_fixtures(repository_root / gate.TEST_ROOT),
        )

    def test_discover_fixtures_rejects_duplicate_fixture_classes(self) -> None:
        """Catches a duplicate class mutation that would run one fixture twice."""
        with TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            (root / "One.cs").write_text(
                "[Test]\n"
                "namespace Content.IntegrationTests.Tests._Solreign;\n"
                "public sealed class DuplicateTest { }\n",
                encoding="utf-8",
            )
            (root / "Two.cs").write_text(
                "[Test]\n"
                "namespace Content.IntegrationTests.Tests._Solreign;\n"
                "internal sealed class DuplicateTest { }\n",
                encoding="utf-8",
            )

            with self.assertRaises(gate.IntegrationGateError) as raised:
                gate.discover_fixtures(root)

        self.assertIn("duplicate", str(raised.exception).lower())

    def test_discover_fixtures_rejects_test_file_without_fixture(self) -> None:
        """Catches a parser mutation that silently omits a [Test] file."""
        with TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            (root / "Unowned.cs").write_text(
                "[Test]\n"
                "namespace Content.IntegrationTests.Tests._Solreign;\n"
                "private sealed class HiddenTest { }\n",
                encoding="utf-8",
            )

            with self.assertRaises(gate.IntegrationGateError) as raised:
                gate.discover_fixtures(root)

        self.assertIn("no non-abstract", str(raised.exception))

    def test_discover_fixtures_rejects_commented_out_fixture_declaration(self) -> None:
        """Catches a parser mutation that mistakes commented source for a fixture."""
        with TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            (root / "Commented.cs").write_text(
                "[Test]\n"
                "namespace Content.IntegrationTests.Tests._Solreign;\n"
                "// public sealed class CommentedOutTest { }\n",
                encoding="utf-8",
            )

            with self.assertRaises(gate.IntegrationGateError) as raised:
                gate.discover_fixtures(root)

        self.assertIn("no non-abstract", str(raised.exception))

    def test_discover_fixtures_retains_nested_file_scoped_namespace(self) -> None:
        """Catches a parser mutation that flattens a nested namespace into the root namespace."""
        with TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            (root / "Nested.cs").write_text(
                "[TestFixture]\n"
                "namespace Content.IntegrationTests.Tests._Solreign.FX.Consumers;\n"
                "public sealed class NestedFixtureTest { }\n",
                encoding="utf-8",
            )

            fixtures = gate.discover_fixtures(root)

        self.assertEqual(
            fixtures,
            ["Content.IntegrationTests.Tests._Solreign.FX.Consumers.NestedFixtureTest"],
        )

    def test_discover_fixtures_rejects_absent_or_multiple_namespaces(self) -> None:
        """Catches namespace parsing that guesses or silently chooses one declaration."""
        sources = {
            "absent": "[Test]\npublic sealed class MissingNamespaceTest { }\n",
            "multiple": (
                "[Test]\n"
                "namespace Content.IntegrationTests.Tests._Solreign;\n"
                "namespace Content.IntegrationTests.Tests._Solreign.FX;\n"
                "public sealed class AmbiguousNamespaceTest { }\n"
            ),
        }
        for label, source in sources.items():
            with self.subTest(label=label):
                with TemporaryDirectory() as temporary_directory:
                    root = Path(temporary_directory)
                    (root / "Fixture.cs").write_text(source, encoding="utf-8")

                    with self.assertRaises(gate.IntegrationGateError) as raised:
                        gate.discover_fixtures(root)

                self.assertIn("namespace", str(raised.exception).lower())


class IntegrationGateExecutionTests(unittest.TestCase):
    def _inventory_root(self) -> TemporaryDirectory[str]:
        temporary_directory = TemporaryDirectory()
        root = Path(temporary_directory.name) / "Content.IntegrationTests" / "Tests" / "_Solreign"
        root.mkdir(parents=True)
        for index in range(92):
            (root / f"Fixture{index:03d}Test.cs").write_text(
                "[Test]\n"
                "namespace Content.IntegrationTests.Tests._Solreign;\n"
                f"public sealed class Fixture{index:03d}Test {{ }}\n",
                encoding="utf-8",
            )
        return temporary_directory

    def test_parse_vstest_summary_rejects_zero_match_and_malformed_final_summary(self) -> None:
        """Catches a parser mutation that accepts absent or malformed VSTest summaries."""
        for output in (
            "test host started\n",
            "Passed: none, Failed: 0, Skipped: 0, Total: 0\n",
            "Passed: 1, Failed: 0\nnot a summary\n",
            "Passed: 1, Failed: 0, Skipped: 0, Total: 2\n",
        ):
            with self.subTest(output=output):
                with self.assertRaises(gate.IntegrationGateError):
                    gate.parse_vstest_summary(output)

    def test_run_gate_rejects_nonzero_child_exit(self) -> None:
        """Catches a runner mutation that trusts a green-looking child transcript."""
        with self._inventory_root() as temporary_directory:
            calls: list[list[str]] = []

            def fake_run(command: list[str], **_: object) -> mock.Mock:
                calls.append(command)
                if command[1] == "build":
                    return mock.Mock(returncode=0, stdout="Build succeeded.\n")
                return mock.Mock(
                    returncode=1,
                    stdout="Passed: 1, Failed: 0, Skipped: 0, Total: 1\n",
                )

            with self.assertRaises(gate.IntegrationGateError) as raised:
                gate.run_gate(Path(temporary_directory), execute=fake_run)

        self.assertIn("status 1", str(raised.exception))
        self.assertEqual(len(calls), 2)

    def test_run_gate_rejects_skipped_only_shard(self) -> None:
        """Catches a gate mutation that treats only-skipped output as execution."""
        with self._inventory_root() as temporary_directory:
            outputs = [
                "Build succeeded.\n",
                "Passed: 0, Failed: 0, Skipped: 1, Total: 1\n",
            ]

            def fake_run(_: list[str], **__: object) -> mock.Mock:
                return mock.Mock(returncode=0, stdout=outputs.pop(0))

            with self.assertRaises(gate.IntegrationGateError) as raised:
                gate.run_gate(Path(temporary_directory), shard=1, execute=fake_run)

        self.assertIn("zero passed", str(raised.exception))

    def test_run_gate_rejects_watchdog_text_despite_green_summary(self) -> None:
        """Catches a runner mutation that ignores watchdog or hard-stop evidence."""
        with self._inventory_root() as temporary_directory:
            outputs = [
                "Build succeeded.\n",
                "Passed: 1, Failed: 0, Skipped: 0, Total: 1\nwatchdog hard-stop\n",
            ]

            def fake_run(_: list[str], **__: object) -> mock.Mock:
                return mock.Mock(returncode=0, stdout=outputs.pop(0))

            with self.assertRaises(gate.IntegrationGateError) as raised:
                gate.run_gate(Path(temporary_directory), shard=1, execute=fake_run)

        self.assertIn("watchdog", str(raised.exception).lower())

    def test_run_gate_rejects_plain_watchdog_evidence(self) -> None:
        """Catches a narrow poison matcher that permits watchdog status text."""
        with self._inventory_root() as temporary_directory:
            outputs = [
                "Build succeeded.\n",
                "Passed: 1, Failed: 0, Skipped: 0, Total: 1\n"
                "watchdog configuration evidence observed\n",
            ]

            def fake_run(_: list[str], **__: object) -> mock.Mock:
                return mock.Mock(returncode=0, stdout=outputs.pop(0))

            with self.assertRaises(gate.IntegrationGateError) as raised:
                gate.run_gate(Path(temporary_directory), shard=1, execute=fake_run)

        self.assertIn("watchdog", str(raised.exception).lower())

    def test_run_gate_rejects_green_summary_followed_by_terminal_failure(self) -> None:
        """Catches a parser that accepts a summary before an aborted VSTest tail."""
        with self._inventory_root() as temporary_directory:
            outputs = [
                "Build succeeded.\n",
                "Passed: 1, Failed: 0, Skipped: 0, Total: 1\n"
                "Test Run Failed.\n",
            ]

            def fake_run(_: list[str], **__: object) -> mock.Mock:
                return mock.Mock(returncode=0, stdout=outputs.pop(0))

            with self.assertRaises(gate.IntegrationGateError) as raised:
                gate.run_gate(Path(temporary_directory), shard=1, execute=fake_run)

        self.assertIn("terminal", str(raised.exception).lower())

    def test_run_gate_rejects_incorrect_full_suite_aggregate(self) -> None:
        """Catches an aggregate mutation that claims full-suite acceptance on drift."""
        with self._inventory_root() as temporary_directory:
            outputs = ["Build succeeded.\n"] + [
                "Passed: 1, Failed: 0, Skipped: 0, Total: 1\n"
            ] * 6

            def fake_run(_: list[str], **__: object) -> mock.Mock:
                return mock.Mock(returncode=0, stdout=outputs.pop(0))

            with self.assertRaises(gate.IntegrationGateError) as raised:
                gate.run_gate(Path(temporary_directory), execute=fake_run)

        self.assertIn("aggregate", str(raised.exception).lower())

    def test_expected_passed_pin_includes_post_pin_regressions(self) -> None:
        """Pins the G6 regression plus two added map-pool population cases."""
        self.assertEqual(gate.EXPECTED_PASSED, 478)

    def test_single_shard_diagnostic_never_claims_full_suite_acceptance(self) -> None:
        """Catches a diagnostic mutation that promotes one shard to full-suite evidence."""
        with self._inventory_root() as temporary_directory:
            outputs = [
                "Build succeeded.\n",
                "Passed: 1, Failed: 0, Skipped: 0, Total: 1\n",
            ]

            def fake_run(_: list[str], **__: object) -> mock.Mock:
                return mock.Mock(returncode=0, stdout=outputs.pop(0))

            result = gate.run_gate(
                Path(temporary_directory),
                shard=1,
                execute=fake_run,
            )

        self.assertFalse(result.full_suite_accepted)
        self.assertEqual(len(result.summaries), 1)

    def test_run_gate_builds_once_then_runs_shards_sequentially_without_watchdog_override(self) -> None:
        """Catches command mutations that parallelize, rebuild, or loosen the watchdog."""
        with self._inventory_root() as temporary_directory:
            calls: list[list[str]] = []
            # Each shard must execute. Spread the required aggregate across all six.
            outputs = ["Build succeeded.\n"] + [
                "Passed: 69, Failed: 0, Skipped: 0, Total: 69\n",
                "Passed: 88, Failed: 0, Skipped: 0, Total: 88\n",
                "Passed: 98, Failed: 0, Skipped: 0, Total: 98\n",
                "Passed: 88, Failed: 0, Skipped: 0, Total: 88\n",
                "Passed: 65, Failed: 0, Skipped: 0, Total: 65\n",
                "Passed: 70, Failed: 0, Skipped: 0, Total: 70\n",
            ]

            def fake_run(command: list[str], **_: object) -> mock.Mock:
                calls.append(command)
                return mock.Mock(returncode=0, stdout=outputs.pop(0))

            result = gate.run_gate(Path(temporary_directory), execute=fake_run)

        self.assertTrue(result.full_suite_accepted)
        self.assertEqual(len(calls), 7)
        self.assertEqual(calls[0][0:2], ["dotnet", "build"])
        self.assertEqual([command[1] for command in calls[1:]], ["test"] * 6)
        for command in calls:
            self.assertIn("-m:1", command)
            self.assertIn("-nodeReuse:false", command)
            self.assertIn("-p:UseSharedCompilation=false", command)
            self.assertNotIn("SOLREIGN_INTEGRATION_WATCHDOG_MINUTES", " ".join(command))
        self.assertEqual(
            [command[command.index("--filter") + 1] for command in calls[1:]],
            sorted(
                [command[command.index("--filter") + 1] for command in calls[1:]]
            ),
        )
        external_selector = (
            "(FullyQualifiedName~_Solreign&"
            "FullyQualifiedName!~Content.IntegrationTests.Tests._Solreign.)"
        )
        filters = [command[command.index("--filter") + 1] for command in calls[1:]]
        self.assertEqual(sum(value.count(external_selector) for value in filters), 1)
        self.assertNotIn(external_selector, filters[0])
        self.assertIn(external_selector, filters[-1])

    def test_test_command_uses_real_namespace_and_exact_class_boundary(self) -> None:
        """Catches a selector mutation that flattens namespaces or matches class-name prefixes."""
        repository_root = Path("/tmp/solreign-gate-contract")
        command = gate._test_command(
            ["Content.IntegrationTests.Tests._Solreign.FX.ExampleTest"],
            repository_root,
            include_external=False,
        )

        self.assertEqual(
            command[command.index("--filter") + 1],
            "FullyQualifiedName~Content.IntegrationTests.Tests._Solreign.FX.ExampleTest.",
        )
        self.assertIn("NUnit.MapWarningTo=Failed", command)
        self.assertIn("NUnit.TestOutputXml=logs", command)
        self.assertIn(
            f"NUnit.WorkDirectory={repository_root / gate.LOG_ROOT / 'test_results'}",
            command,
        )
        self.assertNotIn(f"NUnit.WorkDirectory={repository_root / 'test_results'}", command)

    def test_run_gate_can_reuse_an_explicit_prior_build_and_reports_progress(self) -> None:
        """Catches CI-only rebuild drift and silent long-running shard execution."""
        with self._inventory_root() as temporary_directory:
            calls: list[list[str]] = []
            progress: list[str] = []

            def fake_run(command: list[str], **_: object) -> mock.Mock:
                calls.append(command)
                return mock.Mock(
                    returncode=0,
                    stdout="Passed: 1, Failed: 0, Skipped: 0, Total: 1\n",
                )

            result = gate.run_gate(
                Path(temporary_directory),
                shard=1,
                build=False,
                execute=fake_run,
                progress=progress.append,
            )

        self.assertFalse(result.full_suite_accepted)
        self.assertEqual(len(calls), 1)
        self.assertEqual(calls[0][0:2], ["dotnet", "test"])
        self.assertTrue(any("shard 1" in message and "started" in message for message in progress))
        self.assertTrue(any("shard 1" in message and "accepted" in message for message in progress))

    def test_run_gate_allows_exact_shard_target_and_rejects_over_target_after_logging(self) -> None:
        """Catches a timing mutation that permits over-target shards or rejects the exact boundary."""
        transcript = "Passed: 1, Failed: 0, Skipped: 0, Total: 1\n"
        cases = (
            (720.0, False),
            (720.001, True),
        )
        for elapsed, should_refuse in cases:
            with self.subTest(elapsed=elapsed):
                with self._inventory_root() as temporary_directory:
                    outputs = ["Build succeeded.\n", transcript]

                    def fake_run(_: list[str], **__: object) -> mock.Mock:
                        return mock.Mock(returncode=0, stdout=outputs.pop(0))

                    clock = iter((100.0, 100.0 + elapsed))
                    if should_refuse:
                        with self.assertRaises(gate.IntegrationGateError) as raised:
                            gate.run_gate(
                                Path(temporary_directory),
                                shard=1,
                                execute=fake_run,
                                monotonic=lambda: next(clock),
                            )
                        message = str(raised.exception)
                        self.assertIn("shard 1", message)
                        self.assertIn("720.001", message)
                        self.assertIn("720", message)
                    else:
                        result = gate.run_gate(
                            Path(temporary_directory),
                            shard=1,
                            execute=fake_run,
                            monotonic=lambda: next(clock),
                        )
                        self.assertFalse(result.full_suite_accepted)

                    log = Path(temporary_directory) / gate.LOG_ROOT / "shard-1.log"
                    self.assertEqual(log.read_text(encoding="utf-8"), transcript)

    def test_run_gate_localizes_every_child_without_mutating_parent_environment(self) -> None:
        """Catches child-process locale drift or accidental mutation of the parent environment."""
        with self._inventory_root() as temporary_directory:
            outputs = [
                "Build succeeded.\n",
                "Passed: 1, Failed: 0, Skipped: 0, Total: 1\n",
            ]
            child_kwargs: list[dict[str, object]] = []

            def fake_run(_: list[str], **kwargs: object) -> mock.Mock:
                child_kwargs.append(kwargs)
                return mock.Mock(returncode=0, stdout=outputs.pop(0))

            with mock.patch.dict(gate.os.environ, {"PARENT_SENTINEL": "kept"}, clear=True):
                before = dict(gate.os.environ)
                gate.run_gate(Path(temporary_directory), shard=1, execute=fake_run)
                after = dict(gate.os.environ)

        self.assertEqual(before, after)
        self.assertEqual(len(child_kwargs), 2)
        for kwargs in child_kwargs:
            self.assertNotIn("timeout", kwargs)
            child_environment = kwargs["env"]
            self.assertIsInstance(child_environment, dict)
            self.assertIsNot(child_environment, gate.os.environ)
            self.assertEqual(child_environment["PARENT_SENTINEL"], "kept")
            self.assertEqual(child_environment["DOTNET_CLI_UI_LANGUAGE"], "en")

    def test_run_gate_refuses_inherited_watchdog_override_without_mutating_environment(self) -> None:
        """Catches a runner mutation that inherits a watchdog-loosening environment."""
        with self._inventory_root() as temporary_directory:
            with mock.patch.dict(
                "os.environ",
                {"SOLREIGN_INTEGRATION_WATCHDOG_MINUTES": "60"},
                clear=False,
            ):
                before = dict(gate.os.environ)
                with self.assertRaises(gate.IntegrationGateError) as raised:
                    gate.run_gate(Path(temporary_directory), execute=mock.Mock())
                after = dict(gate.os.environ)

        self.assertIn("SOLREIGN_INTEGRATION_WATCHDOG_MINUTES", str(raised.exception))
        self.assertEqual(before, after)

    def test_list_diagnostic_prints_inventory_without_invoking_dotnet(self) -> None:
        """Catches a --list mutation that starts a build or test subprocess."""
        repository_root = Path(__file__).resolve().parents[3]
        output = io.StringIO()

        with mock.patch.object(gate.subprocess, "run") as run:
            exit_code = gate.main(
                ["--list"],
                repository_root=repository_root,
                output=output,
            )

        self.assertEqual(exit_code, 0)
        self.assertIn("Shard 1", output.getvalue())
        self.assertIn("Shard 6", output.getvalue())
        self.assertEqual(run.call_count, 0)

    def test_debug_ci_checkpoints_six_sequential_shards_and_verifies_receipts(self) -> None:
        """Catches CI drift back to one fragile hour-long hosted-runner job."""
        repository_root = Path(__file__).resolve().parents[3]
        workflow = (
            repository_root / ".github/workflows/build-test-debug.yml"
        ).read_text(encoding="utf-8")

        self.assertIn("integration-build-${{ github.sha }}", workflow)
        self.assertIn("bin/Content.IntegrationTests/**", workflow)
        self.assertIn("bin/Content.Client/**", workflow)
        self.assertIn("bin/Content.Server/**", workflow)
        self.assertIn("Content.IntegrationTests/obj/**", workflow)
        self.assertIn(".gstack/integration-build/**", workflow)
        checkpoint_roots = (
            "bin/Content.IntegrationTests bin/Content.Client bin/Content.Server "
            "Content.IntegrationTests/obj"
        )
        self.assertEqual(workflow.count(f"find {checkpoint_roots} -type f -print"), 7)
        self.assertEqual(workflow.count("test -d bin/Content.Client"), 7)
        self.assertEqual(workflow.count("test -d bin/Content.Server"), 7)
        build_artifact = workflow.split("    - name: Checkpoint integration build\n", 1)[1].split(
            "\n    - name: Checkpoint integration build manifest", 1
        )[0]
        self.assertIn("include-hidden-files: true", build_artifact)
        self.assertEqual(workflow.count("sha256sum --check .gstack/integration-build/artifact.sha256"), 6)
        self.assertEqual(workflow.count("sha256sum --check .gstack/integration-build/build-manifest.sha256"), 7)
        self.assertEqual(workflow.count("--build-manifest .gstack/integration-build/build-manifest.sha256"), 7)
        self.assertEqual(
            workflow.count(
                "name: integration-build-manifest-${{ github.sha }}\n"
                "        path: .gstack/integration-build\n"
            ),
            7,
        )
        self.assertEqual(workflow.count("cmp .gstack/integration-build/submodules.txt"), 6)
        self.assertEqual(
            workflow.count("dotnet restore Content.IntegrationTests/Content.IntegrationTests.csproj"),
            6,
        )
        for shard in range(1, 7):
            job = f"integration-shard-{shard}:"
            self.assertEqual(workflow.count(job), 1)
            command = (
                "python3 -B Tools/_Solreign/IntegrationGate/solreign_integration_gate.py "
                f"--no-build --shard {shard} --receipt-directory "
                ".gstack/integration-gate/receipts"
            )
            self.assertEqual(workflow.count(command), 1)
            section = workflow.split(job, 1)[1].split(
                f"\n  integration-shard-{shard + 1}:" if shard < 6 else "\n  integration-aggregate:",
                1,
            )[0]
            if shard == 1:
                self.assertIn("needs: build", section)
            else:
                self.assertIn(f"needs: integration-shard-{shard - 1}", section)
            self.assertIn("timeout-minutes: 25", section)
            self.assertIn("include-hidden-files: true", section.split(f"- name: Upload shard {shard} receipt", 1)[1])
        self.assertNotIn(
            "solreign_integration_gate.py --no-build &",
            workflow,
        )
        aggregate = workflow.split("  integration-aggregate:\n", 1)[1].split(
            "\n  ci-success:", 1
        )[0]
        self.assertIn("needs: integration-shard-6", aggregate)
        self.assertIn("--verify-receipts .gstack/integration-gate/aggregate-receipts", aggregate)
        self.assertIn("merge-multiple: true", aggregate)
        self.assertEqual(workflow.count("test_results/**"), 1)
        self.assertEqual(workflow.count(".gstack/integration-gate/**"), 6)
        self.assertIn("include-hidden-files: true", workflow)
        self.assertLess(workflow.index("- name: Build Project"), workflow.index("integration-shard-1:"))


class IntegrationGateReceiptTests(unittest.TestCase):
    """RED contract for artifact-scoped, fail-closed shard receipts."""

    CANDIDATE_SHA = "a" * 40
    BUILD_MANIFEST = b"artifact closure\n"
    BUILD_SHA = hashlib.sha256(BUILD_MANIFEST).hexdigest()

    def _inventory_root(self) -> TemporaryDirectory[str]:
        temporary_directory = TemporaryDirectory()
        root = Path(temporary_directory.name) / "Content.IntegrationTests" / "Tests" / "_Solreign"
        root.mkdir(parents=True)
        for index in range(92):
            (root / f"Fixture{index:03d}Test.cs").write_text(
                "[Test]\n"
                "namespace Content.IntegrationTests.Tests._Solreign;\n"
                f"public sealed class Fixture{index:03d}Test {{ }}\n",
                encoding="utf-8",
            )
        return temporary_directory

    @staticmethod
    def _digest(shards: list[list[str]]) -> str:
        canonical = json.dumps(
            {"fixture_count": 92, "shards": shards},
            sort_keys=True,
            separators=(",", ":"),
        ).encode("utf-8")
        return hashlib.sha256(canonical).hexdigest()

    def _receipt(self, shards: list[list[str]], shard: int, **changes: object) -> dict[str, object]:
        fixtures = shards[shard - 1]
        receipt: dict[str, object] = {
            "schema": "solreign.integration-gate.receipt/v1",
            "candidate_sha": self.CANDIDATE_SHA,
            "inventory_sha256": self._digest(shards),
            "build_sha256": self.BUILD_SHA,
            "shard": shard,
            "fixtures": fixtures,
            "filter": gate._test_command(fixtures, Path("/receipt-root"), shard == 6)[
                gate._test_command(fixtures, Path("/receipt-root"), shard == 6).index("--filter") + 1
            ],
            "elapsed_seconds": 1.25,
            "summary": {"passed": 78, "failed": 0, "skipped": 0, "total": 78},
        }
        receipt.update(changes)
        return receipt

    def _build_manifest(self, root: Path) -> Path:
        path = root / "artifact.sha256"
        path.write_bytes(self.BUILD_MANIFEST)
        return path

    @staticmethod
    def _write(receipt_directory: Path, shard: int, receipt: dict[str, object]) -> None:
        (receipt_directory / f"shard-{shard}.json").write_text(
            json.dumps(receipt), encoding="utf-8"
        )

    def _valid_receipts(self, receipt_directory: Path, shards: list[list[str]]) -> None:
        summaries = ((69, 0), (88, 0), (98, 0), (88, 0), (65, 0), (70, 0))
        for shard, (passed, skipped) in enumerate(summaries, start=1):
            receipt = self._receipt(
                shards,
                shard,
                summary={
                    "passed": passed,
                    "failed": 0,
                    "skipped": skipped,
                    "total": passed + skipped,
                },
            )
            self._write(receipt_directory, shard, receipt)

    def test_accepted_single_shard_writes_strict_bound_receipt(self) -> None:
        """Catches a green shard that has no attestable candidate/inventory receipt."""
        with self._inventory_root() as temporary_directory, TemporaryDirectory() as receipt_directory:
            root = Path(temporary_directory)
            receipts = Path(receipt_directory)
            shards = gate.inventory_for_repository(root)
            manifest = self._build_manifest(root)

            with mock.patch.object(gate, "candidate_sha_for_repository", return_value=self.CANDIDATE_SHA):
                gate.run_gate(
                    root,
                    shard=1,
                    build=False,
                    execute=lambda *_args, **_kwargs: mock.Mock(
                        returncode=0,
                        stdout="Passed: 1, Failed: 0, Skipped: 0, Total: 1\n",
                    ),
                    receipt_directory=receipts,
                    build_manifest=manifest,
                )

            receipt = json.loads((receipts / "shard-1.json").read_text(encoding="utf-8"))
            self.assertEqual(
                set(receipt),
                {
                    "schema", "candidate_sha", "inventory_sha256", "build_sha256", "shard", "fixtures",
                    "filter", "elapsed_seconds", "summary",
                },
            )
            self.assertEqual(receipt["candidate_sha"], self.CANDIDATE_SHA)
            self.assertEqual(receipt["inventory_sha256"], self._digest(shards))
            self.assertEqual(receipt["build_sha256"], self.BUILD_SHA)
            self.assertEqual(receipt["fixtures"], shards[0])
            self.assertEqual(receipt["summary"], {"passed": 1, "failed": 0, "skipped": 0, "total": 1})
            self.assertIsInstance(receipt["elapsed_seconds"], float)

    def test_verifier_accepts_complete_same_candidate_receipts_at_gate_aggregate(self) -> None:
        """Catches a verifier that cannot establish a complete bound six-shard result."""
        with self._inventory_root() as temporary_directory, TemporaryDirectory() as receipt_directory:
            root, receipts = Path(temporary_directory), Path(receipt_directory)
            shards = gate.inventory_for_repository(root)
            manifest = self._build_manifest(root)
            self._valid_receipts(receipts, shards)
            with mock.patch.object(gate, "candidate_sha_for_repository", return_value=self.CANDIDATE_SHA):
                gate.verify_receipts(receipts, root, manifest)

    def test_verifier_rejects_receipt_set_shape_and_binding_drift(self) -> None:
        """Catches missing, mixed, or incorrectly bound shard evidence."""
        cases = {
            "missing": lambda directory, shards: (directory / "shard-6.json").unlink(),
            "extra": lambda directory, shards: self._write(directory, 7, self._receipt(shards, 1)),
            "duplicate": lambda directory, shards: (directory / "copy.json").write_text(
                json.dumps(self._receipt(shards, 1)), encoding="utf-8"
            ),
            "filename-mismatch": lambda directory, shards: self._write(directory, 1, self._receipt(shards, 2)),
            "candidate": lambda directory, shards: self._write(directory, 1, self._receipt(shards, 1, candidate_sha="b" * 40)),
            "inventory": lambda directory, shards: self._write(directory, 1, self._receipt(shards, 1, inventory_sha256="0" * 64)),
            "fixtures": lambda directory, shards: self._write(directory, 1, self._receipt(shards, 1, fixtures=[])),
            "filter": lambda directory, shards: self._write(directory, 6, self._receipt(shards, 6, filter="wrong")),
        }
        for label, mutate in cases.items():
            with self.subTest(label=label), self._inventory_root() as temporary_directory, TemporaryDirectory() as receipt_directory:
                root, receipts = Path(temporary_directory), Path(receipt_directory)
                shards = gate.inventory_for_repository(root)
                manifest = self._build_manifest(root)
                self._valid_receipts(receipts, shards)
                mutate(receipts, shards)
                with mock.patch.object(gate, "candidate_sha_for_repository", return_value=self.CANDIDATE_SHA):
                    with self.assertRaises(gate.IntegrationGateError):
                        gate.verify_receipts(receipts, root, manifest)

    def test_verifier_rejects_malformed_fields_invalid_results_and_wrong_aggregate(self) -> None:
        """Catches receipt laundering through parser, result, duration, or aggregate gaps."""
        cases = {
            "malformed": lambda directory, shards: (directory / "shard-1.json").write_text("{", encoding="utf-8"),
            "unknown": lambda directory, shards: self._write(directory, 1, self._receipt(shards, 1, extra=True)),
            "build": lambda directory, shards: self._write(directory, 1, self._receipt(shards, 1, build_sha256="0" * 64)),
            "failed": lambda directory, shards: self._write(directory, 1, self._receipt(shards, 1, summary={"passed": 78, "failed": 1, "skipped": 0, "total": 79})),
            "zero-passed": lambda directory, shards: self._write(directory, 1, self._receipt(shards, 1, summary={"passed": 0, "failed": 0, "skipped": 1, "total": 1})),
            "bad-total": lambda directory, shards: self._write(directory, 1, self._receipt(shards, 1, summary={"passed": 78, "failed": 0, "skipped": 0, "total": 77})),
            "over-target": lambda directory, shards: self._write(directory, 1, self._receipt(shards, 1, elapsed_seconds=720.001)),
            "wrong-aggregate": lambda directory, shards: self._write(directory, 1, self._receipt(shards, 1, summary={"passed": 78, "failed": 0, "skipped": 1, "total": 79})),
        }
        for label, mutate in cases.items():
            with self.subTest(label=label), self._inventory_root() as temporary_directory, TemporaryDirectory() as receipt_directory:
                root, receipts = Path(temporary_directory), Path(receipt_directory)
                shards = gate.inventory_for_repository(root)
                manifest = self._build_manifest(root)
                self._valid_receipts(receipts, shards)
                mutate(receipts, shards)
                with mock.patch.object(gate, "candidate_sha_for_repository", return_value=self.CANDIDATE_SHA):
                    with self.assertRaises(gate.IntegrationGateError):
                        gate.verify_receipts(receipts, root, manifest)

    def test_verifier_rejects_duplicate_json_keys_at_every_object_depth(self) -> None:
        """Catches last-value-wins parsing of contradictory receipt evidence."""
        with self._inventory_root() as temporary_directory, TemporaryDirectory() as receipt_directory:
            root, receipts = Path(temporary_directory), Path(receipt_directory)
            shards = gate.inventory_for_repository(root)
            manifest = self._build_manifest(root)
            self._valid_receipts(receipts, shards)
            original = (receipts / "shard-1.json").read_text(encoding="utf-8")
            variants = (
                original[:-1] + ',"shard":1}',
                original.replace('"passed": 69', '"passed": 69, "passed": 69', 1),
            )
            for duplicate_json in variants:
                with self.subTest(duplicate_json=duplicate_json[-80:]):
                    (receipts / "shard-1.json").write_text(duplicate_json, encoding="utf-8")
                    with mock.patch.object(
                        gate,
                        "candidate_sha_for_repository",
                        return_value=self.CANDIDATE_SHA,
                    ):
                        with self.assertRaises(gate.IntegrationGateError):
                            gate.verify_receipts(receipts, root, manifest)

    def test_failed_shard_never_leaves_a_receipt_or_temporary_receipt(self) -> None:
        """Catches receipt creation before exit, poison, summary, and duration validation."""
        cases = (
            (1, "Passed: 1, Failed: 0, Skipped: 0, Total: 1\n", (0.0, 1.0)),
            (0, "watchdog\nPassed: 1, Failed: 0, Skipped: 0, Total: 1\n", (0.0, 1.0)),
            (0, "no summary\n", (0.0, 1.0)),
            (0, "Passed: 1, Failed: 0, Skipped: 0, Total: 1\n", (0.0, 720.001)),
        )
        for returncode, transcript, clock_values in cases:
            with self.subTest(returncode=returncode, transcript=transcript), self._inventory_root() as temporary_directory, TemporaryDirectory() as receipt_directory:
                root, receipts = Path(temporary_directory), Path(receipt_directory)
                manifest = self._build_manifest(root)
                clock = iter(clock_values)
                with mock.patch.object(
                    gate,
                    "candidate_sha_for_repository",
                    return_value=self.CANDIDATE_SHA,
                ):
                    with self.assertRaises(gate.IntegrationGateError):
                        gate.run_gate(
                            root,
                            shard=1,
                            build=False,
                            receipt_directory=receipts,
                            build_manifest=manifest,
                            execute=lambda *_args, **_kwargs: mock.Mock(
                                returncode=returncode,
                                stdout=transcript,
                            ),
                            monotonic=lambda: next(clock),
                        )
                self.assertEqual(list(receipts.iterdir()), [])

    def test_shard_refuses_a_preexisting_final_or_temporary_receipt(self) -> None:
        """Catches a failed rerun laundering stale green evidence from the same build."""
        for name in ("shard-1.json", ".shard-1.json.tmp"):
            with self.subTest(name=name), self._inventory_root() as temporary_directory, TemporaryDirectory() as receipt_directory:
                root, receipts = Path(temporary_directory), Path(receipt_directory)
                manifest = self._build_manifest(root)
                (receipts / name).write_text("stale\n", encoding="utf-8")
                execute = mock.Mock()
                with mock.patch.object(
                    gate,
                    "candidate_sha_for_repository",
                    return_value=self.CANDIDATE_SHA,
                ):
                    with self.assertRaises(gate.IntegrationGateError):
                        gate.run_gate(
                            root,
                            shard=1,
                            build=False,
                            receipt_directory=receipts,
                            build_manifest=manifest,
                            execute=execute,
                        )
                execute.assert_not_called()

    def test_candidate_sha_rejects_a_dirty_source_tree(self) -> None:
        """Catches receipts that name HEAD while executing modified source bytes."""
        results = (
            mock.Mock(returncode=0, stdout=self.CANDIDATE_SHA + "\n"),
            mock.Mock(returncode=0, stdout=" M Content.IntegrationTests/Dirty.cs\n"),
        )
        with mock.patch.object(gate.subprocess, "run", side_effect=results):
            with self.assertRaises(gate.IntegrationGateError):
                gate.candidate_sha_for_repository(Path("/candidate"))

    def test_candidate_sha_allows_bound_submodule_head_drift_but_rejects_dirty_submodule_files(self) -> None:
        """Catches either rejecting the dependency action or ignoring dirty engine source."""
        clean_results = (
            mock.Mock(returncode=0, stdout=self.CANDIDATE_SHA + "\n"),
            mock.Mock(returncode=0, stdout=""),
            mock.Mock(returncode=0, stdout=""),
        )
        with mock.patch.object(gate.subprocess, "run", side_effect=clean_results) as run:
            self.assertEqual(
                gate.candidate_sha_for_repository(Path("/candidate")),
                self.CANDIDATE_SHA,
            )
        self.assertIn("--ignore-submodules=all", run.call_args_list[1].args[0])

        dirty_submodule_results = (
            mock.Mock(returncode=0, stdout=self.CANDIDATE_SHA + "\n"),
            mock.Mock(returncode=0, stdout=""),
            mock.Mock(returncode=0, stdout=" M RobustToolbox/Dirty.cs\n"),
        )
        with mock.patch.object(gate.subprocess, "run", side_effect=dirty_submodule_results):
            with self.assertRaises(gate.IntegrationGateError):
                gate.candidate_sha_for_repository(Path("/candidate"))

    def test_verify_receipts_cli_never_runs_the_serial_gate_and_default_still_does(self) -> None:
        """Catches verifier mode accidentally building/testing or default CLI drift."""
        root = Path(__file__).resolve().parents[3]
        with mock.patch.object(gate, "verify_receipts") as verify, mock.patch.object(gate, "run_gate") as run:
            self.assertEqual(
                gate.main(
                    [
                        "--verify-receipts",
                        "/tmp/receipts",
                        "--build-manifest",
                        "/tmp/artifact.sha256",
                    ],
                    repository_root=root,
                ),
                0,
            )
            verify.assert_called_once()
            run.assert_not_called()
            self.assertEqual(gate.main([], repository_root=root), 0)
            run.assert_called_once()


if __name__ == "__main__":
    unittest.main()
