"""Download, prepare, sample, and split a Pip Usage training dataset."""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import shutil
import time
from concurrent.futures import FIRST_COMPLETED, ThreadPoolExecutor, wait
from datetime import datetime, timedelta, timezone
from importlib.resources import files
from pathlib import Path
from string import Template
from threading import Lock

import pandas as pd
from azure.identity import AzureCliCredential
from azure.kusto.data import ClientRequestProperties, KustoClient, KustoConnectionStringBuilder
from azure.kusto.data.exceptions import KustoNetworkError
from azure.kusto.data.helpers import dataframe_from_result_table
from pip_usage.features import (
    PIP_USAGE_CATEGORICAL_FEATURES,
    PIP_USAGE_HAS_HISTORIC_PERF_DATA,
    PIP_USAGE_NUMERIC_FEATURES,
    PIP_USAGE_PRIORS,
    PIP_USAGE_TARGETS,
)

TARGETS = PIP_USAGE_TARGETS
PRIORS = PIP_USAGE_PRIORS
NUMERIC = PIP_USAGE_NUMERIC_FEATURES
CATEGORICAL = PIP_USAGE_CATEGORICAL_FEATURES
HAS_HISTORIC_PERF_DATA = PIP_USAGE_HAS_HISTORIC_PERF_DATA
RAW_QUERY_MAX_BUILDS = 50
RAW_QUERY_TARGET_ROWS = 750_000
RAW_QUERY_DOWNLOAD_WORKERS = 2
SAMPLE_PARTITIONS = 64
DEFAULT_BUILDS_PER_CODEBASE = 100
KUSTO_QUERY_TIMEOUT = timedelta(minutes=10)
KUSTO_QUERY_ATTEMPTS = 3


def render_query(name: str, **parameters: object) -> str:
    """Render a packaged KQL template, rejecting missing or unused parameters."""
    template = Template(files("pip_usage").joinpath("queries").joinpath(name).read_text(encoding="utf-8"))
    identifiers = set(template.get_identifiers())
    unexpected = set(parameters) - identifiers
    if unexpected:
        raise ValueError(f"Unexpected parameters for {name}: {', '.join(sorted(unexpected))}")
    return template.substitute({key: str(value) for key, value in parameters.items()})


def time_chunks(start: datetime, end: datetime, size: timedelta = timedelta(days=1)):
    """Split a time window into consecutive ranges, normally one day each."""
    current = start
    while current < end:
        chunk_end = min(current + size, end)
        yield current, chunk_end
        current = chunk_end


def prefetch_and_process(items, download, process, download_workers: int = RAW_QUERY_DOWNLOAD_WORKERS) -> None:
    """Download a few items concurrently while processing completed items one at a time.

    This overlaps network and CPU work without keeping the full downloaded dataset in memory.
    """
    iterator = iter(items)
    with ThreadPoolExecutor(max_workers=download_workers) as executor:
        pending = set()
        for _ in range(download_workers):
            try:
                pending.add(executor.submit(download, next(iterator)))
            except StopIteration:
                break
        while pending:
            completed, pending = wait(pending, return_when=FIRST_COMPLETED)
            for future in completed:
                try:
                    pending.add(executor.submit(download, next(iterator)))
                except StopIteration:
                    pass
                process(future.result())


def raw_query_batches(builds: pd.DataFrame, target_rows: int = RAW_QUERY_TARGET_ROWS, max_builds: int = RAW_QUERY_MAX_BUILDS):
    """Group builds into queries limited by both build count and estimated result rows."""
    batch: list[str] = []
    batch_rows = 0
    for row in builds[["BuildId", "PipRows"]].itertuples(index=False):
        pip_rows = max(1, int(row.PipRows))
        if batch and (len(batch) >= max_builds or batch_rows + pip_rows > target_rows):
            yield batch
            batch = []
            batch_rows = 0
        batch.append(str(row.BuildId))
        batch_rows += pip_rows
    if batch:
        yield batch


