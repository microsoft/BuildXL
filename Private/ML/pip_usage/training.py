"""Train, validate, and export Pip Usage models."""
from __future__ import annotations

import argparse
import gc
import json
from bisect import bisect_right
from collections import Counter
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import lightgbm as lgb
import numpy as np
import pandas as pd
from bayes_opt import BayesianOptimization
from bayes_opt.acquisition import ExpectedImprovement

from pip_usage import export
from pip_usage.features import (
    PIP_USAGE_CATEGORICAL_FEATURES,
    PIP_USAGE_FEATURES,
    PIP_USAGE_HAS_HISTORIC_PERF_DATA,
    PIP_USAGE_PRIORS,
    PIP_USAGE_TARGETS,
)

CATEGORICAL = PIP_USAGE_CATEGORICAL_FEATURES
FEATURES = PIP_USAGE_FEATURES
PRIORS = PIP_USAGE_PRIORS
TARGETS = PIP_USAGE_TARGETS
HAS_HISTORIC_PERF_DATA = PIP_USAGE_HAS_HISTORIC_PERF_DATA
MODEL_PARAMS = {
    "estimators": 800,
    "learning_rate": 0.04,
    "random_seed": 42,
    "workers": -1,
    "early_stopping_rounds": 40,
}
FAMILY_PARAMS = {
    "objective": "regression_l1",
    "feature_pre_filter": False,
    "num_leaves": 63,
    "min_child_samples": 100,
    "subsample": 0.8,
    "colsample_bytree": 0.8,
    "reg_lambda": 1.0,
    "verbose": -1,
}
TUNING_RANGES = {
    "learning_rate": (0.02, 0.08),
    "num_leaves": (16, 127),
    "min_child_samples": (20, 200),
}
TUNING_INITIAL_TRIALS = 3
MODEL_EVALUATION_PATHS = ("warm", "warm_masked", "cold")


class PartitionSequence(lgb.Sequence):
    """Let LightGBM train on rows from several files without loading the full dataset into RAM."""

    batch_size = 65536

    def __init__(self, paths: list[Path]):
        """Open each feature file for read-only, on-demand access."""
        self.paths = paths
        self.arrays = [np.load(path, mmap_mode="r") for path in paths]
        self.ends = np.cumsum([len(array) for array in self.arrays]).tolist()

    def __len__(self) -> int:
        """Return the total number of rows across all feature files."""
        return self.ends[-1] if self.ends else 0

    def __getitem__(self, item):
        """Return one row or a batch of rows from the memory-mapped feature files."""
        if isinstance(item, slice):
            start, stop, step = item.indices(len(self))
            if step != 1:
                return np.vstack([self[index] for index in range(start, stop, step)])
            chunks = []
            while start < stop:
                partition = bisect_right(self.ends, start)
                offset = start - (self.ends[partition - 1] if partition else 0)
                count = min(stop - start, len(self.arrays[partition]) - offset)
                chunks.append(self.arrays[partition][offset:offset + count])
                start += count
            return np.vstack(chunks) if len(chunks) > 1 else chunks[0]
        partition = bisect_right(self.ends, item)
        offset = item - (self.ends[partition - 1] if partition else 0)
        return self.arrays[partition][offset]


@dataclass
class NativeModel:
    """Keep a trained LightGBM model together with the text-to-number mappings it expects."""

    booster_: lgb.Booster
    vocabularies: dict[str, list[str]]

    def predict(self, frame: pd.DataFrame) -> np.ndarray:
        """Convert raw pip features to model inputs and return the predicted values."""
        return self.booster_.predict(export.encode_frame(frame, FEATURES, self.vocabularies))


def partition_vocabularies(paths: list[Path]) -> dict[str, list[str]]:
    """Keep text values seen at least twice in training rows and add a ``rare`` fallback."""
    counts = {column: Counter() for column in CATEGORICAL}
    for path in paths:
        train = pd.read_pickle(path).loc[lambda frame: frame["Split"].eq("train")]
        for column in CATEGORICAL:
            counts[column].update(train[column].astype(str))
    return {
        column: sorted({value for value, count in column_counts.items() if count >= 2} | {"rare"})
        for column, column_counts in counts.items()
    }


