// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Scheduler;
using BuildXL.Storage;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tracing;
using BuildXL.Scheduler.Fingerprints;

namespace Test.BuildXL.Scheduler.Utils
{
    /// <summary>
    /// Class for disabled file monitoring violation analyzer.
    /// </summary>
    /// <remarks>
    /// This class should only be used for testing pip execution that does not need file monitoring violation analysis.
    /// For example, if a pip does not have dynamic properties like (shared) opaque output directories, then file monitoring
    /// violation analysis is not needed.
    /// </remarks>
    internal class DisabledFileMonitoringViolationAnalyzer : IFileMonitoringViolationAnalyzer
    {
        private readonly CounterCollection<FileMonitoringViolationAnalysisCounter> m_counters = new CounterCollection<FileMonitoringViolationAnalysisCounter>();

        /// <inheritdoc />
        public CounterCollection<FileMonitoringViolationAnalysisCounter> Counters => m_counters;

        /// <inheritdoc />
        public bool AnalyzeDynamicViolations(
            Process pip,
            [AllowNull] IReadOnlyCollection<(DirectoryArtifact, ReadOnlyArray<FileArtifactWithAttributes>)> exclusiveOpaqueDirectoryContent,
            [AllowNull] IReadOnlyDictionary<AbsolutePath, IReadOnlyCollection<FileArtifactWithAttributes>> sharedOpaqueDirectoryWriteAccesses,
            [AllowNull] IReadOnlyDictionary<AbsolutePath, ObservedInputType> allowedUndeclaredReads,
            [AllowNull] IReadOnlyCollection<(AbsolutePath Path, DynamicObservationKind Kind)> dynamicObservations,
            ReadOnlyArray<(FileArtifact fileArtifact, FileMaterializationInfo fileInfo, PipOutputOrigin pipOutputOrigin)> outputContent) => true;

        /// <inheritdoc />
        public AnalyzePipViolationsResult AnalyzePipViolations(
            Process pip,
            [AllowNull] IReadOnlyCollection<ReportedFileAccess> violations,
            [AllowNull] IReadOnlyCollection<ReportedFileAccess> allowlistedAccesses,
            [AllowNull] IReadOnlyCollection<(DirectoryArtifact, ReadOnlyArray<FileArtifactWithAttributes>)> exclusiveOpaqueDirectoryContent,
            [AllowNull] IReadOnlyDictionary<AbsolutePath, IReadOnlyCollection<FileArtifactWithAttributes>> sharedOpaqueDirectoryWriteAccesses,
            [AllowNull] IReadOnlyDictionary<AbsolutePath, ObservedInputType> allowedUndeclaredReads,
            [AllowNull] IReadOnlyCollection<(AbsolutePath Path, DynamicObservationKind Kind)> dynamicObservations,
            ReadOnlyArray<(FileArtifact fileArtifact, FileMaterializationInfo fileInfo, PipOutputOrigin pipOutputOrigin)> outputsContent,
            IReadOnlyDictionary<AbsolutePath, RequestedAccess> fileAccessesBeforeFirstUndeclaredRead,
            out IReadOnlyDictionary<FileArtifact, (FileMaterializationInfo, ReportedViolation)> allowedSameContentViolations)
        {
            allowedSameContentViolations = CollectionUtilities.EmptyDictionary<FileArtifact, (FileMaterializationInfo, ReportedViolation)>();
            return AnalyzePipViolationsResult.NoViolations;
        }

        /// <inheritdoc />
        public AnalyzePipViolationsResult AnalyzeSameContentViolationsOnCacheConvergence(
            Process pip,
            ReadOnlyArray<(FileArtifact fileArtifact, FileMaterializationInfo fileInfo, PipOutputOrigin pipOutputOrigin)> convergedContent,
            IReadOnlyDictionary<FileArtifact, (FileMaterializationInfo fileMaterializationInfo, ReportedViolation reportedViolation)> allowedDoubleWriteViolations)
        => AnalyzePipViolationsResult.NoViolations;
    }
}
