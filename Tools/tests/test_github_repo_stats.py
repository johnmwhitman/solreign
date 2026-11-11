from __future__ import annotations

import ast
from contextlib import contextmanager
import os
import sys
import types
import unittest
from unittest.mock import MagicMock, patch

import requests


def _load_github_repo_stats_module() -> types.ModuleType:
    repo_root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    script_path = os.path.join(repo_root, "Tools", "github_repo_stats.py")

    with open(script_path, "r", encoding="utf-8") as file:
        source_ast = ast.parse(file.read(), filename=script_path)

    # Strip the trailing top-level main() invocation so importing does not run
    # the server or signal.pause().
    source_ast.body = [
        node
        for node in source_ast.body
        if not (
            isinstance(node, ast.Expr)
            and isinstance(node.value, ast.Call)
            and getattr(node.value.func, "id", None) == "main"
        )
    ]

    compiled_code = compile(source_ast, script_path, "exec")
    module_name = "Tools.github_repo_stats"
    module = types.ModuleType(module_name)
    module.__file__ = script_path
    sys.modules[module_name] = module

    # Provide a placeholder token during module initialization
    with patch.dict(os.environ, {"SS14_REPO_STATS_GITHUB_TOKEN": "mock-token-for-tests"}):
        exec(compiled_code, module.__dict__)

    return module


stats_module = _load_github_repo_stats_module()


@contextmanager
def mock_graphql_response(status_code: int = 200, json_data: dict | None = None, raise_error: Exception | None = None):
    mock_resp = MagicMock()
    mock_resp.status_code = status_code
    if raise_error:
        mock_resp.raise_for_status.side_effect = raise_error
    else:
        mock_resp.raise_for_status.return_value = None

    if json_data is not None:
        mock_resp.json.return_value = json_data

    mock_post_ctx = MagicMock()
    mock_post_ctx.__enter__.return_value = mock_resp
    mock_post_ctx.__exit__.return_value = None

    with patch.object(stats_module.REQUESTS_SESSION, "post", return_value=mock_post_ctx) as mock_post:
        yield mock_post, mock_resp


class TestGithubRepoStatsQueryBuilders(unittest.TestCase):
    def test_enum_join(self) -> None:
        self.assertEqual(
            stats_module.enum_join([stats_module.IssueState.OPEN, stats_module.IssueState.CLOSED]),
            "[OPEN,CLOSED]",
        )
        self.assertEqual(
            stats_module.enum_join([stats_module.PullRequestState.MERGED]),
            "[MERGED]",
        )

    def test_labels_join_and_graphql_string(self) -> None:
        self.assertEqual(
            stats_module.labels_join(["bug", 'feature "fast"']),
            '["bug","feature \\"fast\\""]',
        )

    def test_issue_query_selectors(self) -> None:
        all_issues = stats_module.IssueQuery()
        self.assertEqual(all_issues.get_selector("total"), "total: issues")

        filtered_issues = stats_module.IssueQuery(
            states=[stats_module.IssueState.OPEN],
            labels=["needs triage"],
        )
        self.assertEqual(
            filtered_issues.get_selector("open_triage"),
            'open_triage: issues(states:[OPEN],labels:["needs triage"])',
        )

    def test_pull_request_query_selectors(self) -> None:
        all_prs = stats_module.PullRequestQuery()
        self.assertEqual(all_prs.get_selector("total"), "total: pullRequests")

        filtered_prs = stats_module.PullRequestQuery(
            states=[stats_module.PullRequestState.OPEN, stats_module.PullRequestState.MERGED],
            labels=["reviewed"],
        )
        self.assertEqual(
            filtered_prs.get_selector("prs_reviewed"),
            'prs_reviewed: pullRequests(states:[OPEN,MERGED],labels:["reviewed"])',
        )

    def test_generate_graphql_query_for_repo(self) -> None:
        repo = stats_module.Repo(
            owner="synthetic-owner",
            name="synthetic-repo",
            queries={
                "q_issues": stats_module.IssueQuery(states=[stats_module.IssueState.OPEN]),
            },
        )
        query = stats_module.generate_graphql_query_for_repo(repo)
        self.assertIn('repository(owner: "synthetic-owner", name: "synthetic-repo")', query)
        self.assertIn("q_issues: issues(states:[OPEN])", query)
        self.assertIn("totalCount", query)

    def test_first_or_default(self) -> None:
        items = [1, 2, 3, 4]
        self.assertEqual(stats_module.first_or_default(items, lambda x: x == 3), 3)
        self.assertIsNone(stats_module.first_or_default(items, lambda x: x == 10))