def format_duration(seconds: float) -> str:
    """Display seconds as a short value such as ``45s``, ``3m 10s``, or ``2h 5m``."""
    total_seconds = max(0, round(seconds))
    hours, remainder = divmod(total_seconds, 3600)
    minutes, seconds = divmod(remainder, 60)
    if hours:
        return f"{hours}h {minutes}m"
    if minutes:
        return f"{minutes}m {seconds}s"
    return f"{seconds}s"


def format_download_progress(
    completed_batches: int,
    total_batches: int,
    completed_builds: int,
    total_builds: int,
    completed_rows: int,
    total_rows: int,
    elapsed_seconds: float,
) -> str:
    """Build a progress message with completed work, remaining work, elapsed time, and ETA."""
    percent = completed_rows / total_rows * 100 if total_rows else 100.0
    remaining_rows = max(0, total_rows - completed_rows)
    eta_seconds = elapsed_seconds * remaining_rows / completed_rows if completed_rows else 0.0
    return (
        f"Progress: {completed_batches}/{total_batches} batches, {completed_builds:,}/{total_builds:,} builds, "
        f"~{completed_rows:,}/{total_rows:,} rows ({percent:.1f}%); remaining: "
        f"{total_batches - completed_batches} batches, {total_builds - completed_builds:,} builds, "
        f"~{remaining_rows:,} rows; elapsed {format_duration(elapsed_seconds)}, ETA {format_duration(eta_seconds)}."
    )


