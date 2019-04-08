// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;

namespace Tool.ExecutionLogSdk
{
    /// <summary>
    /// Enumeration used to specify what to load from execution logs.
    /// Multiple of these enum values can be OR-ed into a single uint value and passed to LoadExecutionLog.
    /// </summary>
    [SuppressMessage("Microsoft.Usage", "CA2217:DoNotMarkEnumsWithFlags")]
    [SuppressMessage("Microsoft.Design", "CA1008:EnumsShouldHaveZeroValue")]
    [Flags]
    public enum ExecutionLogLoadOptions : uint
    {
        /// <summary>
        /// Default value, same as LoadPipDataOnly.
        /// </summary>
        None = 0,

        /// <summary>
        /// Load pip data only (this is the minimum amount of data that can be loaded).
        /// </summary>
        LoadPipDataOnly = None,

        /// <summary>
        /// Load the build graph.
        /// </summary>
        LoadBuildGraph = 0x1,

        /// <summary>
        /// Load PipExecutionPerformanceData events.
        /// </summary>
        LoadPipExecutionPerformanceData = 0x2,

        /// <summary>
        /// Load FileArtifactContentDecided events.
        /// </summary>
        LoadFileHashValues = 0x4,

        /// <summary>
        /// Load ObservedInputs events.
        /// </summary>
        LoadObservedInputs = 0x8,

        /// <summary>
        /// Load ReportedProcesses data from ProcessExecutionMonitoringReported events.
        /// </summary>
        LoadProcessMonitoringData = 0x10,

        /// <summary>
        /// Load DirectoryMembershipHashed events.
        /// </summary>
        LoadDirectoryMemberships = 0x20,

        /// <summary>
        /// When NOT specified, files that are not referenced by any pips during the build will be skipped while processing DirectoryMembershipHashed events.
        /// </summary>
        LoadDirectoryMembershipsForUnusedFiles = LoadDirectoryMemberships | 0x40,

        /// <summary>
        /// Load ReportedFileAccesses data from ProcessExecutionMonitoringReported events.
        /// </summary>
        LoadReportedFileAccesses = LoadProcessMonitoringData | 0x80,

        /// <summary>
        /// Only load data for pips that ran during the build.
        /// </summary>
        LoadExecutedPipsOnly = LoadPipExecutionPerformanceData | 0x100,

        /// <summary>
        /// Do not load some pip properties that are rarely used.
        /// </summary>
        DoNotLoadRarelyUsedPipProperties = 0x200,

        /// <summary>
        /// Do not load source files.
        /// </summary>
        DoNotLoadSourceFiles = 0x400,

        /// <summary>
        /// Do not load output files.
        /// </summary>
        DoNotLoadOutputFiles = 0x800,

        /// <summary>
        /// Load process fingerprint computations events.
        /// </summary>
        LoadProcessFingerprintComputations = 0x1000,

        // The following values are combinations of the above values and they serve as shortcuts for commonly used load options.
        // These combo values can still be combine with basic values from above by setting or clearing individual bits:
        // i.e LoadPipDataBuildGraphAndFileHashes|LoadObservedInputs - load pip data, build graph, file hashes and observed inputs
        // i.e LoadEverything^LoadObservedInputs or LoadEverything&(~LoadObservedInputs) - everything but observed inputs

        /// <summary>
        /// Load pip data and build graph.
        /// </summary>
        LoadPipDataAndBuildGraph = LoadPipDataOnly | LoadBuildGraph,

        /// <summary>
        /// Load pip data, build graph and pip execution performance data.
        /// </summary>
        LoadPipDataBuildGraphAndPipPerformanceData = LoadPipDataAndBuildGraph | LoadPipExecutionPerformanceData,

        /// <summary>
        /// Load pip data, build graph and file hashes.
        /// </summary>
        LoadPipDataBuildGraphAndFileHashes = LoadPipDataAndBuildGraph | LoadFileHashValues,

        /// <summary>
        /// Load pip data, build graph and observed file inputs.
        /// </summary>
        LoadPipDataBuildGraphAndObservedFileAccesses = LoadPipDataAndBuildGraph | LoadObservedInputs,

        /// <summary>
        /// Load everything
        /// </summary>
        LoadEverything = LoadPipDataAndBuildGraph | LoadPipExecutionPerformanceData | LoadProcessMonitoringData |
                         LoadFileHashValues | LoadObservedInputs | LoadDirectoryMemberships |
                         LoadDirectoryMembershipsForUnusedFiles | LoadReportedFileAccesses | LoadProcessFingerprintComputations,

        /// <summary>
        /// Load everything but directory memberships.
        /// </summary>
        LoadEverythingButDirectoryMemberships = LoadPipDataAndBuildGraph | LoadPipExecutionPerformanceData | LoadProcessMonitoringData |
                                                LoadFileHashValues | LoadObservedInputs | LoadReportedFileAccesses | LoadProcessFingerprintComputations,

        /// <summary>
        /// Load everything but directory memberships and reported file accesses.
        /// </summary>
        LoadEverythingButDirectoryMembershipsAndReportedFileAccesses = LoadPipDataAndBuildGraph | LoadPipExecutionPerformanceData | LoadProcessMonitoringData |
                                                LoadFileHashValues | LoadObservedInputs | LoadProcessFingerprintComputations,
    }
}
