"""Export trained LightGBM models as JSON for BuildXL's C# evaluator.

The output contains the decision trees, feature schema, category mappings, and sample predictions.
The exported trees are evaluated in Python and compared with LightGBM so incompatible JSON cannot be packaged.
"""

from __future__ import annotations

import json
import math
import re
from datetime import datetime, timezone
from hashlib import sha256
from pathlib import Path
from typing import Any, Mapping, Sequence

import numpy as np

__all__ = [
    "export_models",
]

# --- Artifact policy validation ---------------------------------------------------------------------
#
# Every artifact is checked at the serialization boundary. A policy violation fails export instead of
# changing model inputs, vocabularies, or expected predictions.
_PATH_RE = re.compile(r"(?:[A-Za-z]:\\|\\\\)[^\s|)}\"]*")
_GUID_RE = re.compile(r"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b")


def _assert_no_sensitive_values(obj: Any, artifact_name: str) -> None:
    """Reject nested JSON-like data that violates the model-package content policy."""
    if isinstance(obj, str):
        if _PATH_RE.search(obj) or _GUID_RE.search(obj):
            raise ValueError(f"{artifact_name} contains a Windows path or GUID; remove it before export.")
        return
    if isinstance(obj, dict):
        for key, value in obj.items():
            _assert_no_sensitive_values(key, artifact_name)
            _assert_no_sensitive_values(value, artifact_name)
        return
    if isinstance(obj, list):
        for value in obj:
            _assert_no_sensitive_values(value, artifact_name)



def dump_lgbm(model, out_path: str | Path) -> Path:
    """Write a trained LightGBM model as the JSON read by BuildXL's C# evaluator.

    The model-package content policy is checked before writing.
    """
    out_path = Path(out_path)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    dump = model.booster_.dump_model()
    _assert_no_sensitive_values(dump, out_path.name)
    out_path.write_text(json.dumps(dump), encoding="utf-8")
    return out_path


def encode_frame(frame, feature_names: Sequence[str], vocabularies: Mapping[str, Sequence[str]]) -> np.ndarray:
    """Convert raw feature columns into the numeric values expected by the exported model.

    Text values become vocabulary indexes; unseen values use the ``rare`` index or ``-1``.
    """
    columns = []
    for name in feature_names:
        values = frame[name]
        if name not in vocabularies:
            columns.append(values.to_numpy(dtype="float64"))
            continue
        codes = {value: index for index, value in enumerate(vocabularies[name])}
        fallback = codes.get("rare", -1)
        columns.append(values.astype(str).map(lambda value: codes.get(value, fallback)).to_numpy(dtype="float64"))
    return np.column_stack(columns).astype(np.float64)


def predict_exported_raw(model_dump: dict[str, Any], encoded_rows: np.ndarray) -> np.ndarray:
    """Calculate raw predictions directly from exported JSON, without using LightGBM."""
    def evaluate(node: dict[str, Any], row: np.ndarray) -> float:
        """Follow one decision tree for one input row and return its leaf value."""
        while "leaf_value" not in node:
            value = row[node["split_feature"]]
            if node["decision_type"] == "<=":
                go_left = node["default_left"] if math.isnan(value) else value <= node["threshold"]
            elif node["decision_type"] == "==":
                categories = {int(item) for item in str(node["threshold"]).split("||")}
                go_left = node["default_left"] if math.isnan(value) else int(value) in categories
            else:
                raise ValueError(f"Unsupported LightGBM decision type: {node['decision_type']}")
            node = node["left_child"] if go_left else node["right_child"]
        return node["leaf_value"]

    trees = [tree["tree_structure"] for tree in model_dump["tree_info"]]
    return np.array([sum(evaluate(tree, row) for tree in trees) for row in encoded_rows], dtype=np.float64)


def validate_export(model, exported_path: Path, fixture_rows, feature_names: Sequence[str], vocabularies: Mapping[str, Sequence[str]]) -> float:
    """Confirm exported JSON predicts the same values as the trained Python model.

    The function raises an error when the largest difference exceeds one millionth.
    """
    dumped = json.loads(exported_path.read_text(encoding="utf-8"))
    encoded = encode_frame(fixture_rows, feature_names, vocabularies)
    exported = predict_exported_raw(dumped, encoded)
    expected = np.asarray(model.predict(fixture_rows[list(feature_names)])).ravel()
    maximum_difference = float(np.max(np.abs(expected - exported))) if expected.size else 0.0
    if maximum_difference > 1e-6:
        raise AssertionError(f"Export validation failed for {exported_path.name}: max difference {maximum_difference:.3e}.")
    return maximum_difference