def encode_native_partitions(paths: list[Path], work_dir: Path, vocabularies: dict[str, list[str]]) -> tuple[list[Path], dict[str, list[Path]]]:
    """Convert training rows to numeric feature and label files that LightGBM can read in batches.

    Only training rows are included. Warm pips are stored once with history and once with
    history hidden; cold pips are stored with history hidden.
    """
    work_dir.mkdir(parents=True, exist_ok=True)
    feature_paths: list[Path] = []
    label_paths = {target: [] for target in TARGETS}
    priors = [FEATURES.index(name) for name in PRIORS.values()]
    for index, path in enumerate(paths):
        train = pd.read_pickle(path).loc[lambda frame: frame["Split"].eq("train")]
        if train.empty:
            continue
        encoded = export.encode_frame(train, FEATURES, vocabularies)
        cold = ~train[HAS_HISTORIC_PERF_DATA]
        masked = encoded.copy()
        masked[:, priors] = np.nan
        augmented = np.vstack([encoded[~cold.to_numpy()], masked])
        feature_path = work_dir / f"features-{index:02d}.npy"
        np.save(feature_path, augmented)
        feature_paths.append(feature_path)
        for target, target_column in TARGETS.items():
            labels = np.log1p(train[target_column].clip(lower=0).to_numpy(dtype=float))
            augmented_labels = np.concatenate([labels[~cold.to_numpy()], labels])
            label_path = work_dir / f"labels-{target}-{index:02d}.npy"
            np.save(label_path, augmented_labels)
            label_paths[target].append(label_path)
    return feature_paths, label_paths


@dataclass
class EvaluationPartitions:
    """Store paths to validation or test arrays and up to 20 original rows for export tests."""

    feature_paths: list[Path]
    target_paths: dict[str, list[Path]]
    cold_paths: list[Path]
    fixtures: pd.DataFrame


@dataclass
class TrainingInputs:
    """Store category mappings, training-array paths, and the validation and test file sets."""

    cohort: Path
    vocabularies: dict[str, list[str]]
    feature_paths: list[Path]
    label_paths: dict[str, list[Path]]
    validation: EvaluationPartitions
    test: EvaluationPartitions


def encode_evaluation_partitions(paths: list[Path], work_dir: Path, vocabularies: dict[str, list[str]], split: str) -> EvaluationPartitions:
    """Write numeric feature, target, and cold-pip files for one validation or test split.

    Up to 20 original rows are also retained for exported-model compatibility tests.
    """
    work_dir.mkdir(parents=True, exist_ok=True)
    feature_paths: list[Path] = []
    target_paths = {target: [] for target in TARGETS}
    cold_paths: list[Path] = []
    fixtures = []
    for index, path in enumerate(paths):
        selected = pd.read_pickle(path).loc[lambda frame: frame["Split"].eq(split)]
        if selected.empty:
            continue
        feature_path = work_dir / f"{split}-features-{index:02d}.npy"
        np.save(feature_path, export.encode_frame(selected, FEATURES, vocabularies))
        feature_paths.append(feature_path)
        cold_path = work_dir / f"{split}-cold-{index:02d}.npy"
        np.save(cold_path, (~selected[HAS_HISTORIC_PERF_DATA]).to_numpy())
        cold_paths.append(cold_path)
        for target, target_column in TARGETS.items():
            target_path = work_dir / f"{split}-target-{target}-{index:02d}.npy"
            np.save(target_path, selected[target_column].to_numpy(dtype=float))
            target_paths[target].append(target_path)
        if sum(len(frame) for frame in fixtures) < 20:
            fixtures.append(selected.head(20 - sum(len(frame) for frame in fixtures)))
    if not feature_paths:
        raise RuntimeError(f"Prepared dataset has no {split} rows.")
    return EvaluationPartitions(feature_paths, target_paths, cold_paths, pd.concat(fixtures, ignore_index=True))


