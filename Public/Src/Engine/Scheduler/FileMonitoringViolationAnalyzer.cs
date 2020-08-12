// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Pips;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using JetBrains.Annotations;
using static BuildXL.Utilities.FormattableStringEx;
#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Performs higher-level analysis of individual file monitoring violations for the purpose of better user reporting,
    /// including provenance information for the files accessed.
    /// </summary>
    /// <remarks>
    /// An single analyzer instance should be associated to a particular build session / scheduler. It may accumulate
    /// cross-pip analysis state. Violations are aggregated per-pip at analysis ingress (allowing localized de-dupe
    /// of e.g. multiple violations to one file), since each pip may call <see cref="AnalyzePipViolations" /> at most once.
    ///
    /// Analyses provided so far:
    /// - DoubleWrite: Detecting disallowed writes to a known build output, possibly generated beforehand.
    /// - ReadRace: Detecting disallowed reads such that the file is a known build output, but not guaranteed written by
    ///     the time this pip runs.
    /// - UndeclaredOrderedRead: Detecting disallowed reads such that the file is a known build output guaranteed
    ///     produced by the time the pip runs (e.g. declared input on a dll but not pdb produced by the same pip).
    /// - MissingSourceDependency: Detecting disallowed reads to declared or undeclared source files.
    /// - UndeclaredReadCycle: Detecting disallowed reads such that the file is a known build output that is produced
    ///     sometime after the pip runs.
    /// - UndeclaredOutput: Detecting writes that were undeclared
    /// - ReadUndeclaredOutput: Detecting reads of writes that were undeclared
    /// </remarks>
    public class FileMonitoringViolationAnalyzer : IFileMonitoringViolationAnalyzer
    {
        /// <summary>
        /// The type of dynamic accesses that can occur on a given path
        /// </summary>
        private enum DynamicFileAccessType : byte
        {
            /// <summary>
            /// A file/directory was produced
            /// </summary>
            Write = 1,

            /// <summary>
            /// A file was read (but not declared) - this option is only valid under allowed undeclared read mode
            /// </summary>
            UndeclaredRead = 2,

            /// <summary>
            /// A path was probed, but it was not physically present on disk
            /// </summary>
            AbsentPathProbe = 3
        }

        /// <summary>
        /// Detailed reasons when a same content rewrite on an undeclared source or alien file is denied
        /// </summary>
        private enum SameContentRewriteDisallowedReason
        {
            /// <summary>
            /// Same content rewrite is allowed
            /// </summary>
            None = 1,
            
            /// <summary>
            /// The rewrite happened on a file and reads cannot be guaranteed to consistently read the same content
            /// </summary>
            SameContentCannotBeGuaranteed = 2,
            
            /// <summary>
            /// Configured policy does not allow a same content rewrite
            /// </summary>
            PolicyDoesNotAllowRewrite = 3,
        }

        [SuppressMessage("StyleCop.CSharp.NamingRules", "SA1304:NonPrivateReadonlyFieldsMustBeginWithUpperCaseLetter")]
        protected readonly PipExecutionContext Context;
        protected readonly LoggingContext LoggingContext;
        private readonly IQueryablePipDependencyGraph m_graph;
        private readonly IQueryableFileContentManager m_fileContentManager;
        private readonly IExecutionLogTarget m_executionLog;
        private readonly ConcurrentBigMap<AbsolutePath, UndeclaredAccessors> m_undeclaredAccessors = new ConcurrentBigMap<AbsolutePath, UndeclaredAccessors>();
        
        // Maps of paths that are dynamically read or written to the corresponding pip. 
        // The tuple indicates whether it is a reader of the path or a writer, together with the actual pip and materialization info when available (absent file otherwise)
        private readonly ConcurrentBigMap<AbsolutePath, (DynamicFileAccessType accessType, PipId processPip, FileMaterializationInfo fileMaterializationInfo)> m_dynamicReadersAndWriters = new ConcurrentBigMap<AbsolutePath, (DynamicFileAccessType, PipId, FileMaterializationInfo)>();
        
        // Maps of path of temp files under shared opaques to their producers.
        // Under certain conditions the same file can be produced by multiple pips.
        private readonly ConcurrentBigMap<AbsolutePath, ConcurrentQueue<PipId>> m_dynamicTemporaryFileProducers = new ConcurrentBigMap<AbsolutePath, ConcurrentQueue<PipId>>();


        // Maps of paths that represent undeclared reads to all its known readers. Only kept for same content rewrites, since we need to track whether there is at least one reader ordered before the rewrite 
        private readonly ConcurrentBigMap<AbsolutePath, ConcurrentQueue<PipId>> m_undeclaredReaders = new ConcurrentBigMap<AbsolutePath, ConcurrentQueue<PipId>>();

        // Some dependency analysis rules cause issues with distribution. Even if /unsafe_* flags or configuration options
        // are used to downgrade errors to warnings, these must be treated as errors and cleaned up for distributed
        // builds to work.
        private readonly bool m_validateDistribution;
        private readonly bool m_unexpectedFileAccessesAsErrors;
        private readonly DynamicWriteOnAbsentProbePolicy m_dynamicWritesOnAbsentProbePolicy;
        private readonly CounterCollection<FileMonitoringViolationAnalysisCounter> m_counters = new CounterCollection<FileMonitoringViolationAnalysisCounter>();

        // When the materialization info for a file is not available/not pertinent, we store an absent file info
        private static readonly FileMaterializationInfo s_absentFileInfo = FileMaterializationInfo.CreateWithUnknownLength(WellKnownContentHashes.AbsentFile);

        /// <summary>
        /// Dependency-error classification as a result of analysis.
        /// </summary>
        public enum DependencyViolationType
        {
            /// <summary>
            /// Detected disallowed writes to a known build output, possibly generated beforehand.
            /// </summary>
            DoubleWrite,

            /// <summary>
            /// Detected disallowed read such that the file is a known build output, but not guaranteed written by
            /// the time this pip runs.
            /// </summary>
            ReadRace,

            /// <summary>
            /// Detected disallowed read such that the file is a known build output, guaranteed produced before the offending pip runs,
            /// but an explicit dependency was not declared (so it is not guaranteed materialized).
            /// </summary>
            UndeclaredOrderedRead,

            /// <summary>
            /// Detected disallowed read such that the file is a known build input, but an explicit dependency was not declared (so
            /// it is not hashed as an input to this pip).
            /// </summary>
            MissingSourceDependency,

            /// <summary>
            /// Detected disallowed read such that the file is a known build output that may be written by a pip that has a (chain of)
            /// explicit dependencies to the violating pip.
            /// </summary>
            UndeclaredReadCycle,

            /// <summary>
            /// Detected an output that wasn't declared. Other pips are not able to consume outputs that are not declared.
            /// </summary>
            UndeclaredOutput,

            /// <summary>
            /// Detected disallowed read on an output that wasn't declared. Other pips are not able to consume outputs that are not declared.
            /// </summary>
            ReadUndeclaredOutput,

            /// <summary>
            /// Detected a write inside a source seal directory
            /// </summary>
            WriteInSourceSealDirectory,

            /// <summary>
            /// Detected a write to the same path that is treated as an undeclared source file
            /// </summary>
            WriteInUndeclaredSourceRead,

            /// <summary>
            /// Detected a write on a path that corresponds to a file that existed before the pip ran.
            /// </summary>
            /// <remarks>
            /// Observe this file was not known to be an input (so there is not a HashSourceFile pip for it). This can
            /// be understood as a sub-category of <see cref="UndeclaredOutput"/>, but in this case the file is known to exist
            /// before the pip ran
            /// </remarks>
            WriteInExistingFile,

            /// <summary>
            /// Detected a write to the same path where an absent path probe also occurs
            /// </summary>
            WriteOnAbsentPathProbe,

            /// <summary>
            /// Detected a probe of a path under an opaque directory that is not a part of pip's dependencies
            /// </summary>
            AbsentPathProbeUnderUndeclaredOpaque,

            /// <summary>
            /// Detected a write inside a temp directory under a shared opaque
            /// </summary>
            WriteToTempPathInsideSharedOpaque,

            /// <summary>
            /// Detected a temp file produced by two pips that do not share an explicit dependency
            /// </summary>
            TempFileProducedByIndependentPips,
        }

        /// <summary>
        /// Levels of access observed at a path. These levels are totally ordered - write is strictly higher than read.
        /// </summary>
        public enum AccessLevel
        {
            /// <summary>
            /// A file was unexpectedly accessed at read-level (no write-level accesses).
            /// </summary>
            Read = 0,

            /// <summary>
            /// A file was unexpectedly accessed at write-level. Read-level accesses possibly occurred as well.
            /// </summary>
            Write = 1,
        }

        /// <summary>
        /// Aggregation of multiple reported violations to a single path.
        /// </summary>
        private readonly struct AggregateViolation
        {
            public readonly AccessLevel Level;
            public readonly AbsolutePath Path;
            public readonly AbsolutePath ProcessPath;
            public readonly FileAccessStatusMethod Method;

            public AggregateViolation(AccessLevel level, AbsolutePath path, AbsolutePath processPath, FileAccessStatusMethod method)
            {
                Contract.Requires(path.IsValid);
                Contract.Requires(processPath.IsValid);
                Level = level;
                Path = path;
                ProcessPath = processPath;
                Method = method;
            }

            public AggregateViolation Combine(AccessLevel newLevel)
            {
                return new AggregateViolation(
                    (AccessLevel)Math.Max((int)Level, (int)newLevel),
                    Path,
                    ProcessPath,
                    Method);
            }
        }

        /// <summary>
        /// Creates an analyzer that uses the given queryable graph for querying declared dependency information.
        /// </summary>
        public FileMonitoringViolationAnalyzer(
            LoggingContext loggingContext,
            PipExecutionContext context,
            IQueryablePipDependencyGraph graph,
            IQueryableFileContentManager fileContentManager,
            bool validateDistribution,
            bool unexpectedFileAccessesAsErrors,
            DynamicWriteOnAbsentProbePolicy ignoreDynamicWritesOnAbsentProbes,
            IExecutionLogTarget executionLog = null)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(context != null);
            Contract.Requires(graph != null);
            Contract.Requires(fileContentManager != null);

            LoggingContext = loggingContext;
            Context = context;
            m_graph = graph;
            m_fileContentManager = fileContentManager;
            m_validateDistribution = validateDistribution;
            m_unexpectedFileAccessesAsErrors = unexpectedFileAccessesAsErrors;
            m_dynamicWritesOnAbsentProbePolicy = ignoreDynamicWritesOnAbsentProbes;
            m_executionLog = executionLog;
        }

        /// <inheritdoc />
        public CounterCollection<FileMonitoringViolationAnalysisCounter> Counters => m_counters;

        /// <inheritdoc />
        public AnalyzePipViolationsResult AnalyzePipViolations(
            Process pip,
            [CanBeNull] IReadOnlyCollection<ReportedFileAccess> violations,
            [CanBeNull] IReadOnlyCollection<ReportedFileAccess> allowlistedAccesses,
            [CanBeNull] IReadOnlyCollection<(DirectoryArtifact, ReadOnlyArray<FileArtifactWithAttributes>)> exclusiveOpaqueDirectoryContent,
            [CanBeNull] IReadOnlyDictionary<AbsolutePath, IReadOnlyCollection<FileArtifactWithAttributes>> sharedOpaqueDirectoryWriteAccesses,
            [CanBeNull] IReadOnlySet<AbsolutePath> allowedUndeclaredReads,
            [CanBeNull] IReadOnlySet<AbsolutePath> absentPathProbesUnderOutputDirectories,
            ReadOnlyArray<(FileArtifact fileArtifact, FileMaterializationInfo fileInfo, PipOutputOrigin pipOutputOrigin)> outputsContent,
            out IReadOnlyDictionary<FileArtifact, (FileMaterializationInfo, ReportedViolation)> allowedSameContentDoubleWriteViolations)
        {
            Contract.Requires(pip != null);

            using (m_counters.StartStopwatch(FileMonitoringViolationAnalysisCounter.AnalyzePipViolationsDuration))
            {
                // Early return to avoid wasted allocations.
                if ((violations == null || violations.Count == 0) &&
                    (!m_validateDistribution || (allowlistedAccesses == null || allowlistedAccesses.Count == 0)) &&
                    (sharedOpaqueDirectoryWriteAccesses == null || sharedOpaqueDirectoryWriteAccesses.Count == 0) &&
                    (allowedUndeclaredReads == null || allowedUndeclaredReads.Count == 0) &&
                    (absentPathProbesUnderOutputDirectories == null || absentPathProbesUnderOutputDirectories.Count == 0) &&
                    (exclusiveOpaqueDirectoryContent == null || exclusiveOpaqueDirectoryContent.Count == 0))
                {
                    allowedSameContentDoubleWriteViolations = CollectionUtilities.EmptyDictionary<FileArtifact, (FileMaterializationInfo, ReportedViolation)>();
                    return AnalyzePipViolationsResult.NoViolations;
                }

                var allowedDoubleWriteViolations = new Dictionary<FileArtifact, (FileMaterializationInfo, ReportedViolation)>();
                var outputArtifactInfo = GetOutputArtifactInfoMap(pip, outputsContent);
                
                UpdateUndeclaredReadersIfNeeded(pip, allowedUndeclaredReads);

                ReportedViolation[] reportedDependencyViolations = null;
                ReportedFileAccess[] nonAnalyzableViolations = null;
                if (violations?.Count > 0)
                {
                    reportedDependencyViolations = ClassifyAndReportAggregateViolations(
                        pip,
                        violations,
                        isAllowlistedViolation: false,
                        outputArtifactInfo,
                        allowedDoubleWriteViolations,
                        out nonAnalyzableViolations);
                }

                ReportedViolation[] reportedDependencyViolationsForAllowlisted = null;
                if (m_validateDistribution && allowlistedAccesses?.Count > 0)
                {
                    reportedDependencyViolationsForAllowlisted = ClassifyAndReportAggregateViolations(
                        pip,
                        allowlistedAccesses,
                        isAllowlistedViolation: true,
                        outputArtifactInfo,
                        allowedDoubleWriteViolations,
                        out _);
                }

                var errorPaths = new HashSet<ReportedViolation>();
                var warningPaths = new HashSet<ReportedViolation>();

                // For violation analysis results
                if (reportedDependencyViolations != null)
                {
                    Func<ReportedFileAccess, ReportedViolation?> getAccessViolationPath =
                        a =>
                        {
                            if (TryGetAccessedAndProcessPaths(pip, a, out var path, out var processPath))
                            {
                                return new ReportedViolation(isError: false,
                                    a.IsWriteViolation ? DependencyViolationType.UndeclaredOutput : DependencyViolationType.UndeclaredOrderedRead,
                                    path: path,
                                    violatorPipId: pip.PipId,
                                    relatedPipId: null,
                                    processPath: processPath);
                            }

                            // The failure to parse the accessed path has already been logged in TryParseAbsolutePath.
                            return null;
                        };

                    if (nonAnalyzableViolations?.Length > 0)
                    {
                        // Populated non-reported violations. Note that this modifies the underlying errorPaths and warningPaths hashet
                        var errorOrWarningPaths = m_unexpectedFileAccessesAsErrors ? errorPaths : warningPaths;

                        // If unexpectedFileAccessesAsErrors is false, then (violations - reportedDependencyViolations) are warnings.
                        // The paths whose access is RequestedAccess.None or whose paths cannot be parsed are not analyzed for dependency violations.
                        // Because we were originally logging a warning (dx09) or error (dx14) for those paths, we wanted to keep the same behavior.
                        errorOrWarningPaths.UnionWith(
                            nonAnalyzableViolations
                            .Select(a => getAccessViolationPath(a))
                            // skip violations for which we failed to parse the accessed path or process path
                            .Where(a => a != null)
                            .Select(a => a.Value));
                    }

                    PopulateErrorsAndWarnings(reportedDependencyViolations, errorPaths, warningPaths);
                }

                // For allowlisted analysis results
                if (reportedDependencyViolationsForAllowlisted != null && reportedDependencyViolationsForAllowlisted.Length > 0)
                {
                    // If /validateDistribution is enabled, we need to log errors from reportedDependencyViolationsForallowlisted.
                    var errors = reportedDependencyViolationsForAllowlisted.Where(a => a.IsError);
                    errorPaths.UnionWith(errors);
                }

                var dynamicViolations = ReportDynamicViolations(pip, exclusiveOpaqueDirectoryContent, sharedOpaqueDirectoryWriteAccesses, allowedUndeclaredReads, absentPathProbesUnderOutputDirectories, outputArtifactInfo, allowedDoubleWriteViolations);
                allowedSameContentDoubleWriteViolations = new ReadOnlyDictionary<FileArtifact, (FileMaterializationInfo, ReportedViolation)>(allowedDoubleWriteViolations);

                PopulateErrorsAndWarnings(dynamicViolations, errorPaths, warningPaths);

                LogErrorsAndWarnings(pip, errorPaths, warningPaths);

                // The pip is safe to cache if there are no violations or all its violations do not make the pip uncacheable
                bool pipIsSafeToCache =
                    (reportedDependencyViolations != null ? new List<ReportedViolation>(reportedDependencyViolations) : new List<ReportedViolation>())
                    .Union(dynamicViolations)
                    .All(violation => !violation.ViolationMakesPipUncacheable);

                return new AnalyzePipViolationsResult(
                    isViolationClean: errorPaths.Count == 0,
                    pipIsSafeToCache);
            }
        }

        private void UpdateUndeclaredReadersIfNeeded(Process pip, IReadOnlySet<AbsolutePath> allowedUndeclaredReads)
        {
            // If same content rewrites are allowed, keep track of all readers from undeclared reads, since in case of a rewrite we need
            // to make sure there is at least one reader ordered before the write
            if ((pip.RewritePolicy & RewritePolicy.SafeSourceRewritesAreAllowed) != 0 && allowedUndeclaredReads?.Count > 0)
            {
                foreach (var undeclaredRead in allowedUndeclaredReads)
                {
                    m_undeclaredReaders.AddOrUpdate(undeclaredRead, pip.PipId,
                        (path, pipId) => { var readers = new ConcurrentQueue<PipId>(); readers.Enqueue(pipId); return readers; },
                        (path, pipId, readers) => { readers.Enqueue(pipId); return readers; });
                }
            }
        }

        /// <inheritdoc/>
        public AnalyzePipViolationsResult AnalyzeDoubleWritesOnCacheConvergence(
            Process pip,
            ReadOnlyArray<(FileArtifact fileArtifact, FileMaterializationInfo fileInfo, PipOutputOrigin pipOutputOrigin)> convergedContent,
            IReadOnlyDictionary<FileArtifact, (FileMaterializationInfo fileMaterializationInfo, ReportedViolation reportedViolation)> allowedDoubleWriteViolations)
        {
            Contract.Requires(pip != null);

            if (allowedDoubleWriteViolations.Count == 0)
            {
                return AnalyzePipViolationsResult.NoViolations;
            }

            var disallowedViolationsOnConvergence = new List<ReportedViolation>();
            foreach (var content in convergedContent)
            {
                // If the converged content changed, then the allowed double write becomes a true violation
                if (allowedDoubleWriteViolations.TryGetValue(content.fileArtifact, out var originalContentAndViolation) &&
                    originalContentAndViolation.fileMaterializationInfo.Hash != content.fileInfo.Hash)
                {
                    ReportedViolation violation = originalContentAndViolation.reportedViolation;
                    Contract.Assert(violation.RelatedPipId.HasValue, "A double write violation should always have a related pip Id");

                    disallowedViolationsOnConvergence.Add(
                        HandleDependencyViolation(
                            violation.Type,
                            AccessLevel.Write,
                            violation.Path,
                            (Process)m_graph.HydratePip(violation.ViolatorPipId, PipQueryContext.FileMonitoringViolationAnalyzerClassifyAndReportAggregateViolations),
                            isAllowlistedViolation: false,
                            (Process)m_graph.HydratePip(violation.RelatedPipId.Value, PipQueryContext.FileMonitoringViolationAnalyzerClassifyAndReportAggregateViolations),
                            violation.ProcessPath));
                }
            }

            var errorPaths = new HashSet<ReportedViolation>();
            var warningPaths = new HashSet<ReportedViolation>();
            PopulateErrorsAndWarnings(disallowedViolationsOnConvergence, errorPaths, warningPaths);

            LogErrorsAndWarnings(pip, errorPaths, warningPaths);

            return new AnalyzePipViolationsResult(
                isViolationClean: errorPaths.Count == 0,
                pipIsSafeToCache: true);
        }

        /// <summary>
        /// Returns a map with all output file artifact with their corresponding file materialization info
        /// </summary>
        /// <remarks>
        /// The map is only populated when <see cref="RewritePolicyExtensions.ImpliesContentAwareness(RewritePolicy)"/>, which is the policy that actually require the analyzer to be content aware. Otherwise it just returns an empty map.
        /// This is to improve performance of subsequent lookups.
        /// </remarks>
        private static IReadOnlyDictionary<FileArtifact, FileMaterializationInfo> GetOutputArtifactInfoMap(Process pip, ReadOnlyArray<(FileArtifact fileArtifact, FileMaterializationInfo fileInfo, PipOutputOrigin pipOutputOrigin)> outputsContent)
        {
            if (pip.RewritePolicy.ImpliesContentAwareness())
            {
                var result = new Dictionary<FileArtifact, FileMaterializationInfo>(outputsContent.Length);
                foreach (var kvp in outputsContent)
                {
                    // outputContents may have duplicate content. See PipExecutor.GetCacheHitExecutionResult
                    // So let's consider that, but make sure it is consistent
                    if (!result.TryAdd(kvp.fileArtifact, kvp.fileInfo))
                    {
                        Contract.Assert(result[kvp.fileArtifact].FileContentInfo.Hash == kvp.fileInfo.FileContentInfo.Hash, 
                            "outputsContent may have duplicated content, but file materialization hash should be the same for the same file artifact");
                    }
                }

                return result;
            }
            else
            {
                return CollectionUtilities.EmptyDictionary<FileArtifact, FileMaterializationInfo>();
            }
        }

        /// <inheritdoc />
        public bool AnalyzeDynamicViolations(
            Process pip,
            IReadOnlyCollection<(DirectoryArtifact, ReadOnlyArray<FileArtifactWithAttributes>)> exclusiveOpaqueDirectoryContent,
            [CanBeNull] IReadOnlyDictionary<AbsolutePath, IReadOnlyCollection<FileArtifactWithAttributes>> sharedOpaqueDirectoryWriteAccesses,
            [CanBeNull] IReadOnlySet<AbsolutePath> allowedUndeclaredReads,
            [CanBeNull] IReadOnlySet<AbsolutePath> absentPathProbesUnderOutputDirectories,
            ReadOnlyArray<(FileArtifact fileArtifact, FileMaterializationInfo fileInfo, PipOutputOrigin pipOutputOrigin)> outputsContent)
        {
            Contract.Requires(pip != null);

            using (m_counters.StartStopwatch(FileMonitoringViolationAnalysisCounter.AnalyzeDynamicViolationsDuration))
            {
                var errorPaths = new HashSet<ReportedViolation>();
                var warningPaths = new HashSet<ReportedViolation>();

                UpdateUndeclaredReadersIfNeeded(pip, allowedUndeclaredReads);

                List<ReportedViolation> dynamicViolations = ReportDynamicViolations(
                    pip,
                    exclusiveOpaqueDirectoryContent,
                    sharedOpaqueDirectoryWriteAccesses,
                    allowedUndeclaredReads,
                    absentPathProbesUnderOutputDirectories,
                    GetOutputArtifactInfoMap(pip, outputsContent),
                    // We don't need to collect allowed same content double writes here since this is used in the cache replay scenario only, when there is no convergence
                    allowedDoubleWriteViolations: null);

                PopulateErrorsAndWarnings(dynamicViolations, errorPaths, warningPaths);

                LogErrorsAndWarnings(pip, errorPaths, warningPaths);

                return errorPaths.Count == 0;
            }
        }

        private List<ReportedViolation> ReportDynamicViolations(
            Process pip,
            [CanBeNull] IReadOnlyCollection<(DirectoryArtifact, ReadOnlyArray<FileArtifactWithAttributes>)> exclusiveOpaqueDirectories,
            [CanBeNull] IReadOnlyDictionary<AbsolutePath, IReadOnlyCollection<FileArtifactWithAttributes>> sharedOpaqueDirectoryWriteAccesses,
            [CanBeNull] IReadOnlySet<AbsolutePath> allowedUndeclaredReads,
            [CanBeNull] IReadOnlySet<AbsolutePath> absentPathProbesUnderOutputDirectories,
            IReadOnlyDictionary<FileArtifact, FileMaterializationInfo> outputArtifactInfo,
            [CanBeNull] Dictionary<FileArtifact, (FileMaterializationInfo fileMaterializationInfo, ReportedViolation reportedViolation)> allowedDoubleWriteViolations)
        {
            List<ReportedViolation> dynamicViolations = new List<ReportedViolation>();

            if (sharedOpaqueDirectoryWriteAccesses?.Count > 0)
            {
                ReportSharedOpaqueViolations(pip, sharedOpaqueDirectoryWriteAccesses, dynamicViolations, outputArtifactInfo, allowedDoubleWriteViolations);
            }

            if (exclusiveOpaqueDirectories?.Count > 0)
            {
                ReportExclusiveOpaqueViolations(pip, exclusiveOpaqueDirectories, dynamicViolations, outputArtifactInfo);
            }

            if (allowedUndeclaredReads?.Count > 0)
            {
                ReportAllowedUndeclaredReadViolations(pip, allowedUndeclaredReads, dynamicViolations, allowedDoubleWriteViolations);
            }

            if (absentPathProbesUnderOutputDirectories?.Count > 0)
            {
                ReportAbsentPathProbesUnderOutputDirectoriesViolations(pip, absentPathProbesUnderOutputDirectories, dynamicViolations);
            }

            return dynamicViolations;
        }

        private void LogErrorsAndWarnings(Process pip, HashSet<ReportedViolation> errorPaths, HashSet<ReportedViolation> warningPaths)
        {
            Func<PipId, string> getDescription = (pipId) =>
            {
                return m_graph.HydratePip(pipId, PipQueryContext.FileMonitoringViolationAnalyzerClassifyAndReportAggregateViolations).GetDescription(Context);
            };

            if (errorPaths.Count > 0)
            {
                Logger.Log.FileMonitoringError(
                    LoggingContext,
                    pip.SemiStableHash,
                    pip.GetDescription(Context),
                    AggregateAccessViolationPaths(errorPaths, Context.PathTable, getDescription));
            }

            if (warningPaths.Count > 0)
            {
                Logger.Log.FileMonitoringWarning(
                    LoggingContext,
                    pip.SemiStableHash,
                    pip.GetDescription(Context),
                    AggregateAccessViolationPaths(warningPaths, Context.PathTable, getDescription));
            }
        }

        private bool TryGetAccessedAndProcessPaths(Process pip, ReportedFileAccess reportedAccess, out AbsolutePath accessedPath, out AbsolutePath processPath)
        {
            accessedPath = processPath = AbsolutePath.Invalid;
            return reportedAccess.TryParseAbsolutePath(Context, LoggingContext, pip, out accessedPath)
                   && AbsolutePath.TryCreate(Context.PathTable, reportedAccess.Process.Path, out processPath);
        }


        private void PopulateErrorsAndWarnings(IEnumerable<ReportedViolation> reportedDependencyViolations, ISet<ReportedViolation> errorPaths, ISet<ReportedViolation> warningPaths)
        {
            var errors = reportedDependencyViolations.Where(a => a.IsError);
            var warnings = reportedDependencyViolations.Where(a => !a.IsError);

            (m_unexpectedFileAccessesAsErrors ? errorPaths : warningPaths).UnionWith(errors);
            warningPaths.UnionWith(warnings);
        }

        internal static string AggregateAccessViolationPaths(HashSet<ReportedViolation> paths, PathTable pathTable, Func<PipId, string> getPipDescription)
        {
            using (var wrap = Pools.GetStringBuilder())
            {
                var builder = wrap.Instance;

                HashSet<PipId> relatedNodes = new HashSet<PipId>();
                HashSet<string> legendText = new HashSet<string>();

                // Handle each process observed to have file accesses
                var accessesByProcesses = paths.ToMultiValueDictionary(item => item.ProcessPath, item => item);
                foreach (var accessByProcess in accessesByProcesses.OrderBy(item => item.Key.ToString(pathTable), StringComparer.OrdinalIgnoreCase))
                {
                    var processPath = accessByProcess.Key;
                    var processAccessesByPath = accessByProcess.Value.ToMultiValueDictionary(item => item.Path, item => item);

                    bool printedProcessHeaderRow = false;

                    // Handle each path accessed by that process
                    foreach (var pathsAccessed in processAccessesByPath.OrderBy(item => item.Key.ToString(pathTable), StringComparer.OrdinalIgnoreCase))
                    {
                        var path = pathsAccessed.Key;
                        if (!path.IsValid)
                        {
                            // skip elements with invalid paths
                            continue;
                        }

                        if (!printedProcessHeaderRow)
                        {
                            builder.AppendLine($"Disallowed file accesses performed by: {processPath.ToString(pathTable)}");
                            printedProcessHeaderRow = true;
                        }

                        // There may be more than one access for the same path under the process. We pick the "worst" access to display
                        ReportedViolation worstAccess = new ReportedViolation();
                        for (int i = 0; i < pathsAccessed.Value.Count; i++)
                        {
                            var thisAccess = pathsAccessed.Value[i];

                            // Collect any relatedNodes if applicable to display at the end of the message
                            if (thisAccess.RelatedPipId.HasValue && thisAccess.RelatedPipId.Value.IsValid)
                            {
                                relatedNodes.Add(thisAccess.RelatedPipId.Value);
                            }

                            if (i == 0)
                            {
                                worstAccess = thisAccess;
                            }
                            else if(thisAccess.ReportingType > worstAccess.ReportingType)
                            {
                                worstAccess = thisAccess;
                            }
                        }

                        // Write out the access information
                        builder.AppendLine(worstAccess.RenderForDFASummary(pathTable));
                        legendText.Add(worstAccess.LegendText);
                    }

                    builder.AppendLine();
                }

                // Display summary information for what file access abbreviations mean
                if (legendText.Count > 0)
                {
                    foreach (string line in legendText.OrderBy(s => s))
                    {
                        builder.AppendLine(line);
                    }

                    builder.AppendLine();
                }

                // Reference any replated pips
                if (relatedNodes.Count > 0)
                {
                    builder.AppendLine("Violations related to pip(s):");
                    foreach (var pipId in relatedNodes)
                    {
                        builder.AppendLine(getPipDescription(pipId));
                    }
                }

                // cutting the trailing line break
                return builder.ToString().Trim();
            }
        }

        /// <summary>
        /// Wrapper for <see cref="IQueryablePipDependencyGraph.TryFindProducer"/> that tracks <see cref="FileMonitoringViolationAnalysisCounter.ViolationClassificationGraphQueryDuration"/>
        /// </summary>
        private Pip TryFindProducer(AbsolutePath producedPath, VersionDisposition versionDisposition, DependencyOrderingFilter? orderingFilter = null)
        {
            using (m_counters.StartStopwatch(FileMonitoringViolationAnalysisCounter.ViolationClassificationGraphQueryDuration))
            {
                return m_graph.TryFindProducer(producedPath, versionDisposition, orderingFilter);
            }
        }

        /// <summary>
        /// Reports violations for shared opaque directories
        /// </summary>
        /// <remarks>
        /// Violations of this kind can be:
        /// - Another pip dynamically or statically writing the same file
        /// - A source sealed directory containing the write
        /// - Another pip probed absent path at which location <paramref name="pip"/> created a directory
        /// </remarks>
        private void ReportSharedOpaqueViolations(
            Process pip,
            IReadOnlyDictionary<AbsolutePath, IReadOnlyCollection<FileArtifactWithAttributes>> sharedOpaqueDirectoryWriteAccesses,
            List<ReportedViolation> reportedViolations,
            IReadOnlyDictionary<FileArtifact, FileMaterializationInfo> outputArtifactsInfo,
            [CanBeNull] Dictionary<FileArtifact, (FileMaterializationInfo fileMaterializationInfo, ReportedViolation reportedViolation)> allowedDoubleWriteViolations)
        {
            foreach (var kvp in sharedOpaqueDirectoryWriteAccesses)
            {
                var accesses = kvp.Value;
                var rootSharedOpaqueDirectory = kvp.Key;
                using var createdSubDirectoriesWrapper = IsDirProbeAnalysisDisabled ? (PooledObjectWrapper<HashSet<AbsolutePath>>?)null : Pools.GetAbsolutePathSet();
                foreach (var access in accesses)
                {
                    ReportWriteViolations(pip, reportedViolations, outputArtifactsInfo, access, allowedDoubleWriteViolations);
                    ReportBlockedScopesViolations(pip, reportedViolations, access.Path);
                    DirProbe_AccumulateParentDirectories(createdSubDirectoriesWrapper?.Instance, rootSharedOpaqueDirectory, access.Path);
                }

                DirProbe_ReportViolations(pip, createdSubDirectoriesWrapper?.Instance, reportedViolations);
            }
        }

        #region Directory Probe Analysis 
        private bool IsDirProbeAnalysisDisabled => m_dynamicWritesOnAbsentProbePolicy.HasFlag(DynamicWriteOnAbsentProbePolicy.IgnoreDirectoryProbes);

        /// <summary>
        /// Returns immediately if <see cref="IsDirProbeAnalysisDisabled"/> is true.
        /// 
        /// Otherwise, traverses parents of <paramref name="accessPath"/> up to (and exclusing) <paramref name="rootDirectory"/>
        /// and adds them to <paramref name="accumulator"/>.
        /// </summary>
        private void DirProbe_AccumulateParentDirectories(HashSet<AbsolutePath> accumulator, AbsolutePath rootDirectory, AbsolutePath accessPath)
        {
            Contract.Requires(IsDirProbeAnalysisDisabled || accumulator != null);

            if (IsDirProbeAnalysisDisabled)
            {
                return;
            }

            // traverse parents of the 'access' path up to its root shared opaque directory
            var parent = accessPath.GetParent(Context.PathTable);
            while (parent.IsValid && parent != rootDirectory)
            {
                accumulator.Add(parent);
                parent = parent.GetParent(Context.PathTable);
            }
        }

        /// <summary>
        /// Returns immediately if <see cref="IsDirProbeAnalysisDisabled"/> is true.
        /// 
        /// Otherwise, goes through the list of directories (<paramref name="createdDirectories"/>) created by <paramref name="pip"/>
        /// and for each checks if there was another pip that had previously reported an absent path probe for it.  If so, it creates
        /// a violation, reports it, and adds it to <paramref name="reportedViolations"/>.
        /// </summary>
        private void DirProbe_ReportViolations(Process pip, HashSet<AbsolutePath> createdDirectories, List<ReportedViolation> reportedViolations)
        {
            Contract.Requires(IsDirProbeAnalysisDisabled || createdDirectories != null);

            if (IsDirProbeAnalysisDisabled)
            {
                return;
            }

            // check if any of the created sub-directories was previously probed (while it was still absent) by a different pip
            foreach (var dirAccess in createdDirectories)
            {
                var getResult = m_dynamicReadersAndWriters.TryGet(dirAccess);
                if (getResult.IsFound &&
                    getResult.Item.Value.processPip != pip.PipId &&
                    getResult.Item.Value.accessType == DynamicFileAccessType.AbsentPathProbe &&
                    !IsDependencyDeclared(writerPipId: pip.PipId, absentProbePipId: getResult.Item.Value.processPip))
                {
                    var relatedPip = m_graph.HydratePip(getResult.Item.Value.processPip, PipQueryContext.FileMonitoringViolationAnalyzerClassifyAndReportAggregateViolations);
                    reportedViolations.Add(HandleDependencyViolation(
                        DependencyViolationType.WriteOnAbsentPathProbe,
                        AccessLevel.Write,
                        dirAccess,
                        pip,
                        isAllowlistedViolation: false,
                        relatedPip,
                        pip.Executable.Path));
                }
            }
        }
        #endregion

        private void ReportBlockedScopesViolations(Process pip, List<ReportedViolation> reportedViolations, AbsolutePath access)
        {
            // Check there are no source sealed directories containing the write
            // Observe that source sealed directories are not allowed above a shared opaque, which means that any source sealed directory
            // containing the access will be under the shared opaque (or they will share roots)
            var sourceSealedContainer = m_graph.TryGetSealSourceAncestor(access);
            if (sourceSealedContainer != DirectoryArtifact.Invalid)
            {
                var relatedPip = m_graph.GetSealedDirectoryPip(sourceSealedContainer, PipQueryContext.FileMonitoringViolationAnalyzerClassifyAndReportAggregateViolations);

                // We found a source seal directory that contains the accessed path
                reportedViolations.Add(
                    HandleDependencyViolation(
                        DependencyViolationType.WriteInSourceSealDirectory,
                        AccessLevel.Write,
                        access,
                        pip,
                        isAllowlistedViolation: false,
                        related: relatedPip,
                        // we don't have the path of the process that caused the file access violation, so 'blame' the main process (i.e., the current pip) instead
                        pip.Executable.Path));
            }

            // Check if the shared opaque write happens inside a temp directory
            if (m_graph.TryGetTempDirectoryAncestor(access, out var ownerPip, out var tempPath))
            {
                // We found a shared opaque directory that contains a temp directory
                reportedViolations.Add(
                    HandleDependencyViolation(
                        DependencyViolationType.WriteToTempPathInsideSharedOpaque,
                        AccessLevel.Write,
                        access,
                        pip,
                        isAllowlistedViolation: false,
                        related: ownerPip,
                        tempPath));
            }
        }

        private void ReportWriteViolations(
            Process pip, 
            List<ReportedViolation> reportedViolations, 
            IReadOnlyDictionary<FileArtifact, FileMaterializationInfo> outputArtifactsInfo,
            FileArtifactWithAttributes access,
            [CanBeNull] Dictionary<FileArtifact, (FileMaterializationInfo fileMaterializationInfo, ReportedViolation reportedViolation)> allowedDoubleWriteViolations)
        {
            // The access in an opaque has always rewrite count 1
            Contract.Assert(access.RewriteCount == 1);
            var artifact = access.ToFileArtifact();
            var outputArtifactInfo = GetOutputMaterializationInfo(outputArtifactsInfo, artifact);

            RegisterWriteInPathAndUpdateViolations(pip, access, reportedViolations, outputArtifactInfo, out ReportedViolation? allowedDoubleWriteViolation);
            if (allowedDoubleWriteViolations != null && allowedDoubleWriteViolation.HasValue)
            {
                allowedDoubleWriteViolations[artifact] = (outputArtifactInfo, allowedDoubleWriteViolation.Value);
            }

            // Now look for a static one. Any pip statically producing this file, regardless of the scheduled order,
            // is considered a double write violation
            var maybeProducer = TryFindProducer(access.Path, VersionDisposition.Latest);

            if (maybeProducer != null && maybeProducer.PipId != pip.PipId)
            {
                // AllowSameContentDoubleWrites is not actually supported for statically declared files, since the double write may not have occurred yet, and the content
                // may be unavailable. So just warn about this, and log the violation as an error.
                if ((pip.RewritePolicy & RewritePolicy.AllowSameContentDoubleWrites) != 0)
                {
                    Logger.Log.AllowSameContentPolicyNotAvailableForStaticallyDeclaredOutputs(LoggingContext, pip.GetDescription(Context), access.Path.ToString(Context.PathTable));
                }

                // We found a double write: two pips tried to produce the same file in the cone of a shared opaque directory. One statically, the current one
                // dynamically
                reportedViolations.Add(
                    HandleDependencyViolation(
                        DependencyViolationType.DoubleWrite,
                        AccessLevel.Write,
                        access.Path,
                        pip,
                        isAllowlistedViolation: false,
                        related: maybeProducer,
                        // we don't have the path of the process that caused the file access violation, so 'blame' the main process (i.e., the current pip) instead
                        pip.Executable.Path));
            }
        }

        private FileMaterializationInfo GetOutputMaterializationInfo(IReadOnlyDictionary<FileArtifact, FileMaterializationInfo> outputArtifactsInfo, FileArtifact fileArtifact)
        {
            var success = outputArtifactsInfo.TryGetValue(fileArtifact, out FileMaterializationInfo outputArtifactInfo);
            if (!success)
            {
                // The artifact may not be there because we either didn't populate the output artifact dictionary (because the double write policy doesn't require this information)
                // or because the pip failed, in which case the output content is not reported
                outputArtifactInfo = s_absentFileInfo;
            }

            return outputArtifactInfo;
        }

        private void ReportExclusiveOpaqueViolations(
            Process pip,
            IReadOnlyCollection<(DirectoryArtifact, ReadOnlyArray<FileArtifactWithAttributes>)> exclusiveOpaqueContent,
            List<ReportedViolation> reportedViolations,
            IReadOnlyDictionary<FileArtifact, FileMaterializationInfo> outputArtifactsInfo)
        {
            using var outputDirectoryExclusionSetWrapper = Pools.AbsolutePathSetPool.GetInstance();
            var outputDirectoryExclusionSet = outputDirectoryExclusionSetWrapper.Instance;
            outputDirectoryExclusionSet.AddRange(pip.OutputDirectoryExclusions);

            // Static outputs under exclusive opaques are blocked by construction
            // Same for sealed source directories under exclusive opaques (not allowed at graph construction time)
            // So the only cases left are:
            // * The dynamic one. Observe that double writes are also not allowed by construction, so
            // this is effectively about writes in undeclared sources and absent file probes
            // * Content under directory exclusions. Observe this case doesn't have a shared opaque correlate here since
            // the detours-based approach for shared opaques already takes care of this just by not attributing the corresponding writes
            // to the owning shared opaque directory
            foreach ((_, ReadOnlyArray<FileArtifactWithAttributes> directoryContent) in exclusiveOpaqueContent)
            {
                foreach (FileArtifactWithAttributes fileArtifact in directoryContent)
                {
                    var outputArtifactInfo = GetOutputMaterializationInfo(outputArtifactsInfo, fileArtifact.ToFileArtifact());
                    RegisterWriteInPathAndUpdateViolations(pip, fileArtifact, reportedViolations, outputArtifactInfo, out _);
                    ReportExclusiveOpaqueExclusions(pip, reportedViolations, fileArtifact, outputDirectoryExclusionSet);
                }
            }
        }

        private void ReportExclusiveOpaqueExclusions(
            Process pip,
            List<ReportedViolation> reportedViolations,
            FileArtifactWithAttributes fileArtifact,
            HashSet<AbsolutePath> outputDirectoryExclusions)
        {
            // If an exclusive opaque file is under a directory exclusion, the violation is reported as an undeclared output. This matches
            // the shared opaque case.
            if (IsFileUnderAnExclusion(fileArtifact.Path, outputDirectoryExclusions, Context.PathTable))
            {
                reportedViolations.Add(
                    HandleDependencyViolation(
                        DependencyViolationType.UndeclaredOutput,
                        AccessLevel.Write,
                        fileArtifact.Path,
                        pip,
                        isAllowlistedViolation: false,
                        related: null,
                        // we don't have the path of the process that caused the file access violation, so 'blame' the main process (i.e., the current pip) instead
                        pip.Executable.Path));
            }
        }

        /// <summary>
        /// Register a write in <paramref name="access"/> by <paramref name="pip"/>. And in the case the write access generates a violation,
        /// populate <paramref name="reportedViolations"/>
        /// </summary>
        private void RegisterWriteInPathAndUpdateViolations(
            Process pip, 
            FileArtifactWithAttributes access, 
            List<ReportedViolation> reportedViolations,
            FileMaterializationInfo outputMaterializationInfo,
            out ReportedViolation? allowedSameContentDoubleWriteViolation)
        {
            allowedSameContentDoubleWriteViolation = null;
            var path = access.Path;

            // Register the access and the writer, so we can spot other dynamic accesses to the same file later
            var result = m_dynamicReadersAndWriters.GetOrAdd(
                path,
                pip,
                (accessKey, producer) => (DynamicFileAccessType.Write, producer.PipId, outputMaterializationInfo));

            if (access.IsTemporaryOutputFile)
            {
                m_dynamicTemporaryFileProducers.AddOrUpdate(
                    key: path,
                    data: pip.PipId,
                    addValueFactory: (p, pipId) =>
                    {
                        var queue = new ConcurrentQueue<PipId>();
                        queue.Enqueue(pipId);
                        return queue;
                    },
                    updateValueFactory: (p, pipId, oldValue) =>
                    {
                        oldValue.Enqueue(pipId);
                        return oldValue;
                    });
            }

            // We found an existing dynamic access to the same file
            if (result.IsFound && result.Item.Value.processPip != pip.PipId)
            {
                var relatedPipId = result.Item.Value.processPip;

                DependencyViolationType violationType;
                switch (result.Item.Value.accessType)
                {
                    // There was another write, so this is a double write
                    case DynamicFileAccessType.Write:
                        
                        if ((pip.RewritePolicy & RewritePolicy.AllowSameContentDoubleWrites) != 0 && 
                            (m_graph.GetRewritePolicy(result.Item.Value.processPip) & RewritePolicy.AllowSameContentDoubleWrites) != 0 &&
                            outputMaterializationInfo.Hash == result.Item.Value.fileMaterializationInfo.Hash)
                        {
                            // Just log a verbose message to indicate a same-content double write happened
                            Logger.Log.AllowedSameContentDoubleWrite(
                                LoggingContext,
                                pip.SemiStableHash,
                                pip.GetDescription(Context),
                                path.ToString(Context.PathTable),
                                m_graph.GetFormattedSemiStableHash(relatedPipId));

                            allowedSameContentDoubleWriteViolation = new ReportedViolation(isError: true, DependencyViolationType.DoubleWrite, path, pip.PipId, relatedPipId, pip.Executable.Path);
                            return;
                        }

                        if (access.IsTemporaryOutputFile)
                        {
                            // If it's a write at a temp file path, we need to check that there is dependency between the current pip
                            // and all of the producers of that file. We also need to check that the current pip is not deleting a real
                            // file that was produced previously.
                            bool badAccess = false;
                            bool visitedOriginalProducer = false;
                            if (m_dynamicTemporaryFileProducers.TryGetValue(path, out var producers))
                            {
                                foreach (var producerPipId in producers)
                                {
                                    visitedOriginalProducer |= producerPipId == result.Item.Value.processPip;
                                    if (producerPipId != pip.PipId
                                        && !m_graph.IsReachableFrom(from: producerPipId, to: pip.PipId))
                                    {
                                        // Found a producer of the same temp file, and there is no dependency between these
                                        // two pips in the graph.

                                        // Update the related pip if it does not match the one that was hydrated above
                                        if (relatedPipId != producerPipId)
                                        {
                                            relatedPipId = producerPipId;
                                        }

                                        violationType = DependencyViolationType.TempFileProducedByIndependentPips;
                                        badAccess = true;
                                        break;
                                    }
                                }
                            }

                            if (!visitedOriginalProducer)
                            {
                                // writing over a real file -> DoubleWrite
                                violationType = DependencyViolationType.DoubleWrite;
                            }
                            else if (badAccess)
                            {
                                // writing at a temp file path, but there is no dependency -> TempFileProducedByIndependentPips
                                violationType = DependencyViolationType.TempFileProducedByIndependentPips;
                            }
                            else
                            {
                                // writing at a temp file path and there is a dependency -> allowed access
                                return;
                            }
                        }
                        else
                        {
                            violationType = DependencyViolationType.DoubleWrite;
                        }

                        break;
                    // There was an undeclared read, so this is a write in an undeclared read
                    case DynamicFileAccessType.UndeclaredRead:
                        // Check if the violation can be relaxed
                        if (IsAllowedRewriteOnUndeclaredFile(pip.PipId, pip.RewritePolicy, outputMaterializationInfo, pip.Executable.Path, path, out allowedSameContentDoubleWriteViolation, out var disallowedReason, out var racyReader))
                        {
                            // Log a verbose message to indicate a rewrite on an undeclared source happened
                            Logger.Log.AllowedRewriteOnUndeclaredFile(LoggingContext, pip.SemiStableHash, pip.GetDescription(Context), path.ToString(Context.PathTable));

                            return;
                        }

                        // Log a verbose message explaining why the same-content check failed
                        LogDisallowedReasonIfNeeded(disallowedReason, pip, path, racyReader);

                        violationType = DependencyViolationType.WriteInUndeclaredSourceRead;
                        break;
                    // There was an absent file probe, so this is a write on an absent file probe
                    case DynamicFileAccessType.AbsentPathProbe:
                        // WriteOnAbsentPathProbe message literally says "declare an explicit dependency between these pips",
                        // so don't complain if a dependency already exists (i.e., 'pip' must run after 'related').
                        if (m_dynamicWritesOnAbsentProbePolicy.HasFlag(DynamicWriteOnAbsentProbePolicy.IgnoreFileProbes) ||
                            IsDependencyDeclared(absentProbePipId: relatedPipId, writerPipId: pip.PipId))
                        {
                            return;
                        }

                        violationType = DependencyViolationType.WriteOnAbsentPathProbe;
                        break;
                    default:
                        throw new InvalidOperationException(I($"Unexpected value {result.Item.Value.accessType}"));
                }

                var related = (Process)m_graph.HydratePip(relatedPipId, PipQueryContext.FileMonitoringViolationAnalyzerClassifyAndReportAggregateViolations);

                reportedViolations.Add(
                    HandleDependencyViolation(
                        violationType,
                        AccessLevel.Write,
                        access.Path,
                        pip,
                        isAllowlistedViolation: false,
                        related: related,
                        // we don't have the path of the process that caused the file access violation, so 'blame' the main process (i.e., the current pip) instead
                        pip.Executable.Path));
            }
            // If we didn't find any other accesses on this file, but this is an undeclared file rewrite
            // this is only allowed if safe source rewrites is on. Otherwise is a DFA.
            else if (!result.IsFound && 
                    access.IsUndeclaredFileRewrite)
            {
                if ((pip.RewritePolicy & RewritePolicy.SafeSourceRewritesAreAllowed) == 0)
                {
                    reportedViolations.Add(
                    HandleDependencyViolation(
                        DependencyViolationType.WriteInExistingFile,
                        AccessLevel.Write,
                        path,
                        pip,
                        isAllowlistedViolation: false,
                        related: null,
                        // we don't have the path of the process that caused the file access violation, so 'blame' the main process (i.e., the current pip) instead
                        pip.Executable.Path));
                }
                else
                {
                    // Log a verbose message to indicate a rewrite on an undeclared source happened
                    Logger.Log.AllowedRewriteOnUndeclaredFile(LoggingContext, pip.SemiStableHash, pip.GetDescription(Context), path.ToString(Context.PathTable));
                }
            }
        }
        
        private bool IsAllowedRewriteOnUndeclaredFile(
            PipId writerPipId,
            RewritePolicy writerDoubleWritePolicy, 
            FileMaterializationInfo writeMaterializationInfo, 
            AbsolutePath writerExecutablePath,
            AbsolutePath undeclaredRead, 
            out ReportedViolation? allowedSameContentRewriteViolation,
            out SameContentRewriteDisallowedReason disallowedReason,
            out PipId? racyReader)
        {
            racyReader = null;

            // We may allow writing in an undeclared file if a relaxing policy is configured and:
            // 1) Buildxl can guarantee the written content is the same. This means being aware of the previous content by virtue of an undeclared read ordered before the rewrite
            // 2) BuildXL can guarantee there are no reads preceding the rewrite. In this case we can allow writing a different content (buildxl has no way to check whether the 
            // original content was the same or not), but that's fine since the build will never see the original content.
            if ((writerDoubleWritePolicy & RewritePolicy.SafeSourceRewritesAreAllowed) != 0 &&
                m_undeclaredReaders.TryGetValue(undeclaredRead, out var readers))
            {
                // Retrieve the hash of the undeclared file. Observe in this case the hash should be always be retrieved from the cache since an undeclared read on that path already happened
                var maybeUndeclaredSourceMaterializationInfo = m_fileContentManager.TryQueryUndeclaredInputContentAsync(undeclaredRead).GetAwaiter().GetResult();
                Contract.Assert(maybeUndeclaredSourceMaterializationInfo.HasValue);

                // Check if we have the same content. Observe if alls reads happened after the write, we may get the same content but that just means we didn't get the chance to know the original content
                var isSameContent = writeMaterializationInfo.Hash == maybeUndeclaredSourceMaterializationInfo.Value.Hash;
                   
                // Make sure the ordering constraints for the readers can be satisfied
                if (ReadersAreWellOrdered(readers, writerPipId, isSameContent, out racyReader))
                {
                    allowedSameContentRewriteViolation = new ReportedViolation(isError: true, DependencyViolationType.WriteInUndeclaredSourceRead, undeclaredRead, writerPipId, relatedPipId: null, writerExecutablePath);
                    disallowedReason = SameContentRewriteDisallowedReason.None;
                    return true;
                }
                else
                {
                    disallowedReason = SameContentRewriteDisallowedReason.SameContentCannotBeGuaranteed;
                }
            }
            else if ((writerDoubleWritePolicy & RewritePolicy.SafeSourceRewritesAreAllowed) != 0)
            {
                // There are no known readers so far. So it is safe to allow the rewrite regardless of the content
                disallowedReason = SameContentRewriteDisallowedReason.None;
                allowedSameContentRewriteViolation = null;
                return true;
            }
            else 
            {
                disallowedReason = SameContentRewriteDisallowedReason.PolicyDoesNotAllowRewrite;
            }

            allowedSameContentRewriteViolation = null;
            return false;
        }

        /// <summary>
        /// Make sure the ordering constraints for source/alien file readers wrt a rewrite can be satisfied
        /// </summary>
        private bool ReadersAreWellOrdered(ConcurrentQueue<PipId> readers, PipId writerPipId, bool isSameContent, out PipId? racyReader)
        {
            bool hasNonOrderedReaders = false;
            racyReader = null;
            foreach (PipId reader in readers)
            {
                // if there is at least one read that is guaranteed to happen before the write, we can trust we observed the file content before the write happened
                if (IsDependencyDeclared(writerPipId, reader))
                {
                    // If we saw the same content and there is a read before the write, that means we can trust we saw the before/after
                    // and there are actually no restrictions on the readers order
                    // So we can shortcut the validation
                    if (isSameContent)
                    {
                        return true;
                    }
                }
                else if (!IsDependencyDeclared(reader, writerPipId))
                {
                    hasNonOrderedReaders = true;
                    // This will just store the last racy one
                    racyReader = reader;
                }
            }

            // If there are unordered readers, then we cannot guarantee they saw the same content.
            return !hasNonOrderedReaders;
        }

        private bool IsDependencyDeclared(PipId writerPipId, PipId absentProbePipId)
        {
            return m_graph.IsReachableFrom(from: absentProbePipId, to: writerPipId);
        }

        private void ReportAllowedUndeclaredReadViolations(
            Process pip,
            IReadOnlySet<AbsolutePath> allowedUndeclaredReads,
            List<ReportedViolation> reportedViolations,
            [CanBeNull] Dictionary<FileArtifact, (FileMaterializationInfo fileMaterializationInfo, ReportedViolation reportedViolation)> allowedDoubleWriteViolations)
        {
            foreach (var undeclaredRead in allowedUndeclaredReads)
            {
                // We look for a static writer of the file. Any pip statically producing this file, regardless of the scheduled order,
                // violates the assumption that undeclared reads should always happen against source files
                var maybeProducer = TryFindProducer(undeclaredRead, VersionDisposition.Latest);

                if (maybeProducer != null && maybeProducer.PipType != PipType.HashSourceFile)
                {
                    reportedViolations.Add(
                        HandleDependencyViolation(
                            DependencyViolationType.WriteInUndeclaredSourceRead,
                            AccessLevel.Write,
                            undeclaredRead,
                            pip,
                            isAllowlistedViolation: false,
                            related: maybeProducer,
                            // we don't have the path of the process that caused the file access violation, so 'blame' the main process (i.e., the current pip) instead
                            pip.Executable.Path));
                }

                // Register the access and the reader, so we can spot other dynamic accesses to the same
                // file later
                var result = m_dynamicReadersAndWriters.GetOrAdd(
                    undeclaredRead,
                    pip,
                    (accessKey, producer) => (DynamicFileAccessType.UndeclaredRead, producer.PipId, s_absentFileInfo));

                // If there is already an access on that path and it was a:
                // - write: then this is a case of a write in an undeclared source
                // - read: not a violation, we allow other readers
                // - absent probe: not a violation, we allow other probes
                // Two things to observe still: we are only storing the first dynamic access on the path, which means in case multiple
                // accesses occur, in case of a violation the actual reported culprit will be non-deterministic. But this only matters for
                // error reporting, since we are just giving one of the potential offender, not all. Furthermore, this logic is correct assuming
                // that a write after any other type of access produces a violation (write -> write, undeclared read -> write and absent file probe -> write).
                // This is currently the case. Otherwise, multiple access types need to be stored and validated.
                if (result.IsFound && result.Item.Value.accessType == DynamicFileAccessType.Write)
                {
                    var writerPipId = result.Item.Value.processPip;
                    var writerMaterializationInfo = result.Item.Value.fileMaterializationInfo;

                    // Check if the violation can be ignored because of same-content policies
                    if (IsAllowedRewriteOnUndeclaredFile(
                        writerPipId, 
                        m_graph.GetRewritePolicy(writerPipId),
                        writerMaterializationInfo, 
                        m_graph.GetProcessExecutablePath(writerPipId), 
                        undeclaredRead, 
                        out var allowedSameContentRewriteViolation,
                        out var disallowedReason,
                        out var racyReader))
                    {
                        if (allowedDoubleWriteViolations != null && allowedSameContentRewriteViolation.HasValue)
                        {
                            // This is a dynamic write, and therefore has rewrite count 1
                            allowedDoubleWriteViolations[FileArtifact.CreateOutputFile(undeclaredRead)] = (writerMaterializationInfo, allowedSameContentRewriteViolation.Value);
                        }

                        continue;
                    }

                    var related = (Process)m_graph.HydratePip(writerPipId, PipQueryContext.FileMonitoringViolationAnalyzerClassifyAndReportAggregateViolations);
                    
                    // Log a verbose message explaining why the same-content check failed
                    LogDisallowedReasonIfNeeded(disallowedReason, related, undeclaredRead, racyReader);

                    reportedViolations.Add(
                        HandleDependencyViolation(
                            DependencyViolationType.WriteInUndeclaredSourceRead,
                            AccessLevel.Write,
                            undeclaredRead,
                            pip,
                            isAllowlistedViolation: false,
                            related: related,
                            // we don't have the path of the process that caused the file access violation, so 'blame' the main process (i.e., the current pip) instead
                            pip.Executable.Path));
                }
            }
        }

        private void ReportAbsentPathProbesUnderOutputDirectoriesViolations(
            Process pip,
            IReadOnlySet<AbsolutePath> absentPathProbesUnderOutputDirectories,
            List<ReportedViolation> reportedViolations)
        {
            // If the sandbox is configured to ignore these, we shortcut the search
            if (m_dynamicWritesOnAbsentProbePolicy == DynamicWriteOnAbsentProbePolicy.IgnoreAll)
            {
                return;
            }

            foreach (var absentPathProbe in absentPathProbesUnderOutputDirectories)
            {
                // The static case (an absent file probe that is also statically written) is already taken care
                // of in ObservedInputProcessor, so we only deal with the dynamic case here.
                var result = m_dynamicReadersAndWriters.GetOrAdd(
                    absentPathProbe,
                    pip,
                    (accessKey, producer) => (DynamicFileAccessType.AbsentPathProbe, producer.PipId, s_absentFileInfo));


                // Equivalent logic than the one used on ReportAllowedUndeclaredReadViolations, see details there.
                if (result.IsFound &&
                    result.Item.Value.accessType == DynamicFileAccessType.Write &&
                    !m_graph.IsReachableFrom(from: result.Item.Value.processPip, to: pip.PipId))
                {
                    // The writer is always reported as the violator
                    var writer = (Process) m_graph.HydratePip(result.Item.Value.processPip, PipQueryContext.FileMonitoringViolationAnalyzerClassifyAndReportAggregateViolations);

                    reportedViolations.Add(
                        HandleDependencyViolation(
                            DependencyViolationType.WriteOnAbsentPathProbe,
                            AccessLevel.Write,
                            absentPathProbe,
                            writer,
                            isAllowlistedViolation: false,
                            related: pip,
                            // we don't have the path of the process that caused the file access violation, so 'blame' the main process (i.e., the current pip) instead
                            writer.Executable.Path));
                }
                else if (!result.IsFound)
                {
                    // No pips have tried to access this path. Check if the pip probed a path under one of its
                    // directory dependencies, and if it's the case, proceed based on the config.
                    // Since absentPathProbe is guaranteed to be a probe under an opaque directory (ObservedInputProcessor takes care of this),
                    // it is safe to enumerate over all directory dependencies here and not just 'opaque' dependencies. 
                    var allowProbe = pip.DirectoryDependencies.Any(dir => absentPathProbe.IsWithin(Context.PathTable, dir));
                    
                    // If the probe is outside of input directories the pip depends on, we do not need to check whether the probed path
                    // matches file dependencies. If it did match, the access would have been classified as read rather than probe.

                    if (allowProbe)
                    {
                        continue;
                    }

                    // If the pip is running in Unsafe/Relaxed mode, do not treat this probe as an error.
                    // Reporting a violation will trigger a DFA error. Reporting a allowlisted violation makes pip uncacheable, and we don't want to do this.
                    if (pip.ProcessAbsentPathProbeInUndeclaredOpaquesMode == Process.AbsentPathProbeInUndeclaredOpaquesMode.Strict)
                    {
                        var violation = HandleDependencyViolation(
                            DependencyViolationType.AbsentPathProbeUnderUndeclaredOpaque,
                            AccessLevel.Read,
                            absentPathProbe,
                            pip,
                            isAllowlistedViolation: false,
                            related: pip,
                            pip.Executable.Path);

                        reportedViolations.Add(violation);
                    }
                    else
                    {
                        Logger.Log.AbsentPathProbeInsideUndeclaredOpaqueDirectory(
                            LoggingContext,
                            pip.SemiStableHash,
                            pip.GetDescription(Context),
                            absentPathProbe.ToString(Context.PathTable));
                    }
                }
            }
        }

        /// <summary>
        /// Classifies and reports violations
        /// </summary>
        /// <returns>false if any error level violations were encountered</returns>
        private ReportedViolation[] ClassifyAndReportAggregateViolations(
            Process pip,
            IReadOnlyCollection<ReportedFileAccess> violations,
            bool isAllowlistedViolation,
            IReadOnlyDictionary<FileArtifact, FileMaterializationInfo> outputArtifactInfo,
            Dictionary<FileArtifact, (FileMaterializationInfo, ReportedViolation)> allowedSameContentDoubleWriteViolations,
            out ReportedFileAccess[] nonAnalyzableViolations)
        {
            var aggregateViolationsByPath = new Dictionary<(AbsolutePath, AbsolutePath), AggregateViolation>();
            using (var nonAnalyzableViolationsMutableWrapper = ProcessPools.ReportedFileAccessList.GetInstance())
            {
                var nonAnalyzableViolationsMutable = nonAnalyzableViolationsMutableWrapper.Instance;

                foreach (ReportedFileAccess violation in violations)
                {
                    if (violation.RequestedAccess == RequestedAccess.None)
                    {
                        nonAnalyzableViolationsMutable.Add(violation);
                        // How peculiar.
                        continue;
                    }

                    AbsolutePath path;
                    if (!violation.TryParseAbsolutePath(Context, LoggingContext, pip, out path))
                    {
                        nonAnalyzableViolationsMutable.Add(violation);
                        continue;
                    }
                    AbsolutePath processPath;
                    if (!AbsolutePath.TryCreate(Context.PathTable, violation.Process.Path, out processPath))
                    {
                        nonAnalyzableViolationsMutable.Add(violation);
                        continue;
                    }

                    // it's possible that a single path was accessed by several different processes (e.g., child processes),
                    // so we aggregate based on (path, process) rather than (path)
                    var key = (path, processPath);
                    AggregateViolation aggregate;
                    aggregate = aggregateViolationsByPath.TryGetValue(key, out aggregate)
                        ? aggregate.Combine(GetAccessLevel(violation.RequestedAccess))
                        : new AggregateViolation(GetAccessLevel(violation.RequestedAccess), path, processPath, violation.Method);

                    aggregateViolationsByPath[key] = aggregate;
                }

                nonAnalyzableViolations = nonAnalyzableViolationsMutable.ToArray();
            }

            AggregateViolation[] aggregateViolations = aggregateViolationsByPath.Values.ToArray();

            m_counters.AddToCounter(FileMonitoringViolationAnalysisCounter.NumbersOfViolationClassificationAttempts, aggregateViolations.Length);

            var reportedViolations = new List<ReportedViolation>();

            foreach (AggregateViolation violation in aggregateViolations)
            {
                switch (violation.Level)
                {
                    case AccessLevel.Write:
                        // DoubleWrite:
                        //      This pip has performed a write. If there is a pip that *could* have legitimately written the
                        //      file beforehand (perhaps not in this execution), we say this pip has performed a 'double write' on the file.
                        Pip maybeProducer = TryFindProducer(
                            violation.Path,
                            VersionDisposition.Latest,
                            new DependencyOrderingFilter(DependencyOrderingFilterType.PossiblyPrecedingInWallTime, pip));

                        // If there was not a static producer, check if there is a dynamic one so we can refine
                        // the report as a double write if found. 
                        // Otherwise the case where the pip writes to a path that is part of a shared opaque dependency 
                        // gets flagged as an undeclared write, with no connection to the original producer
                        if (maybeProducer == null && 
                            m_dynamicReadersAndWriters.TryGetValue(violation.Path, out var kvp) && 
                            kvp.accessType == DynamicFileAccessType.Write)
                        {
                            maybeProducer = m_graph.HydratePip(kvp.processPip, PipQueryContext.FileMonitoringViolationAnalyzerClassifyAndReportAggregateViolations);
                        }

                        if (maybeProducer != null)
                        {
                            // AllowSameContentDoubleWrites is not actually supported for statically declared files, since the double write may not have occurred yet, and the content
                            // may be unavailable. So just warn about this, and log the violation as an error.
                            if ((pip.RewritePolicy & RewritePolicy.AllowSameContentDoubleWrites) != 0)
                            {
                                Logger.Log.AllowSameContentPolicyNotAvailableForStaticallyDeclaredOutputs(LoggingContext, pip.GetDescription(Context), violation.Path.ToString(Context.PathTable));
                            }

                            // TODO: Maybe have a separate violation type for writing to source files.
                            reportedViolations.Add(
                                HandleDependencyViolation(
                                    DependencyViolationType.DoubleWrite,
                                    AccessLevel.Write,
                                    violation.Path,
                                    pip,
                                    isAllowlistedViolation,
                                    related: maybeProducer,
                                    violation.ProcessPath));
                            continue;
                        }
                        else
                        {
                            // This is the case where there is no producer. 
                            // When the violation was determined based on the manifest policy, this means a standard undeclared write.
                            // When the violation was determined based on file existence, this means the pip tried to dynamically write into an undeclared 
                            // file that was not created by the pip. This is case is handled in ReportDynamicViolations
                            if (violation.Method != FileAccessStatusMethod.FileExistenceBased)
                            {
                                reportedViolations.Add(
                                    HandleDependencyViolation(
                                        DependencyViolationType.UndeclaredOutput,
                                        AccessLevel.Write,
                                        violation.Path,
                                        pip,
                                        isAllowlistedViolation,
                                        related: null,
                                        violation.ProcessPath));
                            }
                            // Handle known readers for undeclared output

                            // NOTE: Modifications to undeclared accessors is safe because map ensure synchronized acccess or
                            // add and update delegates on the same key
                            var undeclaredAccessorsResult = m_undeclaredAccessors.AddOrUpdate(
                                violation.Path,
                                pip.PipId,
                                (path, writerId) => new UndeclaredAccessors {Writer = writerId},
                                (path, writerId, accessors) => !accessors.Writer.IsValid ? new UndeclaredAccessors {Writer = writerId} : accessors);

                            // There were readers and this is the writer which was assigned for this path
                            if (undeclaredAccessorsResult.OldItem.Value.Readers != null && undeclaredAccessorsResult.Item.Value.Writer == pip.PipId)
                            {
                                foreach (var undeclaredReader in undeclaredAccessorsResult.OldItem.Value.Readers)
                                {
                                    reportedViolations.Add(
                                        ReportReadUndeclaredOutput(
                                            violation.Path,
                                            consumer: (Process) m_graph.HydratePip(
                                                undeclaredReader,
                                                PipQueryContext.FileMonitoringViolationAnalyzerClassifyAndReportAggregateViolations),
                                            producer: pip,
                                            isAllowlistedViolation: isAllowlistedViolation,
                                            violation.ProcessPath));
                                }
                            }

                            continue;
                        }

                    case AccessLevel.Read:
                        // ReadRace:
                        //      This pip has performed a read. If there is a pip that *could* have legitimately written the
                        //      file *concurrently with* this read (perhaps not in this execution), this was a race.
                        Pip maybeConcurrentProducer = TryFindProducer(
                            violation.Path,
                            VersionDisposition.Earliest,
                            new DependencyOrderingFilter(DependencyOrderingFilterType.Concurrent, pip));

                        if (maybeConcurrentProducer != null)
                        {
                            reportedViolations.Add(
                                HandleDependencyViolation(
                                    // HashSourceFile is special because it means the read is of a read-only build input.
                                    maybeConcurrentProducer.PipType == PipType.HashSourceFile
                                        ? DependencyViolationType.MissingSourceDependency
                                        : DependencyViolationType.ReadRace,
                                    AccessLevel.Read,
                                    violation.Path,
                                    pip,
                                    isAllowlistedViolation,
                                    related: maybeConcurrentProducer,
                                    violation.ProcessPath));

                            continue;
                        }

                        // UndeclaredOrderedRead:
                        //      The read file doesn't have a concurrent producer. It therefore has (a) a preceding producer, (b) a subsequent producer, or (c) no producer.
                        //      In the event of (a), we have an UndeclaredOrderedRead - the read is well-ordered but wasn't declared. That class of error is particularly
                        //      relevant to MLAM and distributed builds, which reserve the right to not materialize all transitive inputs of a process.
                        Pip maybePrecedingProducer = TryFindProducer(
                            violation.Path,
                            VersionDisposition.Earliest,
                            new DependencyOrderingFilter(DependencyOrderingFilterType.OrderedBefore, pip));

                        if (maybePrecedingProducer != null)
                        {
                            reportedViolations.Add(
                                HandleDependencyViolation(
                                    // HashSourceFile is special because it means the read is of a read-only build input.
                                    maybePrecedingProducer.PipType == PipType.HashSourceFile
                                        ? DependencyViolationType.MissingSourceDependency
                                        : DependencyViolationType.UndeclaredOrderedRead,
                                    AccessLevel.Read,
                                    violation.Path,
                                    pip,
                                    isAllowlistedViolation,
                                    related: maybePrecedingProducer,
                                    violation.ProcessPath));

                            continue;
                        }

                        // No preceding producer; check for a subsequent producer (with no DependencyOrderingFilter).
                        Pip maybeSubsequentProducer = TryFindProducer(
                            violation.Path,
                            VersionDisposition.Latest);

                        if (maybeSubsequentProducer != null)
                        {
                            Contract.Assert(
                                maybeSubsequentProducer.PipType != PipType.HashSourceFile,
                                "A HashSourceFile pip should have been found either concurrently or before, but was found only in an unordered search.");

                            // TODO: assert that this is actually subsequent?
                            reportedViolations.Add(
                                HandleDependencyViolation(
                                    DependencyViolationType.UndeclaredReadCycle,
                                    AccessLevel.Read,
                                    violation.Path,
                                    pip,
                                    isAllowlistedViolation,
                                    related: maybeSubsequentProducer,
                                    violation.ProcessPath));

                            continue;
                        }

                        // No statically declared producers. Check for a dynamically observed produced file
                        if (m_dynamicReadersAndWriters.TryGetValue(violation.Path, out var producer))
                        {
                            maybeProducer = m_graph.HydratePip(producer.processPip, PipQueryContext.FileMonitoringViolationAnalyzerClassifyAndReportAggregateViolations);

                            reportedViolations.Add(
                                HandleDependencyViolation(
                                    DependencyViolationType.ReadRace,
                                    AccessLevel.Read,
                                    violation.Path,
                                    pip,
                                    isAllowlistedViolation,
                                    related: maybeProducer,
                                    violation.ProcessPath));

                            continue;
                        }

                        // No declared producers whatsoever.  Probably just a new not-mentioned-anywhere source file.
                        // TODO: Maybe there should be a separate violation for 'extra file accessed under an output mount' (need to first disallow source files in writable mounts).
                        reportedViolations.Add(
                        HandleDependencyViolation(
                            DependencyViolationType.MissingSourceDependency,
                            AccessLevel.Read,
                            violation.Path,
                            pip,
                            isAllowlistedViolation,
                            related: null,
                            violation.ProcessPath));

                        // Report read for undeclared output if applicable.
                        // NOTE: For the sake of determinism with the case where writer may run after reader, we still report a missing source dependency

                        // NOTE: Modifications to undeclared accessors is safe because map ensure synchronized access or
                        // add and update delegates on the same key
                        var undeclaredAccessors = m_undeclaredAccessors.AddOrUpdate(
                            violation.Path,
                            pip.PipId,
                            (path, readerId) => new UndeclaredAccessors { Readers = new List<PipId> { readerId } },
                            (path, readerId, accessors) =>
                            {
                                if (!accessors.Writer.IsValid)
                                {
                                    accessors.Readers.Add(readerId);
                                }

                                return accessors;
                            }).Item.Value;

                        if (undeclaredAccessors.Writer.IsValid)
                        {
                            reportedViolations.Add(
                                ReportReadUndeclaredOutput(
                                    violation.Path,
                                    isAllowlistedViolation: isAllowlistedViolation,
                                    consumer: pip,
                                    producer: (Process)m_graph.HydratePip(
                                        undeclaredAccessors.Writer,
                                        PipQueryContext.FileMonitoringViolationAnalyzerClassifyAndReportAggregateViolations),
                                    processPath: violation.ProcessPath));
                        }

                        continue;
                    default:
                        throw Contract.AssertFailure("Unknown AccessLevel");
                }
            }

            return reportedViolations.ToArray();
        }

        private void LogDisallowedReasonIfNeeded(SameContentRewriteDisallowedReason disallowedReason, Process writerPip, AbsolutePath undeclaredSource, PipId? racyReaderId)
        {
            Contract.Requires(disallowedReason != SameContentRewriteDisallowedReason.SameContentCannotBeGuaranteed || racyReaderId != null);

            string detail = string.Empty;

            switch (disallowedReason)
            {
                // If the configured policy does not allow for same-content rewrites, there is nothing that is worth communicating the user about
                case SameContentRewriteDisallowedReason.None:
                case SameContentRewriteDisallowedReason.PolicyDoesNotAllowRewrite:
                    return;
                case SameContentRewriteDisallowedReason.SameContentCannotBeGuaranteed:
                    var racyReader = m_graph.HydratePip(racyReaderId.Value, PipQueryContext.FileMonitoringViolationAnalyzerClassifyAndReportAggregateViolations);
                    detail = $"The rewrite occured on a file where readers are not guaranteed to always see the same content. Pip '{racyReader.GetDescription(Context)}' should be ordered either after or before the rewriting pip.";
                    break;
                default:
                    Contract.Assert(false, $"Unexpected reason '{disallowedReason}'");
                    break;
            }

            Logger.Log.DisallowedSameContentRewriteOnUndeclaredFile(LoggingContext, writerPip.SemiStableHash, writerPip.GetDescription(Context), undeclaredSource.ToString(Context.PathTable), detail);
        }

        private ReportedViolation ReportReadUndeclaredOutput(
            AbsolutePath violationPath,
            Process consumer,
            Process producer,
            bool isAllowlistedViolation,
            AbsolutePath processPath)
        {
            return HandleDependencyViolation(
                DependencyViolationType.ReadUndeclaredOutput,
                AccessLevel.Read,
                violationPath,
                consumer,
                isAllowlistedViolation,
                related: producer,
                processPath);
        }

        private static AccessLevel GetAccessLevel(RequestedAccess requestedAccess)
        {
            Contract.Requires(requestedAccess != RequestedAccess.None);

            return (requestedAccess & RequestedAccess.Write) != 0 ? AccessLevel.Write : AccessLevel.Read;
        }

        private static bool IsFileUnderAnExclusion(AbsolutePath path, HashSet<AbsolutePath> outputDirectoryExclusions, PathTable pathTable)
        {
            // If there are no exclusions, shortcut the search
            if (outputDirectoryExclusions.Count == 0)
            {
                return false;
            }

            // TODO: Consider adding a cache, since it is likely there are many files under the same directory
            foreach (var current in pathTable.EnumerateHierarchyBottomUp(path.Value))
            {
                var currentAsPath = new AbsolutePath(current);
                if (outputDirectoryExclusions.Contains(currentAsPath))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Log dependency violations by emitting user-facing, warning-level events.
        /// This function is virtual to allow unit tests to check the collected violations.
        /// </summary>
        /// <returns>False if any errors were logged, signaling that the pip needs to be marked as a failure</returns>
        protected virtual ReportedViolation HandleDependencyViolation(
            DependencyViolationType violationType,
            AccessLevel accessLevel,
            AbsolutePath path,
            Process violator,
            bool isAllowlistedViolation,
            Pip related,
            AbsolutePath processPath)
        {
            Contract.Assume(path.IsValid);
            Contract.Assume(violator != null);
            Contract.Assume(processPath.IsValid);

            bool isError = !isAllowlistedViolation;
            bool hasRelatedPip = related != null;

            switch (violationType)
            {
                case DependencyViolationType.MissingSourceDependency:

                    if (isError)
                    {
                        Logger.Log.DependencyViolationMissingSourceDependency(
                            LoggingContext,
                            violator.SemiStableHash,
                            violator.GetDescription(Context),
                            path.ToString(Context.PathTable));
                    }

                    break;
                case DependencyViolationType.UndeclaredOutput:

                    if (isError)
                    {
                        Logger.Log.DependencyViolationUndeclaredOutput(
                            LoggingContext,
                            violator.SemiStableHash,
                            violator.GetDescription(Context),
                            path.ToString(Context.PathTable));
                    }

                    break;
                case DependencyViolationType.DoubleWrite:
                    isError = isError || m_validateDistribution;

                    if (isError && hasRelatedPip)
                    {
                        Logger.Log.DependencyViolationDoubleWrite(
                            LoggingContext,
                            violator.SemiStableHash,
                            violator.GetDescription(Context),
                            path.ToString(Context.PathTable),
                            related.GetDescription(Context));
                    }

                    break;
                case DependencyViolationType.ReadRace:
                    isError = isError || m_validateDistribution;

                    if (isError && hasRelatedPip)
                    {
                        Logger.Log.DependencyViolationReadRace(
                            LoggingContext,
                            violator.SemiStableHash,
                            violator.GetDescription(Context),
                            violator.Provenance.Token.Path.ToString(Context.PathTable),
                            violator.WorkingDirectory.ToString(Context.PathTable),
                            path.ToString(Context.PathTable),
                            related.GetDescription(Context));
                    }

                    break;
                case DependencyViolationType.UndeclaredOrderedRead:
                    isError = isError || m_validateDistribution;

                    if (isError && hasRelatedPip)
                    {
                        Logger.Log.DependencyViolationUndeclaredOrderedRead(
                            LoggingContext,
                            violator.SemiStableHash,
                            violator.GetDescription(Context),
                            violator.Provenance.Token.Path.ToString(Context.PathTable),
                            violator.WorkingDirectory.ToString(Context.PathTable),
                            path.ToString(Context.PathTable),
                            related.GetDescription(Context));
                    }

                    break;
                case DependencyViolationType.UndeclaredReadCycle:

                    if (isError && hasRelatedPip)
                    {
                        Logger.Log.DependencyViolationUndeclaredReadCycle(
                            LoggingContext,
                            violator.SemiStableHash,
                            violator.GetDescription(Context),
                            path.ToString(Context.PathTable),
                            related.GetDescription(Context));
                    }

                    break;
                case DependencyViolationType.ReadUndeclaredOutput:
                    isError = isError || m_validateDistribution;

                    if (isError && hasRelatedPip)
                    {
                        Logger.Log.DependencyViolationReadUndeclaredOutput(
                            LoggingContext,
                            violator.SemiStableHash,
                            violator.GetDescription(Context),
                            violator.Provenance.Token.Path.ToString(Context.PathTable),
                            violator.WorkingDirectory.ToString(Context.PathTable),
                            path.ToString(Context.PathTable),
                            related.GetDescription(Context));
                    }

                    break;
                case DependencyViolationType.WriteInSourceSealDirectory:

                    if (isError && hasRelatedPip)
                    {
                        Logger.Log.DependencyViolationWriteInSourceSealDirectory(
                            LoggingContext,
                            violator.SemiStableHash,
                            violator.GetDescription(Context),
                            violator.Provenance.Token.Path.ToString(Context.PathTable),
                            violator.WorkingDirectory.ToString(Context.PathTable),
                            path.ToString(Context.PathTable),
                            related.GetDescription(Context));
                    }

                    break;
                case DependencyViolationType.WriteToTempPathInsideSharedOpaque:

                    if (isError && hasRelatedPip)
                    {
                        Location shareOpaqueLocation = violator.Provenance.Token.ToLogLocation(Context.PathTable);

                        Logger.Log.DependencyViolationSharedOpaqueWriteInTempDirectory(
                            LoggingContext,
                            violator.SemiStableHash,
                            violator.GetDescription(Context),
                            path.ToString(Context.PathTable),
                            shareOpaqueLocation,
                            related.GetDescription(Context),
                            processPath.ToString(Context.PathTable));
                    }

                    break;
                case DependencyViolationType.WriteInUndeclaredSourceRead:

                    if (isError && hasRelatedPip)
                    {
                        Logger.Log.DependencyViolationWriteInUndeclaredSourceRead(
                            LoggingContext,
                            violator.SemiStableHash,
                            violator.GetDescription(Context),
                            violator.Provenance.Token.Path.ToString(Context.PathTable),
                            violator.WorkingDirectory.ToString(Context.PathTable),
                            path.ToString(Context.PathTable),
                            related.GetDescription(Context));
                    }

                    break;
                case DependencyViolationType.WriteOnAbsentPathProbe:

                    if (isError && hasRelatedPip)
                    {
                        Logger.Log.DependencyViolationWriteOnAbsentPathProbe(
                            LoggingContext,
                            violator.SemiStableHash,
                            violator.GetDescription(Context),
                            violator.Provenance.Token.Path.ToString(Context.PathTable),
                            violator.WorkingDirectory.ToString(Context.PathTable),
                            path.ToString(Context.PathTable),
                            related.GetDescription(Context));
                    }

                    break;
                case DependencyViolationType.AbsentPathProbeUnderUndeclaredOpaque:

                    if (isError)
                    {
                        Logger.Log.DependencyViolationAbsentPathProbeInsideUndeclaredOpaqueDirectory(
                            LoggingContext,
                            violator.SemiStableHash,
                            violator.GetDescription(Context),
                            path.ToString(Context.PathTable));
                    }

                    break;
                case DependencyViolationType.WriteInExistingFile:

                    if (isError)
                    {
                        Logger.Log.DependencyViolationWriteOnExistingFile(
                            LoggingContext,
                            violator.SemiStableHash,
                            violator.GetDescription(Context),
                            violator.Provenance.Token.Path.ToString(Context.PathTable),
                            violator.WorkingDirectory.ToString(Context.PathTable),
                            path.ToString(Context.PathTable));
                    }

                    break;
                case DependencyViolationType.TempFileProducedByIndependentPips:
                    isError = isError || m_validateDistribution;

                    if (isError && hasRelatedPip)
                    {
                        Logger.Log.DependencyViolationTheSameTempFileProducedByIndependentPips(
                            LoggingContext,
                            violator.SemiStableHash,
                            violator.GetDescription(Context),
                            path.ToString(Context.PathTable),
                            related.GetDescription(Context));
                    }

                    break;
                default:

                    if (isError)
                    {
                        if (hasRelatedPip)
                        {
                            Logger.Log.DependencyViolationGenericWithRelatedPip(
                                LoggingContext,
                                violator.SemiStableHash,
                                violator.GetDescription(Context),
                                violationType.ToString("G"),
                                accessLevel.ToString("G"),
                                path.ToString(Context.PathTable),
                                related.GetDescription(Context));
                        }
                        else
                        {
                            Logger.Log.DependencyViolationGeneric(
                                LoggingContext,
                                violator.SemiStableHash,
                                violator.GetDescription(Context),
                                violationType.ToString("G"),
                                accessLevel.ToString("G"),
                                path.ToString(Context.PathTable));
                        }
                    }

                    break;
            }

            // If the unsafe flag that turns violations errors into warnings 
            // is enabled, we also want to log those.
            if (isError || !m_unexpectedFileAccessesAsErrors)
            {
                m_executionLog?.DependencyViolationReported(new DependencyViolationEventData
                {
                    ViolatorPipId = violator.PipId,
                    RelatedPipId = related != null ? related.PipId : PipId.Invalid,
                    ViolationType = violationType,
                    AccessLevel = accessLevel,
                    Path = path,
                });
            }

            return ReportViolation(violationType, path, violator, related, processPath, isError);
        }

        private ReportedViolation ReportViolation(DependencyViolationType violationType, AbsolutePath path, Process violator, Pip related, AbsolutePath processPath, bool isError)
        {
            // Double write violations deserve special treatment since there is a policy controlling if those should be treated as warnings vs errors,
            // plus the violation may not make the pip uncacheable if running under containers
            bool violationMakesPipUncacheable;
            if (violationType == DependencyViolationType.DoubleWrite)
            {
                // In case of a double write, if the the violation path got redirected, then the pip is still cacheable. It is not otherwise.
                violationMakesPipUncacheable = !IsOutputPathRedirected(violator, path);
                // If the double write policy doesn't make the double write an error (on both pips), then the overall reported violation is not an error
                if (violator.RewritePolicy.ImpliesDoubleWriteIsWarning() && (!(related.PipType == PipType.Process) || ((Process) related).RewritePolicy.ImpliesDoubleWriteIsWarning()))
                {
                    isError = false;
                }
            }
            else
            {
                // All other violations make the pip uncacheable
                violationMakesPipUncacheable = true;
                // if unexpected file accesses are not treated as errors, then the whole violation is not an error
                if (!m_unexpectedFileAccessesAsErrors)
                {
                    isError = false;
                }
            }

            return new ReportedViolation(isError, violationType, path, violator.PipId, related?.PipId, processPath, violationMakesPipUncacheable);
        }

        private bool IsOutputPathRedirected(Process violator, AbsolutePath outputPath)
        {
            // If the process didn't run in a container, the path is not redirected
            if (!violator.NeedsToRunInContainer)
            {
                return false;
            }

            // If all outputs are isolated, and since we know outputPath is an output, then the provided path is redirected and
            // we don't need to inspect the value of the path
            if (violator.ContainerIsolationLevel.IsolateAllOutputs())
            {
                return true;
            }

            // At this point we need to check if outputPath is a declared file or part of an output directory and compare that with the isolation level
            if (m_fileContentManager.TryGetContainingOutputDirectory(outputPath, out DirectoryArtifact containingOutputDirectory))
            {
                // The path is part of an opaque directory
                return containingOutputDirectory.IsSharedOpaque ? 
                    violator.ContainerIsolationLevel.IsolateSharedOpaqueOutputDirectories() :
                    violator.ContainerIsolationLevel.IsolateExclusiveOpaqueOutputDirectories();
            }

            // The path is a declared output file, so the path is redirected if output files were redirected
            return violator.ContainerIsolationLevel.IsolateOutputFiles();
        }

        private struct UndeclaredAccessors
        {
            public PipId Writer;
            public List<PipId> Readers;
        }
    }
}