class TestGithubRepoStatsAggregation(unittest.TestCase):
    def test_do_graphql_query_success(self) -> None:
        synthetic_payload = {"data": {"repository": {"issues": {"totalCount": 12}}}}
        with mock_graphql_response(status_code=200, json_data=synthetic_payload) as (mock_post, _):
            result = stats_module.do_graphql_query("query { dummy }")
            self.assertEqual(result, synthetic_payload)
            mock_post.assert_called_once()
            _, kwargs = mock_post.call_args
            self.assertEqual(kwargs["json"], {"query": "query { dummy }"})
            self.assertEqual(kwargs["headers"]["Authorization"], "Bearer mock-token-for-tests")

    def test_stats_aggregation_populated_response(self) -> None:
        synthetic_repo = stats_module.Repo(
            owner="mock-org",
            name="mock-project",
            queries={
                "open_issues": stats_module.IssueQuery(states=[stats_module.IssueState.OPEN]),
                "open_prs": stats_module.PullRequestQuery(states=[stats_module.PullRequestState.OPEN]),
            },
        )
        query_map = {
            stats_module.repo_key(synthetic_repo): stats_module.generate_graphql_query_for_repo(synthetic_repo)
        }

        synthetic_data = {
            "data": {
                "repository": {
                    "open_issues": {"totalCount": 42},
                    "open_prs": {"totalCount": 17},
                }
            }
        }

        with mock_graphql_response(status_code=200, json_data=synthetic_data):
            with patch.object(stats_module, "REPO_CONFIG", [synthetic_repo]), \
                 patch.object(stats_module, "CACHED_QUERIES", query_map):
                collector = stats_module.StatsCollector()
                metrics = list(collector.collect())

        metric_dict = {m.name: m for m in metrics}
        self.assertIn("repo_stats_open_issues", metric_dict)
        self.assertIn("repo_stats_open_prs", metric_dict)
        self.assertIn("repo_stats_query_time", metric_dict)

        issues_metric = metric_dict["repo_stats_open_issues"]
        self.assertEqual(len(issues_metric.samples), 1)
        self.assertEqual(issues_metric.samples[0].labels, {"repo": "mock-org/mock-project"})
        self.assertEqual(issues_metric.samples[0].value, 42.0)

        prs_metric = metric_dict["repo_stats_open_prs"]
        self.assertEqual(len(prs_metric.samples), 1)
        self.assertEqual(prs_metric.samples[0].labels, {"repo": "mock-org/mock-project"})
        self.assertEqual(prs_metric.samples[0].value, 17.0)

        time_metric = metric_dict["repo_stats_query_time"]
        self.assertEqual(len(time_metric.samples), 1)
        self.assertGreaterEqual(time_metric.samples[0].value, 0.0)

    def test_stats_aggregation_populated_configured_repos(self) -> None:
        def side_effect(url, headers, json):
            query_str = json["query"]
            matching_repo = None
            for repo in stats_module.REPO_CONFIG:
                if f'name: "{repo.name}"' in query_str:
                    matching_repo = repo
                    break
            self.assertIsNotNone(matching_repo)

            counts = {key: {"totalCount": idx + 5} for idx, key in enumerate(matching_repo.queries.keys())}
            mock_resp = MagicMock()
            mock_resp.status_code = 200
            mock_resp.raise_for_status.return_value = None
            mock_resp.json.return_value = {"data": {"repository": counts}}

            ctx = MagicMock()
            ctx.__enter__.return_value = mock_resp
            ctx.__exit__.return_value = None
            return ctx

        with patch.object(stats_module.REQUESTS_SESSION, "post", side_effect=side_effect) as mock_post:
            collector = stats_module.StatsCollector()
            metrics = list(collector.collect())
            self.assertEqual(mock_post.call_count, len(stats_module.REPO_CONFIG))

        metric_names = {m.name for m in metrics}
        self.assertIn("repo_stats_issue_total_count", metric_names)
        self.assertIn("repo_stats_pr_total_count", metric_names)
        self.assertIn("repo_stats_query_time", metric_names)

        total_issue_metric = next(m for m in metrics if m.name == "repo_stats_issue_total_count")
        self.assertEqual(len(total_issue_metric.samples), len(stats_module.REPO_CONFIG))
        for sample in total_issue_metric.samples:
            self.assertEqual(sample.value, 5.0)

    def test_stats_aggregation_empty_response_zero_counts(self) -> None:
        synthetic_repo = stats_module.Repo(
            owner="mock-org",
            name="mock-project",
            queries={
                "open_issues": stats_module.IssueQuery(states=[stats_module.IssueState.OPEN]),
                "merged_prs": stats_module.PullRequestQuery(states=[stats_module.PullRequestState.MERGED]),
            },
        )
        query_map = {
            stats_module.repo_key(synthetic_repo): stats_module.generate_graphql_query_for_repo(synthetic_repo)
        }

        synthetic_empty_data = {
            "data": {
                "repository": {
                    "open_issues": {"totalCount": 0},
                    "merged_prs": {"totalCount": 0},
                }
            }
        }

        with mock_graphql_response(status_code=200, json_data=synthetic_empty_data):
            with patch.object(stats_module, "REPO_CONFIG", [synthetic_repo]), \
                 patch.object(stats_module, "CACHED_QUERIES", query_map):
                collector = stats_module.StatsCollector()
                metrics = list(collector.collect())

        metric_dict = {m.name: m for m in metrics}
        self.assertEqual(metric_dict["repo_stats_open_issues"].samples[0].value, 0.0)
        self.assertEqual(metric_dict["repo_stats_merged_prs"].samples[0].value, 0.0)

    def test_stats_aggregation_empty_response_missing_repo_or_data(self) -> None:
        synthetic_repo = stats_module.Repo(
            owner="mock-org",
            name="mock-project",
            queries={
                "open_issues": stats_module.IssueQuery(),
            },
        )
        query_map = {
            stats_module.repo_key(synthetic_repo): stats_module.generate_graphql_query_for_repo(synthetic_repo)
        }

        # Empty body {} missing 'data'
        with mock_graphql_response(status_code=200, json_data={}):
            with patch.object(stats_module, "REPO_CONFIG", [synthetic_repo]), \
                 patch.object(stats_module, "CACHED_QUERIES", query_map):
                collector = stats_module.StatsCollector()
                with self.assertRaises(KeyError):
                    list(collector.collect())

        # Response where repository is null (e.g. repo not found in GraphQL)
        with mock_graphql_response(status_code=200, json_data={"data": {"repository": None}}):
            with patch.object(stats_module, "REPO_CONFIG", [synthetic_repo]), \
                 patch.object(stats_module, "CACHED_QUERIES", query_map):
                collector = stats_module.StatsCollector()
                with self.assertRaises(TypeError):
                    list(collector.collect())


