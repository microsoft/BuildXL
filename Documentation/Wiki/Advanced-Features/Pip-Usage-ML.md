# Pip Usage ML

Pip Usage ML supplies CPU, memory, and duration estimates for process pips. The scheduler uses the CPU and memory estimates for CPU weight, CPU throttling, and RAM projection. `Cold` predicts cold pips without requiring historical data; `ColdAndWarm` predicts cold and warm pips and lets the prediction override historical values; `HistoricDataUnavailable` predicts all eligible pips only when the configured historical performance table cannot be loaded.

## Runtime scope

The model is evaluated only when all of the following are true:

- The pip is a process pip, not an IPC pip.
- The executable path is valid.
- The selected mode enables the pip: `Cold` requires a cold pip, `ColdAndWarm` permits all eligible pips, and `HistoricDataUnavailable` permits all eligible pips only when the historical performance table cannot be loaded.
- The Microsoft-internal scheduler assembly contains a valid embedded model payload.

Missing resources or an incompatible manifest set the runtime mode to `Disabled` for the remainder of the build. An evaluation exception, malformed pip metadata, or a rejected non-finite prediction falls back to existing historical/default resource behavior for that pip. None of these cases fail the build.

## Configuration

Use `/pipUsageMLMode:<Disabled|Cold|ColdAndWarm|HistoricDataUnavailable>` to select a mode. Names are case-insensitive. The default is `HistoricDataUnavailable`.

| Mode | Current behavior |
| --- | --- |
| `Disabled` | Never evaluate the model. |
| `Cold` | Evaluate cold process pips. |
| `ColdAndWarm` | Evaluate cold and warm process pips; warm predictions use historical resource values as model inputs. |
| `HistoricDataUnavailable` | Evaluate all eligible process pips only when historical performance information is enabled but its table cannot be loaded. This is the default. |

The command-line option is the only control. When omitted, the mode is `HistoricDataUnavailable`. Invalid command-line values produce an argument error. The former `BuildXLUseMLForPipUsage` environment variable is no longer read.

## Predictions

The runtime evaluates four model outputs:

- CPU usage in percent, used to derive historic CPU weight and CPU semaphore demand.
- Peak working set in MB, used for RAM projection.
- Average working set in MB, used when less-aggressive memory projection is enabled.
- Duration in seconds, used for critical-path priority whenever the selected mode evaluates the process pip.

Predictions are cached once per pip for the build. All consumers therefore observe the same prediction.

## End-to-end architecture

```text
TRAINING PIPELINE
=================

.azdo/ml/pip-usage-model.yml
  |
  +-- UsePythonVersion + pip install
  |     Inputs: Private/ML/pyproject.toml, constraints.txt
  |
  +-- Invoke-Pester
  |     Private/ML/scripts/Invoke-PipUsageTraining.Tests.ps1
  |     Tests the wrapper, synthetic end-to-end training, download helpers,
  |     artifact contract, and pipeline policy.
  |
  +-- AzureCLI (identity: BUILDXL-ML-Read-Kusto)
	  |
	  +-- Private/ML/scripts/Invoke-PipUsageTraining.ps1
		  |
		  +-- python -m pip_usage.dataset
		  |     Code: dataset.py + features.py
		  |     Reads: cbuild/CloudBuildProd.DominoMessage (DX5071)
		  |            cbuild/Domino.dominoinvocation
		  |     Creates under $(Agent.TempDirectory):
		  |       raw_parts/*.csv
		  |       sample_staging/*.pkl        (temporary)
		  |       prepared_parts/*.pkl
		  |       metadata.csv, builds.csv, split_manifest.json
		  |     Reports:
		  |       discovered/downloaded/prepared row counts
		  |       parser and preparation yield
		  |       categorical unknown counts and rates
		  |
		  +-- python -m pip_usage.training
		  |     Code: training.py + features.py + export.py
		  |     Reads: prepared_parts/*.pkl + split_manifest.json
		  |     Trains and validates four LightGBM targets:
		  |       cpu, memory, average_memory, duration
		  |     Creates $(modelDir):
		  |       model_spec.json
		  |       pip_usage_cpu.json
		  |       pip_usage_memory.json
		  |       pip_usage_average_memory.json
		  |       pip_usage_duration.json
		  |       test_cases.json
		  |     Creates quality_report.json with dataset statistics,
		  |     tuning details, test metrics, quality gate, and hashes.
		  |
		  +-- nuget pack using
			  Private/ML/model-package/BuildXL.ML.Models.nuspec
			  |
			  +-- BuildXL.ML.Models.<version>.nupkg (JSON only)

PIPELINE OUTPUTS
================

$(Build.ArtifactStagingDirectory)
  +-- model-export/                         -- pipeline artifact copy
  |     model_spec.json
  |     pip_usage_*.json
  |     test_cases.json
  +-- quality_report.json
  |
  +-- published as pipeline artifact: pip-usage-training

Private/ML/artifacts/BuildXL.ML.Models.<version>.nupkg
  |
  +-- 1ES.PublishNuget@1
	  |
	  +-- BuildXL.Selfhost internal NuGet feed

Prepared telemetry remains under $(Agent.TempDirectory). It is not uploaded.

BUILDXL BUILD-TIME INTEGRATION
==============================

config.microsoftInternal.dsc  ----- pins package version -----+
cg/nuget/cgmanifest.json      ----- governance metadata ------+
										  |
BuildXL.Selfhost feed --> BuildXL.ML.Models package -----------+
										  |
Public/Src/Engine/Scheduler/BuildXL.Scheduler.dsc
  |
  +-- embeds model_spec.json and the four pip_usage_*.json files
	into the Microsoft-internal BuildXL.Scheduler.dll

Public/Src/Engine/UnitTests/Scheduler/Test.BuildXL.Scheduler.dsc
  |
  +-- additionally embeds test_cases.json for Python/C# parity tests

RUNTIME EVALUATION AND CONSUMERS
================================

Schedule.PipUsageMLMode
	Disabled | Cold | ColdAndWarm | HistoricDataUnavailable (default)
  |
  +-- PipUsageModel.ShouldEvaluate(mode, historical data, table availability)
	  |
	  +-- Scheduler / DynamicScheduler.GetMLPipUsagePrediction(pipId, historicPerfData)
		  |
		  +-- Lazy<PipUsageModel>
		  |     PipUsageModelSpec   -- parses/validates model_spec.json
		  |     LightGbmModel       -- parses/evaluates LightGBM JSON trees
		  |     PipUsageModel       -- extracts and encodes pip features,
		  |                            evaluates all four models
		  |
		  +-- ConcurrentDictionary<PipId, Lazy<PipUsagePrediction?>>
			  caches one shared prediction per pip
			  |
			  +-- CpuPercent
			  |     +-- Scheduler.ComputeHistoricCpuWeight
			  |     +-- Worker CPU semaphore demand
			  |
			  +-- PeakMemoryMb
			  |     +-- Worker RAM projection
			  |
			  +-- AverageMemoryMb
			  |     +-- Worker less-aggressive RAM projection
			  |
			  +-- DurationSec
				  +-- Scheduler critical-path priority estimate

`Cold` evaluates pips without usable historical CPU and duration data.
`ColdAndWarm` evaluates all eligible pips. `HistoricDataUnavailable`
evaluates all eligible pips only when historical performance information is
enabled but its table cannot be loaded.

If loading or evaluation fails, BuildXL counts the failure and falls
back to historical data or existing defaults without failing the build.
```

