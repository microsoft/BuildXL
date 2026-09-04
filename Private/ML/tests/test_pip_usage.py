"""Focused unit tests for Pip Usage dataset, tuning, export, and documentation helpers."""

import ast
from datetime import datetime, timedelta, timezone
from pathlib import Path
from tempfile import TemporaryDirectory
from threading import Event
from types import SimpleNamespace
import unittest

import pandas as pd
import numpy as np

import pip_usage.dataset as dataset
from pip_usage.dataset import (
    format_download_progress,
    merge_discovered_builds,
    parse_pip_description,
    prefetch_and_process,
    prepare,
    raw_query_batches,
    render_query,
    resolve_has_historic_perf_data,
    sample_builds_across_time,
    time_chunks,
    write_csv_atomic,
)
from pip_usage.export import _assert_no_sensitive_values, encode_frame, sample_cases, write_test_cases
from pip_usage.training import EvaluationPartitions, FEATURES, PRIORS, create_tuning_optimizer, partition_expected_mae


class DatasetTests(unittest.TestCase):
    """Verify bounded download, retry, progress, and file-write behavior."""

    def test_time_chunks_cover_window(self):
        """Time chunks cover the requested range without gaps or overlap."""
        start = datetime(2026, 1, 1, 6, tzinfo=timezone.utc)
        end = datetime(2026, 1, 3, 18, tzinfo=timezone.utc)
        self.assertEqual(list(time_chunks(start, end)), [
            (start, start + timedelta(days=1)),
            (start + timedelta(days=1), start + timedelta(days=2)),
            (start + timedelta(days=2), end),
        ])

    def test_history_availability_supports_old_and_new_events(self):
        """New events use the explicit flag while old events retain legacy zero-prior inference."""
        old_events = pd.DataFrame({
            "ExpectedProcessorUseInPercents": [0, 80],
            "ExpectedDurationSec": [0.0, 2.0],
        })
        self.assertEqual(resolve_has_historic_perf_data(old_events).tolist(), [False, True])

        new_events = old_events.copy()
        new_events["HasHistoricPerfData"] = [False, False]
        self.assertEqual(resolve_has_historic_perf_data(new_events).tolist(), [False, False])

        mixed_events = old_events.copy()
        mixed_events["HasHistoricPerfData"] = [None, True]
        self.assertEqual(resolve_has_historic_perf_data(mixed_events).tolist(), [False, True])

        old_row = {
            "BuildId": "build",
            "PreciseTimeStamp": "2026-01-01T00:00:00Z",
            "PipHash": "ABC",
            "PipDescription": "tool.exe, Module, operation, {}",
            "ProcessorUseInPercents": 80,
            "PeakWorkingSetMb": 100,
            "AverageWorkingSetMb": 50,
            "ActualDurationSec": 2.0,
            "ExpectedProcessorUseInPercents": 70,
            "ExpectedPeakWorkingSetMb": 90,
            "ExpectedAverageWorkingSetMb": 40,
            "ExpectedDurationSec": 1.5,
            "Weight": 1,
            "NumFileDependencies": 2,
            "NumDirectoryDependencies": 3,
            "NumFileOutputs": 4,
            "NumDirectoryOutputs": 5,
            "Codebase": "codebase",
            "StageId": "stage",
            "Queue": "queue",
            "Tenant": "tenant",
        }
        self.assertEqual(prepare(pd.DataFrame([old_row]))["ExpectedPipUsageSource"].iloc[0], "Unknown")
        new_row = {**old_row, "ExpectedPipUsageSource": "ML"}
        self.assertEqual(prepare(pd.DataFrame([new_row]))["ExpectedPipUsageSource"].iloc[0], "ML")

    def test_pip_description_parser_rejects_unstructured_javascript_description(self):
        """Short JavaScript descriptions cannot shift telemetry labels into feature columns."""
        description = " || msads-monorepo - campaignui-component-kit/packages/uc-channel-selection [lint]"
        self.assertEqual(parse_pip_description(description), ("unknown", "unknown", "unknown"))

    def test_pip_description_parser_extracts_structured_process_fields(self):
        """Structured descriptions retain tool, module, and qualifier metadata."""
        description = 'cl.exe (Microsoft C/C++ compiler), BuildXL.Engine, CompileFoo, {configuration:"release", platform:"x64"}'
        self.assertEqual(parse_pip_description(description), (
            "cl.exe (Microsoft C/C++ compiler)",
            "BuildXL.Engine",
            '{configuration:"release", platform:"x64"}',
        ))

    def test_pip_description_parser_preserves_graph_fragment_category(self):
        """Graph-fragment descriptions preserve the qualifier text used for DescriptionKind."""
        description = "BxlPipGraphFragmentGenerator.exe, EnlistBuild, import_fragment, {} || {(DScript to Pip Graph Fragment|Import x64.release)}"
        tool, module, qualifier = parse_pip_description(description)
        self.assertEqual(tool, "BxlPipGraphFragmentGenerator.exe")
        self.assertEqual(module, "EnlistBuild")
        self.assertIn("DScript to Pip Graph Fragment", qualifier)

    def test_merge_discovered_builds_applies_codebase_limit(self):
        """Discovery merges duplicates and retains time-distributed builds per codebase."""
        merged = merge_discovered_builds([
            pd.DataFrame([
                {"BuildId": "a1", "PipRows": 4, "FirstPipEvent": "2026-01-01T09:00:00Z", "LastPipEvent": "2026-01-01T10:00:00Z", "Codebase": "large", "StageId": "s", "Queue": "q", "Tenant": "t"},
                {"BuildId": "a2", "PipRows": 2, "FirstPipEvent": "2026-01-01T22:00:00Z", "LastPipEvent": "2026-01-01T23:59:00Z", "Codebase": "large", "StageId": "s", "Queue": "q", "Tenant": "t"},
                {"BuildId": "small", "PipRows": 1, "FirstPipEvent": "2026-01-01T11:00:00Z", "LastPipEvent": "2026-01-01T12:00:00Z", "Codebase": "small", "StageId": "s", "Queue": "q", "Tenant": "t"},
            ]),
            pd.DataFrame([
                {"BuildId": "a2", "PipRows": 3, "FirstPipEvent": "2026-01-02T00:00:00Z", "LastPipEvent": "2026-01-02T00:01:00Z", "Codebase": "large", "StageId": "s", "Queue": "q", "Tenant": "t"},
                {"BuildId": "a3", "PipRows": 6, "FirstPipEvent": "2026-01-02T01:00:00Z", "LastPipEvent": "2026-01-02T02:00:00Z", "Codebase": "large", "StageId": "s", "Queue": "q", "Tenant": "t"},
                {"BuildId": "unknown1", "PipRows": 1, "FirstPipEvent": "2026-01-02T00:30:00Z", "LastPipEvent": "2026-01-02T01:00:00Z", "Codebase": "", "StageId": "", "Queue": "", "Tenant": ""},
                {"BuildId": "unknown2", "PipRows": 1, "FirstPipEvent": "2026-01-02T01:30:00Z", "LastPipEvent": "2026-01-02T02:00:00Z", "Codebase": None, "StageId": None, "Queue": None, "Tenant": None},
            ]),
        ], max_builds=0, builds_per_codebase=2)
        self.assertEqual(set(merged["BuildId"]), {"a1", "a3", "small", "unknown1", "unknown2"})
        self.assertEqual(set(merged.loc[merged["Codebase"].eq("unknown"), "BuildId"]), {"unknown1", "unknown2"})

    def test_merge_discovered_builds_reports_empty_window(self):
        """An expired or empty telemetry window fails with an actionable error."""
        with self.assertRaisesRegex(RuntimeError, "choose a more recent or wider window"):
            merge_discovered_builds([pd.DataFrame(), pd.DataFrame()], max_builds=0, builds_per_codebase=100)

    def test_time_sampling_is_deterministic_and_covers_the_window(self):
        """Sampling selects early, middle, and late builds and keeps groups below the limit intact."""
        builds = pd.DataFrame({
            "BuildId": [f"build-{index}" for index in range(7)],
            "LastPipEvent": pd.date_range("2026-01-01", periods=7, freq="D", tz="UTC"),
        })
        first = sample_builds_across_time(builds, 3)
        second = sample_builds_across_time(builds.sample(frac=1, random_state=42), 3)
        self.assertEqual(first["BuildId"].tolist(), ["build-0", "build-3", "build-6"])
        self.assertEqual(second["BuildId"].tolist(), first["BuildId"].tolist())
        self.assertEqual(sample_builds_across_time(builds.head(2), 3)["BuildId"].tolist(), ["build-0", "build-1"])
        self.assertEqual(sample_builds_across_time(builds, 1)["BuildId"].tolist(), ["build-3"])

    def test_raw_query_batches_obey_row_and_build_limits(self):
        """Raw Kusto batches stop at either estimated rows or build count."""
        adaptive = list(raw_query_batches(pd.DataFrame([
            {"BuildId": "small1", "PipRows": 100},
            {"BuildId": "small2", "PipRows": 200},
            {"BuildId": "large", "PipRows": 900},
            {"BuildId": "small3", "PipRows": 100},
        ]), target_rows=500, max_builds=3))
        self.assertEqual(adaptive, [["small1", "small2"], ["large"], ["small3"]])
        capped = raw_query_batches(pd.DataFrame([
            {"BuildId": str(index), "PipRows": 1} for index in range(5)
        ]), target_rows=100, max_builds=2)
        self.assertEqual(list(capped), [["0", "1"], ["2", "3"], ["4"]])

    def test_kusto_templates_render_with_explicit_parameters(self):
        """Packaged KQL templates render completely and reject parameter contract drift."""
        discovery = render_query(
            "discover_builds.kql",
            chunk_start="2026-01-01T00:00:00Z",
            chunk_end="2026-01-02T00:00:00Z",
            window_start="2026-01-01T00:00:00Z",
            window_end="2026-01-08T00:00:00Z",
            builds_per_codebase=100,
        )
        download = render_query(
            "download_pip_usage.kql",
            ids='"build-id"',
            build_metadata='"build-id","codebase","train"',
            batch_start="2026-01-01T00:00:00Z",
            batch_end="2026-01-01T01:00:00Z",
        )
        for text in (discovery, download):
            self.assertNotIn("$", text)
            self.assertIn("// @cluster: cbuild", text)
            self.assertIn("// @database: CloudBuildProd", text)
            self.assertIn("CODESYNC:", text)
        with self.assertRaises(KeyError):
            render_query("discover_builds.kql", chunk_start="2026-01-01T00:00:00Z")
        with self.assertRaises(ValueError):
            render_query(
                "discover_builds.kql",
                chunk_start="2026-01-01T00:00:00Z",
                chunk_end="2026-01-02T00:00:00Z",
                window_start="2026-01-01T00:00:00Z",
                window_end="2026-01-08T00:00:00Z",
                builds_per_codebase=100,
                unused=True,
            )

    def test_download_progress_reports_remaining_work(self):
        """Progress text includes completed, remaining, elapsed, and estimated work."""
        self.assertEqual(format_download_progress(2, 4, 20, 40, 500, 1000, 60), (
            "Progress: 2/4 batches, 20/40 builds, ~500/1,000 rows (50.0%); remaining: "
            "2 batches, 20 builds, ~500 rows; elapsed 1m 0s, ETA 1m 0s."
        ))

    def test_atomic_csv_write_preserves_existing_file_on_failure(self):
        """A failed CSV write removes its temporary file and preserves the destination."""
        with TemporaryDirectory() as directory:
            destination = Path(directory) / "part.csv"
            destination.write_text("complete", encoding="utf-8")

            class BrokenFrame:
                """Simulate a frame that fails after writing partial output."""

                def to_csv(self, path, index):
                    """Write partial content and raise the injected failure."""
                    path.write_text("partial", encoding="utf-8")
                    raise OSError("injected write failure")

            with self.assertRaises(OSError):
                write_csv_atomic(BrokenFrame(), destination)
            self.assertEqual(destination.read_text(encoding="utf-8"), "complete")
            self.assertFalse(destination.with_suffix(".csv.tmp").exists())

    def test_prefetch_overlaps_download_and_processing(self):
        """The next download starts while the previous result is processed."""
        next_download_started = Event()
        processed = []

        def download(item):
            """Signal when the second item begins downloading."""
            if item == 1:
                next_download_started.set()
            return item

        def process(item):
            """Require the next download to begin before processing the first item."""
            if item == 0:
                self.assertTrue(next_download_started.wait(2))
            processed.append(item)

        prefetch_and_process([0, 1, 2], download, process)
        self.assertEqual(sorted(processed), [0, 1, 2])

    def test_query_retries_transient_network_failure(self):
        """A temporary Kusto network failure is retried before returning results."""
        attempts = 0

        class FlakyClient:
            """Fail the first query attempt and succeed on the second."""

            def execute(self, database, text, properties):
                """Return a table after one injected network failure."""
                nonlocal attempts
                attempts += 1
                if attempts == 1:
                    raise dataset.KustoNetworkError("https://cbuild.kusto.windows.net", None)
                return SimpleNamespace(primary_results=["table"])

        original_converter = dataset.dataframe_from_result_table
        original_sleep = dataset.time.sleep
        dataset.dataframe_from_result_table = lambda table: table
        dataset.time.sleep = lambda seconds: None
        try:
            self.assertEqual(dataset.query(FlakyClient(), object(), "CloudBuildProd", "query"), "table")
            self.assertEqual(attempts, 2)
        finally:
            dataset.dataframe_from_result_table = original_converter
            dataset.time.sleep = original_sleep