def sample_builds_across_time(builds: pd.DataFrame, limit: int) -> pd.DataFrame:
    """Select deterministic, evenly spaced builds from a chronologically ordered codebase.

    All builds are retained when the group is below the limit. A one-build limit selects the
    temporal midpoint; larger limits include both ends of the observed window.
    """
    ordered = builds.sort_values(["LastPipEvent", "BuildId"], kind="stable").reset_index(drop=True)
    if len(ordered) <= limit:
        return ordered
    if limit == 1:
        return ordered.iloc[[len(ordered) // 2]]
    positions = [index * (len(ordered) - 1) // (limit - 1) for index in range(limit)]
    return ordered.iloc[positions]


def merge_discovered_builds(build_parts: list[pd.DataFrame], max_builds: int, builds_per_codebase: int) -> pd.DataFrame:
    """Combine daily discovery results and sample the requested builds across time per codebase.

    Duplicate build rows are merged, missing metadata becomes ``unknown``, and an optional
    overall build limit is applied after the per-codebase limit.
    """
    if not build_parts or all(part.empty for part in build_parts):
        raise RuntimeError(
            "No DX5071 Pip Usage telemetry was found in the requested window; choose a more recent or wider window."
        )
    builds = pd.concat(build_parts, ignore_index=True)
    builds["FirstPipEvent"] = pd.to_datetime(builds["FirstPipEvent"], utc=True)
    builds["LastPipEvent"] = pd.to_datetime(builds["LastPipEvent"], utc=True)
    for column in ["Codebase", "StageId", "Queue", "Tenant"]:
        builds[column] = builds[column].fillna("unknown").astype(str).replace(["", "<missing>"], "unknown")
    builds = (
        builds.sort_values("LastPipEvent", ascending=False)
        .groupby("BuildId", as_index=False)
        .agg(
            PipRows=("PipRows", "sum"),
            FirstPipEvent=("FirstPipEvent", "min"),
            LastPipEvent=("LastPipEvent", "max"),
            Codebase=("Codebase", "first"),
            StageId=("StageId", "first"),
            Queue=("Queue", "first"),
            Tenant=("Tenant", "first"),
        )
        .sort_values("LastPipEvent", ascending=False)
    )
    builds = pd.concat(
        (sample_builds_across_time(group, builds_per_codebase) for _, group in builds.groupby("Codebase", sort=False, observed=True)),
        ignore_index=True,
    ).sort_values("LastPipEvent", ascending=False, kind="stable").reset_index(drop=True)
    return builds.head(max_builds) if max_builds else builds


def kusto_ids(build_ids: list[str]) -> str:
    """Escape build IDs for insertion into a Kusto list."""
    return ",".join(json.dumps(build_id) for build_id in build_ids)


def kusto_build_metadata(build_ids: list[str], codebases: dict[str, str], assignments: dict[str, str]) -> str:
    """Format each build's codebase and data split as rows for a Kusto query."""
    return ",".join(
        f"{json.dumps(build_id)},{json.dumps(codebases.get(build_id, 'unknown'))},{json.dumps(assignments[build_id])}"
        for build_id in build_ids
    )


def arguments() -> argparse.Namespace:
    """Read the download window, build limits, dataset name, and output path from the command line."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--window-start-utc", required=True)
    parser.add_argument("--window-end-utc", required=True)
    parser.add_argument("--max-builds", default=0, type=int, help="Test-only cap on completed builds; omit to download every build in the requested window.")
    parser.add_argument("--builds-per-codebase", default=DEFAULT_BUILDS_PER_CODEBASE, type=int, help="Maximum builds sampled across the requested window per codebase; smaller and unknown codebases are retained.")
    parser.add_argument("--dataset-name", required=True)
    parser.add_argument("--data-root", required=True, type=Path)
    parser.add_argument("--synthetic-fixture", action="store_true", help=argparse.SUPPRESS)
    return parser.parse_args()


def utc(value: str) -> datetime:
    """Parse a timestamp with a time-zone offset and convert it to UTC."""
    parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    if parsed.tzinfo is None:
        raise ValueError("UTC timestamps must include an offset.")
    return parsed.astimezone(timezone.utc)


def kusto_time(value: datetime) -> str:
    """Format a datetime for use inside a Kusto query."""
    return value.strftime("%Y-%m-%dT%H:%M:%SZ")


def query(client: KustoClient, props: ClientRequestProperties, database: str, text: str) -> pd.DataFrame:
    """Run a Kusto query and retry temporary network failures up to the configured attempt limit."""
    for attempt in range(1, KUSTO_QUERY_ATTEMPTS + 1):
        try:
            return dataframe_from_result_table(client.execute(database, text, properties=props).primary_results[0])
        except KustoNetworkError:
            if attempt == KUSTO_QUERY_ATTEMPTS:
                raise
            time.sleep(5 * attempt)


def write_csv_atomic(frame: pd.DataFrame, path: Path) -> None:
    """Write a CSV only after the complete temporary file succeeds.

    If writing fails, the previous destination remains unchanged and the partial file is removed.
    """
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.unlink(missing_ok=True)
    try:
        frame.to_csv(temporary, index=False)
        temporary.replace(path)
    except Exception:
        temporary.unlink(missing_ok=True)
        raise


def resolve_has_historic_perf_data(frame: pd.DataFrame) -> pd.Series:
    """Use explicit history availability when present, otherwise apply the legacy zero-prior rule."""
    legacy_has_history = ~(frame[PRIORS["cpu"]].eq(0) & frame[PRIORS["duration"]].eq(0))
    if HAS_HISTORIC_PERF_DATA not in frame:
        return legacy_has_history
    explicit = frame[HAS_HISTORIC_PERF_DATA].astype("string").str.lower().map({"true": True, "false": False})
    return explicit.fillna(legacy_has_history).astype(bool)


def parse_pip_description(description: object) -> tuple[str, str, str]:
    """Extract tool, module, and qualifier from a structured DX5071 pip description.

    Some pips, notably JavaScript pips, render only a free-form description after the pip hash.
    Those rows cannot supply fields matching the runtime feature contract and return ``unknown``
    instead of allowing later telemetry labels to shift into categorical columns.
    """
    if not isinstance(description, str):
        return "unknown", "unknown", "unknown"
    match = re.fullmatch(r"\s*([^,]+),\s*([^,]+),.*?,\s*(\{.*\})\s*", description)
    if match is None:
        return "unknown", "unknown", "unknown"
    return tuple(value.strip() or "unknown" for value in match.groups())


def prepare(raw: pd.DataFrame) -> pd.DataFrame:
    """Turn raw pip telemetry into valid model rows and derive additional features.

    Invalid targets, negative measurements, duplicate events, and rows without required IDs or
    timestamps are removed. Missing text features are replaced with ``unknown``.
    """
    pips = raw.copy()
    pips["PreciseTimeStamp"] = pd.to_datetime(pips["PreciseTimeStamp"], utc=True, errors="coerce", format="mixed")
    for column in [*TARGETS.values(), *PRIORS.values(), *NUMERIC]:
        pips[column] = pd.to_numeric(pips[column], errors="coerce")
    pips = pips.dropna(subset=["BuildId", "PreciseTimeStamp", "PipHash", *TARGETS.values()]).drop_duplicates(["BuildId", "PipHash", "PreciseTimeStamp"])
    pips = pips[(pips["ProcessorUseInPercents"] >= 0) & (pips["PeakWorkingSetMb"] >= 0) & (pips["AverageWorkingSetMb"] >= 0) & (pips["ActualDurationSec"] >= 0)].copy()
    pips[HAS_HISTORIC_PERF_DATA] = resolve_has_historic_perf_data(pips)
    if "PipDescription" in pips:
        parsed = pd.DataFrame(
            pips["PipDescription"].map(parse_pip_description).tolist(),
            columns=["Tool", "Module", "Qualifier"],
            index=pips.index,
        )
        pips[["Tool", "Module", "Qualifier"]] = parsed
    for column in ["Tool", "Module", "Qualifier", "Codebase", "StageId", "Queue", "Tenant"]:
        pips[column] = pips[column].fillna("unknown").astype(str).replace("", "unknown")
    pips["PipHash"] = pips["PipHash"].str.upper()
    module = pips["Module"].str.split(r"[._]")
    pips["ModuleFamily"] = module.str[0].fillna("unknown").replace("", "unknown")
    pips["ModuleSubgroup"] = module.str[1].fillna("unknown").replace("", "unknown")
    pips["ToolExtension"] = pips["Tool"].str.extract(r"(\.[A-Za-z0-9]+)(?:\s|\(|$)", expand=False).str.lower().fillna("unknown")
    pips["PipKind"] = pips["Qualifier"].str.extract(r"\|\|\s*(?:Syncronization|Synchronization)\s+Pip\s+For\s+\{\(([^|)]+)", expand=False).fillna("unknown")
    pips["Configuration"] = pips["Qualifier"].str.extract(r'configuration:"([^"}]+)"', expand=False).fillna("unknown")
    pips["Platform"] = pips["Qualifier"].str.extract(r'platform:"([^"}]+)"', expand=False).fillna("unknown")
    pips["TargetFramework"] = pips["Qualifier"].str.extract(r'targetFramework:"([^"}]+)"', expand=False).fillna("unknown")
    pips["TargetRuntime"] = pips["Qualifier"].str.extract(r'targetRuntime:"([^"}]+)"', expand=False).fillna("unknown")
    if "ExpectedPipUsageSource" not in pips:
        pips["ExpectedPipUsageSource"] = "Unknown"
    else:
        pips["ExpectedPipUsageSource"] = pips["ExpectedPipUsageSource"].fillna("Unknown").astype(str)
    return pips


def stage_raw_part(raw_path: Path, metadata: pd.DataFrame, assignments: dict[str, str], staging: Path) -> dict:
    """Clean one downloaded CSV and distribute its rows among stable on-disk chunks.

    Rows are grouped by a hash of codebase and pip hash so repeated pips consistently use the
    same chunk. The returned statistics record downloaded, retained, and unknown-feature counts.
    """
    part_key = raw_path.stem
    completion_marker = staging / f"{part_key}.done"
    statistics_path = staging / f"{part_key}.stats.json"
    if completion_marker.exists() and statistics_path.exists():
        return json.loads(statistics_path.read_text(encoding="utf-8"))
    raw = pd.read_csv(raw_path, low_memory=False).merge(metadata, on="BuildId", how="left")
    downloaded_rows = len(raw)
    pips = prepare(raw)
    pips["Split"] = pips["BuildId"].map(assignments)
    pips = pips.dropna(subset=["Split"])
    statistics = {
        "downloadedRows": downloaded_rows,
        "preparedRows": len(pips),
        "unknownFeatureCounts": {
            feature: int(pips[feature].eq("unknown").sum())
            for feature in CATEGORICAL
        },
    }
    keys = pips["Codebase"].astype(str) + "|" + pips["PipHash"].astype(str)
    pips["SamplePartition"] = pd.util.hash_pandas_object(keys, index=False).to_numpy() % SAMPLE_PARTITIONS
    for partition, group in pips.groupby("SamplePartition", sort=False):
        group.drop(columns="SamplePartition").to_pickle(staging / f"{int(partition):02d}-{part_key}.pkl")
    temporary_statistics_path = statistics_path.with_suffix(".json.tmp")
    temporary_statistics_path.write_text(json.dumps(statistics), encoding="utf-8")
    temporary_statistics_path.replace(statistics_path)
    completion_marker.touch()
    return statistics


def split_assignments(builds: pd.DataFrame) -> dict[str, str]:
    """Assign each build to training, validation, or testing within its codebase.

    Older builds train the model and the newest builds are reserved for validation and testing.
    Codebases with fewer than three builds use all available builds for training.
    """
    assignments: dict[str, str] = {}
    for _, group in builds.groupby("Codebase", sort=False):
        ordered = group.sort_values("LastPipEvent")["BuildId"].astype(str).tolist()
        test_count = max(1, round(len(ordered) * .1)) if len(ordered) >= 3 else 0
        validation_count = max(1, round(len(ordered) * .1)) if len(ordered) >= 3 else 0
        if test_count == 0:
            assignments.update({build: "train" for build in ordered})
            continue
        assignments.update({build: "test" for build in ordered[-test_count:]})
        assignments.update({build: "validation" for build in ordered[-(test_count + validation_count):-test_count]})
        assignments.update({build: "train" for build in ordered[:-(test_count + validation_count)]})
    return assignments


def prepare_and_split(data_root: Path, dataset_name: str) -> dict:
    """Prepare all downloaded files and assemble the final train, validation, and test chunks.

    Files are processed separately to limit RAM use. The function writes a split manifest and
    returns row counts, build counts, split sizes, and data-quality statistics.
    """
    cohort = data_root / "snapshots" / dataset_name
    metadata = pd.read_csv(cohort / "metadata.csv", low_memory=False)
    builds = pd.read_csv(cohort / "builds.csv", low_memory=False)
    builds["LastPipEvent"] = pd.to_datetime(builds["LastPipEvent"], utc=True)
    build_metadata = builds.merge(metadata, on="BuildId", how="left")
    build_metadata["Codebase"] = build_metadata["Codebase"].fillna("unknown")
    assignments = split_assignments(build_metadata)
    split_ids = {name: sorted(build for build, split in assignments.items() if split == name) for name in ("train", "validation", "test")}
    split = {"dataset": dataset_name, "method": "chronological 80/10/10 within each codebase", "build_ids": split_ids, "build_counts": {name: len(ids) for name, ids in split_ids.items()}}
    split["sha256"] = hashlib.sha256(json.dumps(split_ids, sort_keys=True).encode()).hexdigest()
    staging = cohort / "sample_staging"
    prepared_parts = cohort / "prepared_parts"
    staging.mkdir(exist_ok=True)
    prepared_parts.mkdir(exist_ok=True)
    part_statistics = [
        stage_raw_part(raw_path, metadata, assignments, staging)
        for raw_path in sorted((cohort / "raw_parts").glob("*.csv"))
    ]
    discovered_rows = int(builds["PipRows"].sum())
    downloaded_rows = sum(item["downloadedRows"] for item in part_statistics)
    staged_rows = sum(item["preparedRows"] for item in part_statistics)
    unknown_feature_counts = {
        feature: sum(item["unknownFeatureCounts"][feature] for item in part_statistics)
        for feature in CATEGORICAL
    }
    statistics = {
        "discoveredRows": discovered_rows,
        "downloadedRows": downloaded_rows,
        "preparedRows": staged_rows,
        "parserYield": downloaded_rows / discovered_rows if discovered_rows else 0.0,
        "preparationYield": staged_rows / downloaded_rows if downloaded_rows else 0.0,
        "unknownFeatureCounts": unknown_feature_counts,
        "unknownFeatureRates": {
            feature: count / staged_rows if staged_rows else 0.0
            for feature, count in unknown_feature_counts.items()
        },
    }
    split["statistics"] = statistics
    (cohort / "split_manifest.json").write_text(json.dumps(split, indent=2), encoding="utf-8")
    print("Pip Usage dataset statistics: " + json.dumps(statistics, sort_keys=True), flush=True)
    if statistics["parserYield"] < 0.9:
        print(
            f"WARNING: only {statistics['parserYield']:.1%} of discovered DX5071 rows survived Kusto parsing; verify the DX5071 log format.",
            flush=True,
        )

    prepared_rows = 0
    prepared_builds: set[str] = set()
    for partition in range(SAMPLE_PARTITIONS):
        paths = sorted(staging.glob(f"{partition:02d}-*.pkl"))
        if not paths:
            continue
        prepared = pd.concat((pd.read_pickle(path) for path in paths), ignore_index=True)
        prepared.to_pickle(prepared_parts / f"{partition:02d}.pkl")
        prepared_rows += len(prepared)
        prepared_builds.update(prepared["BuildId"].astype(str).unique())
    shutil.rmtree(staging)

    return {
        "dataset": dataset_name,
        "preparedRows": prepared_rows,
        "builds": len(prepared_builds),
        "split": split["build_counts"],
        "statistics": statistics,
    }


def write_synthetic_cohort(data_root: Path, dataset_name: str) -> None:
    """Create a small repeatable dataset that tests the real preparation and training workflow."""
    cohort = data_root / "snapshots" / dataset_name
    raw_parts = cohort / "raw_parts"
    raw_parts.mkdir(parents=True, exist_ok=True)
    start = datetime(2026, 1, 1, tzinfo=timezone.utc)
    builds = []
    metadata = []
    rows = []
    for build_index in range(30):
        build_id = f"synthetic-build-{build_index:02d}"
        timestamp = start + timedelta(hours=build_index)
        builds.append({"BuildId": build_id, "PipRows": 12, "LastPipEvent": timestamp.isoformat()})
        metadata.append({"BuildId": build_id, "Codebase": "Synthetic", "StageId": "Build", "Queue": "Test", "Tenant": "Test"})
        for pip_index in range(12):
            cold = pip_index % 2 == 0
            scale = build_index + pip_index + 1
            rows.append({
                "BuildId": build_id,
                "PreciseTimeStamp": (timestamp + timedelta(seconds=pip_index)).isoformat(),
                "PipHash": f"{pip_index + 1:016X}",
                "Tool": "synthetic.exe",
                "Module": f"Synthetic.Module{pip_index % 3}",
                "Qualifier": 'configuration:"debug", platform:"x64", targetFramework:"net8.0", targetRuntime:"win-x64"',
                "ExpectedDurationSec": 0.0 if cold else float(scale),
                "ActualDurationSec": float(scale + 1),
                "ExpectedProcessorUseInPercents": 0 if cold else 50 + scale,
                "ProcessorUseInPercents": 60 + scale,
                "Weight": 1 + pip_index % 3,
                "ExpectedPeakWorkingSetMb": 0 if cold else 100 + scale,
                "PeakWorkingSetMb": 120 + scale,
                "ExpectedAverageWorkingSetMb": 0 if cold else 70 + scale,
                "AverageWorkingSetMb": 80 + scale,
                "HasHistoricPerfData": not cold,
                "ExpectedPipUsageSource": "None" if cold else "Historical",
                "NumFileDependencies": 2 + pip_index,
                "NumDirectoryDependencies": pip_index % 3,
                "NumFileOutputs": 1 + pip_index % 2,
                "NumDirectoryOutputs": pip_index % 2,
            })
    pd.DataFrame(builds).to_csv(cohort / "builds.csv", index=False)
    pd.DataFrame(metadata).to_csv(cohort / "metadata.csv", index=False)
    pd.DataFrame(rows).to_csv(raw_parts / "synthetic.csv", index=False)


def download_raw_cohort(
    data_root: Path,
    dataset_name: str,
    window_start_utc: datetime,
    window_end_utc: datetime,
    max_builds: int,
    builds_per_codebase: int = DEFAULT_BUILDS_PER_CODEBASE,
) -> None:
    """Find eligible builds and download their pip telemetry into reusable CSV files.

    Builds are sampled deterministically across the requested window independently per codebase. Large results are split
    into bounded queries, completed files are reused on restart, and each downloaded file is
    prepared immediately so the full cohort is never held in RAM.
    """
    start = window_start_utc.astimezone(timezone.utc)
    end = window_end_utc.astimezone(timezone.utc)
    if max_builds < 0 or builds_per_codebase < 1 or end <= start or Path(dataset_name).name != dataset_name:
        raise ValueError("Use non-negative/global and positive/per-codebase build limits, an increasing UTC range, and a simple dataset folder name.")
    cohort = data_root / "snapshots" / dataset_name
    parts = cohort / "raw_parts"
    parts.mkdir(parents=True, exist_ok=True)
    credential = AzureCliCredential(); credential.get_token("https://kusto.kusto.windows.net/.default")
    client = KustoClient(KustoConnectionStringBuilder.with_azure_token_credential("https://cbuild.kusto.windows.net", credential))
    props = ClientRequestProperties(); props.set_option(ClientRequestProperties.request_timeout_option_name, KUSTO_QUERY_TIMEOUT)
    st, et = kusto_time(start), kusto_time(end)
    build_parts = []
    discovery_chunks = list(time_chunks(start, end))
    for chunk_index, (chunk_start, chunk_end) in enumerate(discovery_chunks, start=1):
        print(f"Discovering builds {chunk_index}/{len(discovery_chunks)}: {chunk_start.isoformat()} through {chunk_end.isoformat()}", flush=True)
        chunk_st, chunk_et = kusto_time(chunk_start), kusto_time(chunk_end)
        discovery_query = render_query(
            "discover_builds.kql",
            chunk_start=chunk_st,
            chunk_end=chunk_et,
            window_start=st,
            window_end=et,
            builds_per_codebase=builds_per_codebase,
        )
        build_parts.append(query(client, props, "CloudBuildProd", discovery_query))
    builds = merge_discovered_builds(build_parts, max_builds, builds_per_codebase)
    print(f"Discovered {len(builds)} selected builds across {builds['Codebase'].nunique()} codebases.", flush=True)
    if len(builds) < 3: raise RuntimeError(f"Only {len(builds)} complete Pip Usage builds were found; widen the range.")
    build_ids = builds.BuildId.astype(str).tolist()
    metadata = builds[["BuildId", "Codebase", "StageId", "Queue", "Tenant"]].copy()
    metadata.to_csv(cohort / "metadata.csv", index=False)
    builds[["BuildId", "PipRows", "FirstPipEvent", "LastPipEvent"]].to_csv(cohort / "builds.csv", index=False)
    codebases = metadata.set_index("BuildId")["Codebase"].astype(str).to_dict()
    build_metadata = builds[["BuildId", "LastPipEvent", "Codebase"]].copy()
    build_metadata["LastPipEvent"] = pd.to_datetime(build_metadata["LastPipEvent"], utc=True)
    assignments = split_assignments(build_metadata)
    staging = cohort / "sample_staging"
    staging.mkdir(exist_ok=True)

    raw_batches = list(raw_query_batches(builds))
    total_batches = len(raw_batches)
    total_builds = sum(len(batch) for batch in raw_batches)
    total_rows = int(builds["PipRows"].sum())
    download_started = time.perf_counter()
    progress_lock = Lock()
    progress = {"batches": 0, "builds": 0, "rows": 0}

    def download_part(batch_item: tuple[int, list[str]]) -> Path:
        """Download one build batch, or reuse its completed CSV, and report overall progress."""
        batch_index, build_batch = batch_item
        ids = kusto_ids(build_batch)
        batch_metadata = kusto_build_metadata(build_batch, codebases, assignments)
        batch_key = hashlib.sha256("\n".join(build_batch).encode()).hexdigest()
        selected_builds = builds.loc[builds["BuildId"].astype(str).isin(build_batch)]
        estimated_rows = int(selected_builds["PipRows"].sum())
        part = parts / f"{batch_key}.csv"
        if part.exists():
            print(f"Reusing raw batch {batch_index}/{total_batches} {batch_key[:8]} ({len(build_batch)} builds, ~{estimated_rows:,} rows).", flush=True)
        else:
            batch_start = kusto_time(selected_builds["FirstPipEvent"].min())
            batch_end = kusto_time(selected_builds["LastPipEvent"].max())
            print(f"Downloading raw batch {batch_index}/{total_batches} {batch_key[:8]} ({len(build_batch)} builds, ~{estimated_rows:,} rows, {batch_start} through {batch_end}).", flush=True)
            started = time.perf_counter()
            text = render_query(
                "download_pip_usage.kql",
                ids=ids,
                build_metadata=batch_metadata,
                batch_start=batch_start,
                batch_end=batch_end,
            )
            batch = query(client, props, "CloudBuildProd", text)
            write_csv_atomic(batch, part)
            print(f"Downloaded raw batch {batch_index}/{total_batches} {batch_key[:8]} ({len(batch):,} rows) in {time.perf_counter() - started:.1f}s.", flush=True)
        with progress_lock:
            progress["batches"] += 1
            progress["builds"] += len(build_batch)
            progress["rows"] += estimated_rows
            print(format_download_progress(
                progress["batches"], total_batches,
                progress["builds"], total_builds,
                progress["rows"], total_rows,
                time.perf_counter() - download_started,
            ), flush=True)
        return part

    print(f"Downloading {total_batches} raw batches ({total_builds:,} builds, ~{total_rows:,} rows) with {RAW_QUERY_DOWNLOAD_WORKERS} concurrent requests.", flush=True)
    prefetch_and_process(
        list(enumerate(raw_batches, start=1)),
        download_part,
        lambda raw_path: stage_raw_part(raw_path, metadata, assignments, staging),
    )


def main() -> None:
    """Create the requested raw dataset, prepare it, split it, and print summary statistics."""
    args = arguments()
    if args.synthetic_fixture:
        write_synthetic_cohort(args.data_root, args.dataset_name)
    else:
        download_raw_cohort(
            args.data_root,
            args.dataset_name,
            utc(args.window_start_utc),
            utc(args.window_end_utc),
            args.max_builds,
            args.builds_per_codebase,
        )
    print(json.dumps(prepare_and_split(args.data_root, args.dataset_name), indent=2))

if __name__ == "__main__": main()