## Model package

`BuildXL.ML.Models` is an independently versioned, Microsoft-internal NuGet package containing JSON only. Internal scheduler builds embed these resources into `BuildXL.Scheduler.dll`:

- `model_spec.json`
- `pip_usage_cpu.json`
- `pip_usage_memory.json`
- `pip_usage_average_memory.json`
- `pip_usage_duration.json`

Public builds compile the dependency-free evaluator but do not embed the internal model package.

To update the model consumed by BuildXL after validating a training run:

1. Publish a new immutable `BuildXL.ML.Models` version.
2. Update the package version in `config.microsoftInternal.dsc`.
3. Update the same version in `cg/nuget/cgmanifest.json`.
4. Build the scheduler and run `PipUsageModelTests`.
5. Verify the package contains the expected JSON resources before enabling it in production builds.

Do not add Python or LightGBM native binaries to the model package. The scheduler uses the dependency-free evaluator under `Public/Src/ML/Runtime`.

## Telemetry

The scheduler exposes these counters:

- `PipUsageMLPredictionCount`: successful process-pip predictions.
- `PipUsageMLPredictionFailureCount`: evaluations that fell back to normal estimates.
- `PipUsageMLPredictionDuration`: time spent evaluating the model.
- `HistoricalCriticalPath.NumMlPredictions`: critical-path duration estimates supplied by ML.

A low prediction count can mean model resources failed to load, individual evaluations were rejected, or pips are ineligible for the selected mode. Dataset parser/preparation yield and categorical unknown rates are stored in `quality_report.json` and printed during training.

DX5071 reports the selected pre-run estimates and their source in `ExpectedPipUsageSource`:

- `ML`: expected CPU, peak/average memory, and duration come from the cached Pip Usage prediction.
- `Historical`: expected values come from historic pip performance.
- `None`: neither an ML prediction nor historic performance was available, so normal scheduler defaults apply where needed.

`ExpectedProcessorUseInPercents` and `ExpectedDurationSec` contain the selected ML values when the source is `ML`. `ExpectedPeakWorkingSetMb` and `ExpectedAverageWorkingSetMb` contain the selected memory expectations and may later be increased after a low-memory retry. `EwrExpectedProcessSlots` uses the same ML-aware CPU weight used by early worker release.

`HasHistoricPerfData` remains separate from the source. The trainer uses it to preserve cold/warm classification after ML estimates begin appearing in the `Expected*` fields. Older DX5071 messages without this parsed field fall back to the previous zero-CPU-and-duration classification rule.

## Training and compliance

The governed training pipeline is `.azdo/ml/pip-usage-model.yml`. Download, preparation, training, and assembly remain in one job so prepared telemetry stays on the assigned training machine and is not uploaded as a pipeline artifact.

Training dependencies execute only on training agents and are not redistributed in the JSON model package. Their pinned Python 3.11 versions and licenses are documented in `Private/ML/DEPENDENCIES.md`; Component Governance and CELA guidance remain authoritative for approval and attribution requirements.