def partition_mae(booster: lgb.Booster, evaluation: EvaluationPartitions, target: str, path: str | None = None) -> tuple[float | None, float]:
    """Measure mean absolute error and actual-value range for a selected set of pips.

    ``cold`` selects cold pips and hides history. ``warm`` selects warm pips with history.
    ``warm_masked`` selects warm pips and hides history. Any other value selects all pips.
    The returned error is ``None`` when the selected set contains no rows.
    """
    error = 0.0
    count = 0
    minimum = np.inf
    maximum = -np.inf
    prior_indices = [FEATURES.index(name) for name in PRIORS.values()]
    for feature_path, target_path, cold_path in zip(evaluation.feature_paths, evaluation.target_paths[target], evaluation.cold_paths):
        features = np.load(feature_path, mmap_mode="r")
        actual = np.load(target_path, mmap_mode="r")
        cold = np.load(cold_path, mmap_mode="r")
        selector = cold if path == "cold" else ~cold if path in {"warm", "warm_masked"} else np.ones(len(cold), dtype=bool)
        if not selector.any():
            continue
        selected_features = np.asarray(features[selector])
        if path in {"cold", "warm_masked"}:
            selected_features[:, prior_indices] = np.nan
        selected_actual = np.asarray(actual[selector])
        prediction = np.expm1(booster.predict(selected_features))
        error += float(np.abs(selected_actual - prediction).sum())
        count += len(selected_actual)
        minimum = min(minimum, float(selected_actual.min()))
        maximum = max(maximum, float(selected_actual.max()))
    return (error / count if count else None), (maximum - minimum if count else 0.0)


def partition_expected_mae(evaluation: EvaluationPartitions, target: str) -> tuple[float | None, float]:
    """Measure historical expected-value MAE and actual-value range on warm test rows."""
    error = 0.0
    count = 0
    minimum = np.inf
    maximum = -np.inf
    prior_index = FEATURES.index(PRIORS[target])
    for feature_path, target_path, cold_path in zip(evaluation.feature_paths, evaluation.target_paths[target], evaluation.cold_paths):
        features = np.load(feature_path, mmap_mode="r")
        actual = np.load(target_path, mmap_mode="r")
        warm = ~np.load(cold_path, mmap_mode="r")
        if not warm.any():
            continue
        selected_actual = np.asarray(actual[warm])
        selected_expected = np.asarray(features[warm, prior_index])
        error += float(np.abs(selected_actual - selected_expected).sum())
        count += len(selected_actual)
        minimum = min(minimum, float(selected_actual.min()))
        maximum = max(maximum, float(selected_actual.max()))
    return (error / count if count else None), (maximum - minimum if count else 0.0)


def evaluate_partitioned(models: dict[str, NativeModel], evaluation: EvaluationPartitions) -> tuple[dict[str, dict[str, float | None]], dict[str, dict[str, float | None]]]:
    """Measure model and historical expected-value MAE on the test rows."""
    metrics: dict[str, dict[str, float | None]] = {}
    normalized: dict[str, dict[str, float | None]] = {}
    for target, model in models.items():
        metrics[target] = {}
        normalized[target] = {}
        for path in MODEL_EVALUATION_PATHS:
            mae, value_range = partition_mae(model.booster_, evaluation, target, path)
            metrics[target][path] = mae
            normalized[target][path] = None if mae is None or value_range <= 0 else mae / value_range
        expected_mae, expected_range = partition_expected_mae(evaluation, target)
        metrics[target]["expected"] = expected_mae
        normalized[target]["expected"] = None if expected_mae is None or expected_range <= 0 else expected_mae / expected_range
    return metrics, normalized


def native_params(model_params: dict[str, Any] | None = None) -> tuple[dict[str, Any], int, int]:
    """Build LightGBM settings and return them with training and early-stopping limits."""
    common = {**MODEL_PARAMS, **(model_params or {})}
    rounds = common.pop("estimators")
    early_stopping_rounds = common.pop("early_stopping_rounds")
    seed = common.pop("random_seed")
    workers = common.pop("workers")
    return {
        **FAMILY_PARAMS,
        **common,
        "metric": "l1",
        "seed": seed,
        "num_threads": workers,
    }, rounds, early_stopping_rounds


def create_tuning_optimizer(target: str) -> BayesianOptimization:
    """Create a reproducible optimizer that chooses model settings from earlier trial results."""
    seed = MODEL_PARAMS["random_seed"] + list(TARGETS).index(target)
    return BayesianOptimization(
        f=None,
        pbounds={
            "log_learning_rate": tuple(np.log(TUNING_RANGES["learning_rate"])),
            "num_leaves": (*TUNING_RANGES["num_leaves"], int),
            "min_child_samples": (*TUNING_RANGES["min_child_samples"], int),
        },
        acquisition_function=ExpectedImprovement(xi=0.01),
        random_state=seed,
        verbose=0,
    )


