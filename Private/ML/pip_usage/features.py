"""Feature and target schema for the BuildXL Pip Usage model."""

from __future__ import annotations


# Shared Pip Usage model schema. Data preparation owns construction of these columns; training and export consume
# this contract without repeating feature names.
PIP_USAGE_TARGETS = {
    "cpu": "ProcessorUseInPercents",
    "memory": "PeakWorkingSetMb",
    "average_memory": "AverageWorkingSetMb",
    "duration": "ActualDurationSec",
}
PIP_USAGE_PRIORS = {
    "cpu": "ExpectedProcessorUseInPercents",
    "memory": "ExpectedPeakWorkingSetMb",
    "average_memory": "ExpectedAverageWorkingSetMb",
    "duration": "ExpectedDurationSec",
}
PIP_USAGE_HAS_HISTORIC_PERF_DATA = "HasHistoricPerfData"
PIP_USAGE_CATEGORICAL_FEATURES = [
    "Tool", "ToolExtension", "ModuleFamily", "ModuleSubgroup", "PipKind", "Configuration", "Platform",
    "TargetFramework", "TargetRuntime", "Codebase", "StageId", "Queue", "Tenant",
]
PIP_USAGE_NUMERIC_FEATURES = [
    "Weight", "NumFileDependencies", "NumDirectoryDependencies", "NumFileOutputs", "NumDirectoryOutputs",
]
PIP_USAGE_FEATURES = [
    *PIP_USAGE_CATEGORICAL_FEATURES,
    *PIP_USAGE_NUMERIC_FEATURES,
    *PIP_USAGE_PRIORS.values(),
]
