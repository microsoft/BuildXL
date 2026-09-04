# Pip Usage Model Training

The Pip Usage training pipeline trains and validates the models, packages them as `BuildXL.ML.Models`, and optionally publishes the NuGet package consumed by the BuildXL scheduler.

The pipeline runs every Saturday at 00:00 UTC. Scheduled runs leave the `trainingEndDate` placeholder as `2026-MM-DD` to train on the preceding seven complete UTC days, use 10 tuning trials per target, retain up to 500 time-distributed builds per codebase, and publish the validated package. Manual runs can replace the placeholder with a UTC date in `YYYY-MM-DD` format for backfills.

See [Pip Usage ML](../../Documentation/Wiki/Advanced-Features/Pip-Usage-ML.md) for the scheduler runtime contract, enablement modes, telemetry, and package update procedure.

## Layout

- `pip_usage/` - Kusto retrieval, deterministic per-pip sampling, partitioned LightGBM training, and model-data export.
- `pip_usage/queries/` - Standalone discovery and download KQL templates; `dataset.render_query` substitutes their `$parameter` values at runtime.
- `model-package/` - NuGet specification for `BuildXL.ML.Models`, which contains only exported model JSON.
- `scripts/Invoke-PipUsageTraining.ps1` - Canonical full training, validation, export, and package entry point.
- `constraints.txt` and `DEPENDENCIES.md` - Pinned Python 3.11 closure and license inventory.
- `../../Public/Src/ML/` - C# Pip Usage model evaluator built as part of BuildXL; it consumes the exported model JSON without requiring Python, LightGBM, or another ML runtime.
- `../../.azdo/ml/pip-usage-model.yml` - Manual training, bounded validation, and optional feed publication.

## Production Workflow

The production pipeline:

1. Authenticates pip to the internal `CloudBuild-Repo` Azure Artifacts feed before restoring dependencies, avoiding direct public PyPI access.
2. Uses a retryable Azure CLI task with scoped Kusto identity to select and prepare completed builds in the requested UTC window.
3. Retains a deterministic, time-distributed sample of builds across the requested window independently for each codebase.
4. Encodes shared disk-backed training, validation, and test arrays on the training agent.
5. Uses a following PowerShell task on the same agent to tune each target with seeded Gaussian-process Bayesian optimization, then trains CPU, peak-memory, average-memory, and duration models sequentially without uploading prepared telemetry.
6. Verifies target model hashes and dataset identity locally.
7. Applies held-out warm, warm-masked, and cold quality gates.
8. Assembles sanitized model JSON and parity fixtures into one JSON-only `BuildXL.ML.Models` package.
9. Publishes the validated package when requested.

## Bounded Validation

Queue the general pipeline with `buildsPerCodebase: 5` and `tuningTrials: 1` for a short end-to-end validation through the production path. Set `publish: false` when the run should validate without publishing its package.

## Local Validation

From this directory:

```powershell
python -m pip install -c constraints.txt -e .
python -m unittest discover -s tests -p 'test_*.py' -v
Invoke-Pester scripts/Invoke-PipUsageTraining.Tests.ps1
```