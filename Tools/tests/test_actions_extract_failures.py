from __future__ import annotations

import json
import os
from pathlib import Path
import socket
import subprocess
import sys
import tempfile
import unittest

SCRIPT_PATH = Path(__file__).resolve().parents[1] / "actions_extract_failures.py"


class ActionsExtractFailuresTests(unittest.TestCase):
    def setUp(self) -> None:
        # Enforce no network access across the entire test suite
        def disallow_network(*args: object, **kwargs: object) -> None:
            raise AssertionError("Network access disallowed during test execution")

        self._orig_socket_connect = socket.socket.connect
        socket.socket.connect = disallow_network

    def tearDown(self) -> None:
        socket.socket.connect = self._orig_socket_connect

    def _run_script(
        self,
        content_tests_xml: str | None = None,
        integration_tests_xml: str | None = None,
    ) -> tuple[subprocess.CompletedProcess[str], dict[str, str]]:
        with tempfile.TemporaryDirectory() as td:
            temp_path = Path(td)
            logs_dir = temp_path / "test_results" / "logs"
            logs_dir.mkdir(parents=True)

            if content_tests_xml is not None:
                (logs_dir / "Content.Tests.xml").write_text(content_tests_xml, encoding="utf-8")
            if integration_tests_xml is not None:
                (logs_dir / "Content.IntegrationTests.xml").write_text(integration_tests_xml, encoding="utf-8")

            github_output_file = temp_path / "github_output.txt"
            env = os.environ.copy()
            env["GITHUB_OUTPUT"] = str(github_output_file)

            process = subprocess.run(
                [sys.executable, str(SCRIPT_PATH)],
                cwd=str(temp_path),
                env=env,
                capture_output=True,
                text=True,
            )

            outputs: dict[str, str] = {}
            if github_output_file.exists():
                for line in github_output_file.read_text(encoding="utf-8").splitlines():
                    if "=" in line:
                        key, value = line.split("=", 1)
                        outputs[key] = value

            return process, outputs

    def test_extracts_failed_tests(self) -> None:
        """Canned NUnit XML fixture with failures extracts failing test cases to GITHUB_OUTPUT."""
        content_xml = """<test-run id="2" testcasecount="2" result="Failed">
            <test-suite type="TestSuite" name="Content.Tests">
                <test-case id="1" name="PassingTest" fullname="Content.Tests.PassingTest" result="Passed">
                    <output>Passed cleanly</output>
                </test-case>
                <test-case id="2" name="FailingTest1" fullname="Content.Tests.FailingTest1" result="Failed">
                    <failure>
                        <message>Expected 42 but was 0</message>
                    </failure>
                    <output>Assert.That(result, Is.EqualTo(42))\nStack trace...</output>
                </test-case>
            </test-suite>
        </test-run>"""

        integration_xml = """<test-run id="3" testcasecount="1" result="Failed">
            <test-suite type="TestSuite" name="Content.IntegrationTests">
                <test-case id="3" name="FailingTest2" fullname="Content.IntegrationTests.FailingTest2" result="Failed">
                    <failure>
                        <message>Entity timed out</message>
                    </failure>
                    <output>Server timeout after 60s</output>
                </test-case>
            </test-suite>
        </test-run>"""

        proc, outputs = self._run_script(
            content_tests_xml=content_xml,
            integration_tests_xml=integration_xml,
        )

        self.assertEqual(proc.returncode, 0, f"Script failed with stderr:\n{proc.stderr}")
        self.assertIn("count", outputs)
        self.assertEqual(outputs["count"], "2")
        self.assertIn("matrix", outputs)

        matrix = json.loads(outputs["matrix"])
        self.assertEqual(len(matrix), 2)
        self.assertEqual(
            matrix[0],
            {
                "name": "FailingTest1",
                "fullname": "Content.Tests.FailingTest1",
                "failure": "Expected 42 but was 0",
                "output": "Assert.That(result, Is.EqualTo(42))\nStack trace...",
            },
        )
        self.assertEqual(
            matrix[1],
            {
                "name": "FailingTest2",
                "fullname": "Content.IntegrationTests.FailingTest2",
                "failure": "Entity timed out",
                "output": "Server timeout after 60s",
            },
        )

    def test_passing_only_log_yields_empty_result(self) -> None:
        """A passing-only report yields count=0 and empty matrix."""
        content_xml = """<test-run id="2" testcasecount="1" result="Passed">
            <test-suite type="TestSuite" name="Content.Tests">
                <test-case id="1" name="PassingTest" fullname="Content.Tests.PassingTest" result="Passed">
                    <output>All good</output>
                </test-case>
            </test-suite>
        </test-run>"""

        integration_xml = """<test-run id="3" testcasecount="2" result="Passed">
            <test-suite type="TestSuite" name="Content.IntegrationTests">
                <test-case id="2" name="AnotherPass" fullname="Content.IntegrationTests.AnotherPass" result="Passed">
                    <output>Passed</output>
                </test-case>
                <test-case id="3" name="SkippedTest" fullname="Content.IntegrationTests.SkippedTest" result="Skipped">
                    <reason><message>Ignored on Linux</message></reason>
                </test-case>
            </test-suite>
        </test-run>"""

        proc, outputs = self._run_script(
            content_tests_xml=content_xml,
            integration_tests_xml=integration_xml,
        )

        self.assertEqual(proc.returncode, 0, f"Script failed with stderr:\n{proc.stderr}")
        self.assertEqual(outputs.get("count"), "0")
        self.assertEqual(outputs.get("matrix"), "[]")

    def test_malformed_lines_and_unrecognized_nodes_ignored(self) -> None:
        """Malformed lines and unrecognized nodes within the XML tree are ignored without crash."""
        content_xml = """<test-run id="2" testcasecount="2" result="Failed">
            <!-- Non-standard XML comment line -->
            <malformed-wrapper unrecognized-attr="true">
                <arbitrary-node>Ignore this node</arbitrary-node>
                <test-case id="malformed-case-without-result-attr"/>
                <test-case id="1" name="FailingCase" fullname="Content.Tests.FailingCase" result="Failed">
                    <failure>
                        <message>Failed assertion</message>
                    </failure>
                    <output>Output text</output>
                </test-case>
            </malformed-wrapper>
            <unexpected-tag value="123"/>
        </test-run>"""

        integration_xml = """<test-run id="3" testcasecount="1" result="Passed">
            <empty-suite/>
            <arbitrary-data line="malformed"/>
        </test-run>"""

        proc, outputs = self._run_script(
            content_tests_xml=content_xml,
            integration_tests_xml=integration_xml,
        )

        self.assertEqual(proc.returncode, 0, f"Script failed with stderr:\n{proc.stderr}")
        self.assertEqual(outputs.get("count"), "1")
        matrix = json.loads(outputs.get("matrix", "[]"))
        self.assertEqual(len(matrix), 1)
        self.assertEqual(matrix[0]["name"], "FailingCase")
        self.assertEqual(matrix[0]["fullname"], "Content.Tests.FailingCase")

    def test_at_symbols_stripped_from_output(self) -> None:
        """Any '@' symbol in test attributes or failure content is sanitized away."""
        content_xml = """<test-run id="2" testcasecount="1" result="Failed">
            <test-suite type="TestSuite" name="Content.Tests">
                <test-case id="1" name="Test@Special" fullname="Namespace.@Class.Test@Special" result="Failed">
                    <failure>
                        <message>Expected @value but found @null</message>
                    </failure>
                    <output>Stacktrace at @handler()</output>
                </test-case>
            </test-suite>
        </test-run>"""

        integration_xml = """<test-run id="3" testcasecount="0" result="Passed"/>"""

        proc, outputs = self._run_script(
            content_tests_xml=content_xml,
            integration_tests_xml=integration_xml,
        )

        self.assertEqual(proc.returncode, 0, f"Script failed with stderr:\n{proc.stderr}")
        raw_matrix = outputs.get("matrix", "")
        self.assertNotIn("@", raw_matrix)
        matrix = json.loads(raw_matrix)
        self.assertEqual(matrix[0]["name"], "TestSpecial")
        self.assertEqual(matrix[0]["fullname"], "Namespace.Class.TestSpecial")
        self.assertEqual(matrix[0]["failure"], "Expected value but found null")
        self.assertEqual(matrix[0]["output"], "Stacktrace at handler()")


if __name__ == "__main__":
    unittest.main()
