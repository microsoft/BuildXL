// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Scheduler;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tracing;
using JetBrains.Annotations;
using static BuildXL.Scheduler.FileMonitoringViolationAnalyzer;

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
            [CanBeNull] IReadOnlyCollection<(DirectoryArtifact, ReadOnlyArray<FileArtifact>)> exclusiveOpaqueDirectoryContent,
            [CanBeNull] IReadOnlyDictionary<AbsolutePath, IReadOnlyCollection<AbsolutePath>> sharedOpaqueDirectoryWriteAccesses,
            [CanBeNull] IReadOnlySet<AbsolutePath> allowedUndeclaredReads,
            [CanBeNull] IReadOnlySet<AbsolutePath> absentPathProbesUnderOutputDirectories,
            ReadOnlyArray<(FileArtifact fileArtifact, FileMaterializationInfo fileInfo, PipOutputOrigin pipOutputOrigin)> outputContent) => true;

        /// <inheritdoc />
        public AnalyzePipViolationsResult AnalyzePipViolations(
            Process pip,
            [CanBeNull] IReadOnlyCollection<ReportedFileAccess> violations,
            [CanBeNull] IReadOnlyCollection<ReportedFileAccess> whitelistedAccesses,
            [CanBeNull] IReadOnlyCollection<(DirectoryArtifact, ReadOnlyArray<FileArtifact>)> exclusiveOpaqueDirectoryContent,
            [CanBeNull] IReadOnlyDictionary<AbsolutePath, IReadOnlyCollection<AbsolutePath>> sharedOpaqueDirectoryWriteAccesses,
            [CanBeNull] IReadOnlySet<AbsolutePath> allowedUndeclaredReads,
            [CanBeNull] IReadOnlySet<AbsolutePath> absentPathProbesUnderOutputDirectories,
            ReadOnlyArray<(FileArtifact fileArtifact, FileMaterializationInfo fileInfo, PipOutputOrigin pipOutputOrigin)> outputsContent,
            out IReadOnlyDictionary<FileArtifact, (FileMaterializationInfo, ReportedViolation)> allowedSameContentDoubleWriteViolations)
        {
            allowedSameContentDoubleWriteViolations = CollectionUtilities.EmptyDictionary<FileArtifact, (FileMaterializationInfo, ReportedViolation)>();
            return AnalyzePipViolationsResult.NoViolations;
        }

        /// <inheritdoc />
        public AnalyzePipViolationsResult AnalyzeDoubleWritesOnCacheConvergence(
            Process pip,
            ReadOnlyArray<(FileArtifact fileArtifact, FileMaterializationInfo fileInfo, PipOutputOrigin pipOutputOrigin)> convergedContent,
            IReadOnlyDictionary<FileArtifact, (FileMaterializationInfo fileMaterializationInfo, ReportedViolation reportedViolation)> allowedDoubleWriteViolations)
        => AnalyzePipViolationsResult.NoViolations;
    }
}