def model_params_from_tuning_point(point: dict[str, float | int]) -> dict[str, float | int]:
    """Convert an optimizer suggestion into valid LightGBM parameter values."""
    return {
        "learning_rate": float(np.exp(point["log_learning_rate"])),
        "num_leaves": int(point["num_leaves"]),
        "min_child_samples": int(point["min_child_samples"]),
    }


def prepare_training_inputs(cohort: Path) -> TrainingInputs:
    """Build vocabularies and write numeric training, validation, and test arrays for a cohort."""
    paths = sorted((cohort / "prepared_parts").glob("*.pkl"))
    if not paths:
        raise RuntimeError("Prepared dataset has no partitions.")
    vocabularies = partition_vocabularies(paths)
    vocabulary_cardinalities = {name: len(values) for name, values in vocabularies.items()}
    print(f"Categorical vocabulary cardinalities: {json.dumps(vocabulary_cardinalities, sort_keys=True)}", flush=True)
    large_vocabularies = {name: count for name, count in vocabulary_cardinalities.items() if count > 255}
    if large_vocabularies:
        print(
            "Categorical vocabularies above LightGBM's default max_bin=255: "
            f"{json.dumps(large_vocabularies, sort_keys=True)}. LightGBM may ignore max_bin for these native categorical features.",
            flush=True,
        )
    feature_paths, label_paths = encode_native_partitions(paths, cohort / "native_parts", vocabularies)
    validation = encode_evaluation_partitions(paths, cohort / "native_parts", vocabularies, "validation")
    test = encode_evaluation_partitions(paths, cohort / "native_parts", vocabularies, "test")
    return TrainingInputs(cohort, vocabularies, feature_paths, label_paths, validation, test)


def train_target(inputs: TrainingInputs, target: str, tuning_trials: int) -> tuple[NativeModel, dict[str, Any]]:
    """Try several model settings for one target and return the model with lowest validation error."""
    if target not in TARGETS:
        raise ValueError(f"Unknown target '{target}'. Expected one of: {', '.join(TARGETS)}.")
    if tuning_trials < 1:
        raise ValueError("Tuning trials must be at least one.")
    sequence = PartitionSequence(inputs.feature_paths)
    validation_sequence = PartitionSequence(inputs.validation.feature_paths)
    params, rounds, early_stopping_rounds = native_params()
    optimizer = create_tuning_optimizer(target)
    categorical_indices = list(range(len(CATEGORICAL)))
    labels = np.concatenate([np.load(path, mmap_mode="r") for path in inputs.label_paths[target]])
    train_set = lgb.Dataset(sequence, label=labels, feature_name=FEATURES, categorical_feature=categorical_indices, params=params, free_raw_data=False)
    binary_path = inputs.cohort / "native_parts" / f"train-{target}.bin"
    binary_path.unlink(missing_ok=True)
    train_set.save_binary(binary_path)
    del train_set, labels
    gc.collect()
    train_set = lgb.Dataset(binary_path, params=params, free_raw_data=True)
    validation_labels = np.log1p(np.concatenate([np.load(path, mmap_mode="r") for path in inputs.validation.target_paths[target]]).clip(min=0))
    validation_set = lgb.Dataset(validation_sequence, label=validation_labels, reference=train_set, feature_name=FEATURES, categorical_feature=categorical_indices, params=params, free_raw_data=False)
    validation_binary_path = inputs.cohort / "native_parts" / f"validation-{target}.bin"
    validation_binary_path.unlink(missing_ok=True)
    validation_set.save_binary(validation_binary_path)
    del validation_set, validation_labels
    gc.collect()
    validation_set = lgb.Dataset(validation_binary_path, reference=train_set, params=params, free_raw_data=True)

    best_mae = np.inf
    best_params = None
    best_booster = None
    initial_trials = min(TUNING_INITIAL_TRIALS, tuning_trials)
    for trial_index in range(1, tuning_trials + 1):
        point = optimizer.random_sample(1)[0] if trial_index <= initial_trials else optimizer.suggest()
        candidate = model_params_from_tuning_point(point)
        booster = lgb.train({**params, **candidate}, train_set, num_boost_round=rounds, valid_sets=[validation_set], callbacks=[lgb.early_stopping(early_stopping_rounds, verbose=False)])
        validation_mae, _ = partition_mae(booster, inputs.validation, target)
        optimizer.register(params=point, target=-validation_mae)
        print(f"{target} tuning trial {trial_index}/{tuning_trials} finished with validation MAE {validation_mae:.6f} and parameters {candidate}.", flush=True)
        if validation_mae < best_mae:
            best_mae = validation_mae
            best_params = candidate
            best_booster = booster
    model = NativeModel(best_booster, inputs.vocabularies)
    details = {
        "family": "lightgbm",
        "validation_mae": float(best_mae),
        "parameters": best_params,
        "best_iteration": best_booster.best_iteration,
    }
    del train_set, validation_set
    gc.collect()
    binary_path.unlink(missing_ok=True)
    validation_binary_path.unlink(missing_ok=True)
    return model, details