class TuningTests(unittest.TestCase):
    """Verify Bayesian suggestions are reproducible and depend on observed scores."""

    @staticmethod
    def run_optimizer(scores):
        """Register scores against seeded initial points and return the next suggestion."""
        optimizer = create_tuning_optimizer("cpu")
        points = optimizer.random_sample(3)
        for point, score in zip(points, scores):
            optimizer.register(params=point, target=score)
        return points, optimizer.suggest()

    def test_suggestions_are_adaptive_and_reproducible(self):
        """Equal observations reproduce suggestions while different scores change them."""
        points_a, suggestion_a = self.run_optimizer([-3.0, -2.0, -1.0])
        points_b, suggestion_b = self.run_optimizer([-1.0, -2.0, -3.0])
        points_c, suggestion_c = self.run_optimizer([-3.0, -2.0, -1.0])
        self.assertEqual(points_a, points_b)
        self.assertEqual(points_a, points_c)
        self.assertEqual(suggestion_a, suggestion_c)
        self.assertNotEqual(suggestion_a, suggestion_b)
        self.assertIsInstance(suggestion_a["num_leaves"], int)
        self.assertIsInstance(suggestion_a["min_child_samples"], int)


class EvaluationTests(unittest.TestCase):
    """Verify test metrics include the historical expected-value baseline."""

    def test_expected_mae_uses_the_same_warm_rows_as_model_mae(self):
        """Expected-value MAE compares historical estimates with actual values on warm rows."""
        with TemporaryDirectory() as directory:
            directory = Path(directory)
            features = np.zeros((3, len(FEATURES)))
            features[:, FEATURES.index(PRIORS["cpu"])] = [90.0, 260.0, 0.0]
            feature_path = directory / "features.npy"
            target_path = directory / "targets.npy"
            cold_path = directory / "cold.npy"
            np.save(feature_path, features)
            np.save(target_path, np.array([100.0, 200.0, 300.0]))
            np.save(cold_path, np.array([False, False, True]))
            evaluation = EvaluationPartitions(
                feature_paths=[feature_path],
                target_paths={"cpu": [target_path]},
                cold_paths=[cold_path],
                fixtures=pd.DataFrame(),
            )
            mae, value_range = partition_expected_mae(evaluation, "cpu")
            self.assertEqual(mae, 35.0)
            self.assertEqual(value_range, 100.0)


