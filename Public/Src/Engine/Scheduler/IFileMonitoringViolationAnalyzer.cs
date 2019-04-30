// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tracing;
using JetBrains.Annotations;
using static BuildXL.Scheduler.FileMonitoringViolationAnalyzer;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Interface for file monitoring violation analyzer.
    /// </summary>
    public interface IFileMonitoringViolationAnalyzer
    {
        /// <summary>
        /// Counters specific to analyzer.
        /// </summary>
        CounterCollection<FileMonitoringViolationAnalysisCounter> Counters { get; }

        /// <summary>
        /// Analyzes an unordered sequence of violations for a single pip. This may be called only once per pip.
        /// </summary>
        /// <returns>true if there were no violations marked as error</returns>
        /// <remarks>
        /// If double writes involving same-content files are detected but allowed, a collection of those non-reported violations 
        /// are returned. This is useful for the case of cache convergence, where outputs may change after the main violation analysis
        /// is done.
        /// </remarks>
        AnalyzePipViolationsResult AnalyzePipViolations(
            Process pip,
            [CanBeNull] IReadOnlyCollection<ReportedFileAccess> violations,
            [CanBeNull] IReadOnlyCollection<ReportedFileAccess> whitelistedAccesses,
            [CanBeNull] IReadOnlyCollection<(DirectoryArtifact, ReadOnlyArray<FileArtifact>)> exclusiveOpaqueDirectoryContent,
            [CanBeNull] IReadOnlyDictionary<AbsolutePath, IReadOnlyCollection<AbsolutePath>> sharedOpaqueDirectoryWriteAccesses,
            [CanBeNull] IReadOnlySet<AbsolutePath> allowedUndeclaredReads,
            [CanBeNull] IReadOnlySet<AbsolutePath> absentPathProbesUnderOutputDirectories,
            ReadOnlyArray<(FileArtifact fileArtifact, FileMaterializationInfo fileInfo, PipOutputOrigin pipOutputOrigin)> outputsContent,
            out IReadOnlyDictionary<FileArtifact, (FileMaterializationInfo, ReportedViolation)> allowedSameContentDoubleWriteViolations);

        /// <summary>
        /// Analyzes all dynamic violations. This is useful when replaying a pip from the cache, since otherwise some violations may not be seen.
        /// </summary>
        bool AnalyzeDynamicViolations(
            Process pip,
            [CanBeNull] IReadOnlyCollection<(DirectoryArtifact, ReadOnlyArray<FileArtifact>)> exclusiveOpaqueDirectoryContent,
            [CanBeNull] IReadOnlyDictionary<AbsolutePath, IReadOnlyCollection<AbsolutePath>> sharedOpaqueDirectoryWriteAccesses,
            [CanBeNull] IReadOnlySet<AbsolutePath> allowedUndeclaredReads,
            [CanBeNull] IReadOnlySet<AbsolutePath> absentPathProbesUnderOutputDirectories,
            ReadOnlyArray<(FileArtifact fileArtifact, FileMaterializationInfo fileInfo, PipOutputOrigin pipOutputOrigin)> outputsContent);

        /// <summary>
        /// Analyzes double writes after a cache convergence event. This may introduce new violations that were not flagged before convergence
        /// </summary>
        /// <remarks>
        /// The analysis is based on same-content double write violations that were allowed on the first place, before cache convergence happened
        /// </remarks>
        AnalyzePipViolationsResult AnalyzeDoubleWritesOnCacheConvergence(
            Process pip,
            ReadOnlyArray<(FileArtifact fileArtifact, FileMaterializationInfo fileInfo, PipOutputOrigin pipOutputOrigin)> convergedContent,
            IReadOnlyDictionary<FileArtifact, (FileMaterializationInfo fileMaterializationInfo, ReportedViolation reportedViolation)> allowedDoubleWriteViolations);
    }

    /// <summary>
    /// Holds the result of the AnalyzePipViolations(...) call
    /// </summary>
    public readonly struct AnalyzePipViolationsResult
    {
        /// <summary>
        /// Did AnalyzePipViolations(...) return any violations?
        /// </summary>
        public readonly bool IsViolationClean;

        /// <summary>
        ///  Did AnalyzePipViolations(...) find any violation errors that implies the pip is not safe to cache?
        /// </summary>
        public readonly bool PipIsSafeToCache;

        /// <summary>
        /// Construct AnalyzePipViolationsResult
        /// </summary>
        /// <param name="isViolationClean"></param>
        /// <param name="pipIsSafeToCache"></param>
        public AnalyzePipViolationsResult(bool isViolationClean, bool pipIsSafeToCache)
        {
            IsViolationClean = isViolationClean;
            PipIsSafeToCache = pipIsSafeToCache;
        }

        /// <summary>
        /// Sensible default
        /// </summary>
        public static AnalyzePipViolationsResult NoViolations => new AnalyzePipViolationsResult(isViolationClean: true, pipIsSafeToCache: true);
    }
}