def train_partitioned_models(cohort: Path, tuning_trials: int) -> tuple[dict[str, NativeModel], dict[str, dict[str, Any]], EvaluationPartitions, pd.DataFrame]:
    """Train CPU, peak-memory, average-memory, and duration models from one prepared cohort."""
    inputs = prepare_training_inputs(cohort)
    models = {}
    details = {}
    for target in TARGETS:
        models[target], details[target] = train_target(inputs, target, tuning_trials)
    return models, details, inputs.test, inputs.test.fixtures


def enforce_quality(metrics: dict[str, dict[str, float | None]], normalized: dict[str, dict[str, float | None]], max_normalized_mae: float) -> None:
    """Fail when a required test group is missing or its normalized error exceeds the limit."""
    failures = []
    for target, paths in metrics.items():
        if any(paths.get(path) is None or normalized[target].get(path) is None for path in MODEL_EVALUATION_PATHS):
            failures.append(f"{target} lacks a required warm, warm-masked, or cold test group.")
        for path in MODEL_EVALUATION_PATHS:
            value = normalized[target].get(path)
            if value is not None and value > max_normalized_mae:
                failures.append(f"{target}/{path} normalized MAE {value:.6f} exceeds {max_normalized_mae:.6f}.")
    if failures:
        raise RuntimeError("Quality gate failed: " + " ".join(failures))


def export_pip_usage_models(models: dict[str, NativeModel], fixture_rows: pd.DataFrame, dataset_name: str, split_sha256: str, output_dir: Path) -> dict[str, str]:
    """Supply Pip Usage schema and training metadata to the generic JSON exporter."""
    return export.export_models(
        models=models,
        fixture_rows={target: fixture_rows[FEATURES].copy() for target in models},
        output_dir=output_dir,
        feature_names=FEATURES,
        categorical_features=CATEGORICAL,
        vocabularies=models["cpu"].vocabularies,
        targets=TARGETS,
        manifest={"modelKind": "pipUsage", "rareBucket": "rare", "outputTransform": "expm1", "training": {"dataset": dataset_name, "splitSha256": split_sha256}},
    )


def parse_args() -> argparse.Namespace:
    """Read model-training paths, trial count, and quality limit from the command line."""
    parser = argparse.ArgumentParser(description="Train and export Pip Usage models from one prepared dataset.")
    parser.add_argument("--dataset-root", type=Path, required=True)
    parser.add_argument("--dataset-name", required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--quality-report", type=Path, required=True)
    parser.add_argument("--tuning-trials", type=int, default=1)
    parser.add_argument("--max-normalized-mae", type=float, default=1.0)
    return parser.parse_args()


def main() -> None:
    """Train all models, test their quality, export JSON files, and write the quality report."""
    args = parse_args()
    cohort = args.dataset_root / "snapshots" / args.dataset_name
    split = json.loads((cohort / "split_manifest.json").read_text(encoding="utf-8"))
    models, training, evaluation, fixture_rows = train_partitioned_models(cohort, args.tuning_trials)
    metrics, normalized = evaluate_partitioned(models, evaluation)
    enforce_quality(metrics, normalized, args.max_normalized_mae)
    hashes = export_pip_usage_models(models, fixture_rows, args.dataset_name, split["sha256"], args.output_dir)
    report = {"modelKind": "pipUsage", "createdUtc": datetime.now(timezone.utc).isoformat(), "dataset": args.dataset_name, "splitSha256": split["sha256"], "datasetStatistics": split.get("statistics", {}), "vocabularyCardinalities": {name: len(values) for name, values in models["cpu"].vocabularies.items()}, "training": training, "testMae": metrics, "testNormalizedMae": normalized, "maxNormalizedMae": args.max_normalized_mae, "artifacts": hashes}
    args.quality_report.parent.mkdir(parents=True, exist_ok=True)
    args.quality_report.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