class ExportTests(unittest.TestCase):
    """Verify package policy checks do not alter categorical encoding."""

    def test_policy_violations_fail_without_changing_categories(self):
        """Categories retain their indexes and prohibited payloads fail explicitly."""
        guid = "12345678-1234-1234-1234-123456789abc"
        path = r"D:\agent\_work\src\project.dsc"
        vocabulary = {"Feature": [path, guid, "safe", "rare"]}
        encoded = encode_frame(pd.DataFrame({"Feature": [path, guid, "safe"]}), ["Feature"], vocabulary)
        self.assertEqual(encoded[:, 0].tolist(), [0.0, 1.0, 2.0])
        for artifact_name, payload in {
            "model.json": {"pandas_categorical": [[path]]},
            "model_spec.json": {"vocabularies": {"Tenant": [guid]}},
            "test_cases.json": [{"categorical": {"Qualifier": path}}],
        }.items():
            with self.assertRaisesRegex(ValueError, artifact_name):
                _assert_no_sensitive_values(payload, artifact_name)

    def test_fixture_json_uses_null_for_missing_numeric_features(self):
        """Missing model inputs remain representable without non-standard JSON NaN tokens."""
        class ConstantModel:
            """Return a finite raw prediction for every fixture row."""

            def predict(self, rows):
                """Return one constant prediction per row."""
                return np.ones(len(rows))

        rows = pd.DataFrame({"Category": ["known"], "Numeric": [np.nan]})
        cases = sample_cases(ConstantModel(), ["Category", "Numeric"], ["Category"], {"Category": ["known", "rare"]}, rows)
        self.assertIsNone(cases[0]["numeric"]["Numeric"])
        with TemporaryDirectory() as directory:
            path = write_test_cases({"predictions": {"cpu": cases}}, Path(directory) / "test_cases.json")
            text = path.read_text(encoding="utf-8")
            self.assertIn('"Numeric": null', text)
            self.assertNotIn("NaN", text)

    def test_fixture_export_rejects_infinite_values(self):
        """Infinite model inputs and outputs cannot enter parity fixtures."""
        class ConstantModel:
            """Return a finite raw prediction for every fixture row."""

            def predict(self, rows):
                """Return one constant prediction per row."""
                return np.ones(len(rows))

        rows = pd.DataFrame({"Numeric": [np.inf]})
        with self.assertRaisesRegex(ValueError, "Non-finite numeric fixture value"):
            sample_cases(ConstantModel(), ["Numeric"], [], {}, rows)

        class InfiniteModel:
            """Return an invalid infinite raw prediction."""

            def predict(self, rows):
                """Return one infinite prediction per row."""
                return np.full(len(rows), np.inf)

        with self.assertRaisesRegex(ValueError, "Non-finite model output"):
            sample_cases(InfiniteModel(), ["Numeric"], [], {}, pd.DataFrame({"Numeric": [1.0]}))


class DocumentationTests(unittest.TestCase):
    """Verify production Python symbols have docstrings."""

    def test_all_classes_functions_and_methods_are_documented(self):
        """Every class, function, and method in production and test Python files has a docstring."""
        missing = []
        ml_root = Path(__file__).parents[1]
        for source_root in (ml_root / "pip_usage", ml_root / "tests"):
            for path in sorted(source_root.glob("*.py")):
                tree = ast.parse(path.read_text(encoding="utf-8"))
                for node in ast.walk(tree):
                    if isinstance(node, (ast.ClassDef, ast.FunctionDef, ast.AsyncFunctionDef)) and ast.get_docstring(node) is None:
                        missing.append(f"{path}:{node.lineno}:{node.name}")
        self.assertEqual(missing, [], "Undocumented Python symbols: " + ", ".join(missing))


if __name__ == "__main__":
    unittest.main()