class TestGithubRepoStatsHttpErrors(unittest.TestCase):
    def test_http_error_401_unauthorized_surfaces_clean_failure(self) -> None:
        error = requests.exceptions.HTTPError("401 Client Error: Unauthorized")
        with mock_graphql_response(status_code=401, raise_error=error):
            with self.assertRaises(requests.exceptions.HTTPError) as ctx:
                stats_module.do_graphql_query("query { test }")
            self.assertEqual(ctx.exception, error)

    def test_http_error_404_not_found_surfaces_clean_failure(self) -> None:
        error = requests.exceptions.HTTPError("404 Client Error: Not Found")
        with mock_graphql_response(status_code=404, raise_error=error):
            collector = stats_module.StatsCollector()
            with self.assertRaises(requests.exceptions.HTTPError) as ctx:
                list(collector.collect())
            self.assertEqual(ctx.exception, error)

    def test_http_error_500_server_error_surfaces_clean_failure(self) -> None:
        error = requests.exceptions.HTTPError("500 Server Error: Internal Server Error")
        with mock_graphql_response(status_code=500, raise_error=error):
            collector = stats_module.StatsCollector()
            with self.assertRaises(requests.exceptions.HTTPError) as ctx:
                list(collector.collect())
            self.assertEqual(ctx.exception, error)

    def test_http_error_502_bad_gateway_surfaces_clean_failure(self) -> None:
        error = requests.exceptions.HTTPError("502 Server Error: Bad Gateway")
        with mock_graphql_response(status_code=502, raise_error=error):
            collector = stats_module.StatsCollector()
            with self.assertRaises(requests.exceptions.HTTPError) as ctx:
                list(collector.collect())
            self.assertEqual(ctx.exception, error)


class TestGithubRepoStatsMain(unittest.TestCase):
    def test_main_starts_server_and_signals_ready(self) -> None:
        with patch.object(sys, "argv", ["github_repo_stats.py", "9090", "--host", "127.0.0.1"]), \
             patch.object(stats_module, "CollectorRegistry") as mock_registry_cls, \
             patch.object(stats_module, "start_http_server") as mock_start_http, \
             patch("sdnotify.SystemdNotifier") as mock_notifier_cls, \
             patch("signal.pause") as mock_pause:
            mock_registry = MagicMock()
            mock_registry_cls.return_value = mock_registry
            mock_notifier = MagicMock()
            mock_notifier_cls.return_value = mock_notifier

            stats_module.main()

            mock_registry_cls.assert_called_once_with(auto_describe=True)
            mock_registry.register.assert_called_once()
            mock_start_http.assert_called_once_with(9090, "127.0.0.1", mock_registry)
            mock_notifier.notify.assert_called_once_with("READY=1")
            mock_pause.assert_called_once()


if __name__ == "__main__":
    unittest.main()