def _apply_transform(raw: np.ndarray, transform: str) -> np.ndarray:
    """Apply the requested conversion from raw model scores to final prediction units."""
    if transform == "expm1":
        return np.expm1(raw)
    if transform in (None, "", "identity"):
        return raw
    raise ValueError(f"Unsupported output_transform '{transform}'.")


def sample_cases(
    model,
    feature_names: Sequence[str],
    categorical_features: Sequence[str],
    vocabularies: Mapping[str, Sequence[str]],
    X_rows,
    transform: str = "expm1",
) -> list[dict]:
    """Create sample inputs and expected predictions for testing the C# evaluator.

    Each case includes the original text and numeric features plus both the raw model score and
    final prediction.
    """
    cat_set = set(categorical_features)
    ordered = list(feature_names)
    raw = np.asarray(model.predict(X_rows[ordered])).ravel()
    pred = _apply_transform(raw, transform)
    cases = []
    for i in range(len(X_rows)):
        row = X_rows.iloc[i]
        numeric = {}
        for name in ordered:
            if name in cat_set:
                continue
            value = float(row[name])
            if math.isinf(value):
                raise ValueError(f"Non-finite numeric fixture value for feature '{name}' in row {i}: {value!r}")
            numeric[name] = None if math.isnan(value) else value
        raw_score = float(raw[i])
        prediction = float(pred[i])
        if not math.isfinite(raw_score) or not math.isfinite(prediction):
            raise ValueError(f"Non-finite model output in fixture row {i}: rawScore={raw_score!r}, prediction={prediction!r}")
        cases.append({
            "categorical": {c: str(row[c]) for c in ordered if c in cat_set},
            "numeric": numeric,
            "rawScore": raw_score,
            "prediction": prediction,
        })
    return cases


def write_test_cases(test_cases: dict[str, Any], out_path: str | Path) -> Path:
    """Write sample predictions used to compare the C# and Python evaluators.

    A schema version is added when missing and package content policy is checked before writing.
    """
    test_cases = dict(test_cases)
    test_cases.setdefault("schemaVersion", 1)
    out_path = Path(out_path)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    _assert_no_sensitive_values(test_cases, out_path.name)
    out_path.write_text(json.dumps(test_cases, indent=2, allow_nan=False), encoding="utf-8")
    return out_path


def write_spec(spec: dict[str, Any], out_path: str | Path) -> Path:
    """Write the model schema and file map after checking package content policy.

    A schema version and creation time are added when missing. Keys remain unchanged because they must
    match the names read by the C# model specification.
    """
    spec = dict(spec)
    spec.setdefault("schemaVersion", 1)
    spec.setdefault("createdUtc", datetime.now(timezone.utc).isoformat())
    out_path = Path(out_path)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    _assert_no_sensitive_values(spec, out_path.name)
    out_path.write_text(json.dumps(spec, indent=2), encoding="utf-8")
    return out_path


def export_models(
    models: Mapping[str, Any],
    fixture_rows,
    output_dir: str | Path,
    feature_names: Sequence[str],
    categorical_features: Sequence[str],
    vocabularies: Mapping[str, Sequence[str]],
    targets: Mapping[str, str],
    manifest: Mapping[str, Any],
) -> dict[str, str]:
    """Write all model JSON files and return a SHA-256 hash for each file.

    The output includes one model per target, the model specification, and sample predictions that
    verify the C# evaluator. Every output is checked against package content policy.
    """
    output_dir = Path(output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)
    manifest = dict(manifest)
    model_kind = manifest["modelKind"]
    artifact_prefix = re.sub(r"(?<!^)(?=[A-Z])", "_", model_kind).lower()
    artifact_names = {target: f"{artifact_prefix}_{target}.json" for target in models}
    for target, model in models.items():
        path = dump_lgbm(model, output_dir / artifact_names[target])
        validate_export(model, path, fixture_rows[target], feature_names, vocabularies)

    manifest.setdefault("categoricalFeatures", list(categorical_features))
    manifest.setdefault("vocabularies", {name: list(values) for name, values in vocabularies.items()})
    manifest.setdefault("targets", dict(targets))
    manifest.setdefault("features", list(feature_names))
    manifest["modelFiles"] = artifact_names
    write_spec(manifest, output_dir / "model_spec.json")
    fixtures = {
        "modelKind": manifest["modelKind"],
        "modelHashes": {
            target: sha256((output_dir / artifact_names[target]).read_bytes()).hexdigest()
            for target in models
        },
        "predictions": {
            target: sample_cases(model, feature_names, categorical_features, vocabularies, fixture_rows[target])
            for target, model in models.items()
        },
    }
    write_test_cases(fixtures, output_dir / "test_cases.json")
    return {path.name: sha256(path.read_bytes()).hexdigest() for path in output_dir.glob("*.json")}
