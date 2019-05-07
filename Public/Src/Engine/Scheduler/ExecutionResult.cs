// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Pips;
using BuildXL.Processes;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;

#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Defines the result of execution or cache lookup of a process pip.
    /// TODO: Capture and serialize/deserialize file access violations.
    /// </summary>
    public sealed class ExecutionResult
    {
        private UnsealedState m_unsealedState;

        private UnsealedState InnerUnsealedState
        {
            get
            {
                EnsureUnsealed();
                return m_unsealedState ?? (m_unsealedState = new UnsealedState());
            }
        }

        private PipResultStatus m_result;
        private IReadOnlyList<ReportedFileAccess> m_fileAccessViolationsNotWhitelisted;
        private IReadOnlyList<ReportedFileAccess> m_whitelistedFileAccessViolations;
        private int m_numberOfWarnings;
        private ProcessPipExecutionPerformance m_performanceInformation;
        private WeakContentFingerprint? m_weakFingerprint;
        private TwoPhaseCachingInfo m_twoPhaseCachingInfo;
        private ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)> m_outputContent;
        private ReadOnlyArray<(DirectoryArtifact, ReadOnlyArray<FileArtifact>)> m_directoryOutputs;
        private bool m_mustBeConsideredPerpetuallyDirty;
        private bool m_converged;
        private ReadOnlyArray<AbsolutePath> m_dynamicallyObservedFiles;
        private ReadOnlyArray<AbsolutePath> m_dynamicallyObservedEnumerations;
        private IReadOnlySet<AbsolutePath> m_allowedUndeclaredSourceReads;
        private IReadOnlySet<AbsolutePath> m_absentPathProbesUnderOutputDirectories;
        private PipCacheDescriptorV2Metadata m_pipCacheDescriptorV2Metadata;
        private CacheLookupPerfInfo m_cacheLookupPerfInfo;

        public CacheLookupPerfInfo CacheLookupPerfInfo
        {
            get
            {
                EnsureSealed();
                return m_cacheLookupPerfInfo;
            }

            set
            {
                EnsureUnsealed();
                m_cacheLookupPerfInfo = value;
            }
        }

        private ObservedPathSet? m_pathSet;

        /// <summary>
        /// Gets the pip result for the process execution
        /// </summary>
        public PipResultStatus Result
        {
            get
            {
                EnsureSealed();
                return m_result;
            }
        }

        /// <summary>
        /// Gets observed ownership for shared dynamic directories
        /// </summary>
        public IReadOnlyDictionary<AbsolutePath, IReadOnlyCollection<AbsolutePath>> SharedDynamicDirectoryWriteAccesses { get; private set; }

        /// <summary>
        /// Observed allowed undeclared source reads
        /// </summary>
        public IReadOnlySet<AbsolutePath> AllowedUndeclaredReads
        {
            get
            {
                EnsureSealed();
                return m_allowedUndeclaredSourceReads;
            }

            set
            {
                EnsureUnsealed();
                InnerUnsealedState.AllowedUndeclaredSourceReads = value;
            }
        }

        /// <summary>
        /// Observed absent path probes under output directory roots
        /// </summary>
        public IReadOnlySet<AbsolutePath> AbsentPathProbesUnderOutputDirectories
        {
            get
            {
                EnsureSealed();
                return m_absentPathProbesUnderOutputDirectories;
            }

            set
            {
                EnsureUnsealed();
                InnerUnsealedState.AbsentPathProbesUnderOutputDirectories = value;
            }
        }

        /// <summary>
        /// Indicates whether the cache entry was converged when storing cache entry to cache
        /// </summary>
        public bool Converged
        {
            get
            {
                return m_converged;
            }

            set
            {
                EnsureUnsealed();
                m_converged = value;
            }
        }

        /// <summary>
        /// Sets the pip result for the process execution. An error must be logged before calling this method
        /// </summary>
        public void SetResult(LoggingContext context, PipResultStatus status)
        {
            if (status == PipResultStatus.Failed)
            {
                Contract.Assert(context.ErrorWasLogged, "Set a failed status without logging an error");
            }

            EnsureUnsealed();
            InnerUnsealedState.Result = status;
        }

        /// <summary>
        /// Gets the collection of unexpected file accesses reported so far that were not whitelisted. These are 'violations'.
        /// </summary>
        public IReadOnlyList<ReportedFileAccess> FileAccessViolationsNotWhitelisted
        {
            get
            {
                EnsureSealed();
                return m_fileAccessViolationsNotWhitelisted;
            }

            set
            {
                EnsureUnsealed();
                InnerUnsealedState.FileAccessViolationsNotWhitelisted = new Optional<IReadOnlyList<ReportedFileAccess>>(value);
            }
        }

        /// <summary>
        /// Gets the collection of unexpected file accesses reported so far that were whitelisted.
        /// </summary>
        public IReadOnlyList<ReportedFileAccess> WhitelistedFileAccessViolations
        {
            get
            {
                EnsureSealed();
                return m_whitelistedFileAccessViolations;
            }

            set
            {
                EnsureUnsealed();
                InnerUnsealedState.WhitelistedFileAccessViolations = new Optional<IReadOnlyList<ReportedFileAccess>>(value);
            }
        }

        /// <summary>
        /// Gets the pip cache fingerprint. Only used for cache lookup miss result and logging.
        /// </summary>
        public WeakContentFingerprint? WeakFingerprint
        {
            get
            {
                EnsureSealed();
                return m_weakFingerprint;
            }

            set
            {
                EnsureUnsealed();
                m_weakFingerprint = value;
            }
        }

        /// <summary>
        /// Gets the path set
        /// </summary>
        public ObservedPathSet? PathSet
        {
            get
            {
                EnsureSealed();
                return m_pathSet;
            }

            set
            {
                EnsureUnsealed();
                m_pathSet = value;
            }
        }

        /// <summary>
        /// Gets the two-phase caching info
        /// </summary>
        public TwoPhaseCachingInfo TwoPhaseCachingInfo
        {
            get
            {
                EnsureSealed();
                return m_twoPhaseCachingInfo;
            }

            set
            {
                EnsureUnsealed();
                m_twoPhaseCachingInfo = value;
            }
        }

        /// <summary>
        /// Gets the pip cache descriptor metadata
        /// </summary>
        public PipCacheDescriptorV2Metadata PipCacheDescriptorV2Metadata
        {
            get
            {
                EnsureSealed();
                return m_pipCacheDescriptorV2Metadata;
            }

            set
            {
                EnsureUnsealed();
                m_pipCacheDescriptorV2Metadata = value;
            }
        }

        #region Reported State

        /// <summary>
        /// Number of warnings raised by the process during execution
        /// </summary>
        public int NumberOfWarnings
        {
            get
            {
                EnsureSealed();
                return m_numberOfWarnings;
            }
        }

        /// <summary>
        /// Performance information for the pip execution
        /// </summary>
        public ProcessPipExecutionPerformance PerformanceInformation
        {
            get
            {
                EnsureSealed();
                return m_performanceInformation;
            }
        }

        /// <summary>
        /// Output content of the pip
        /// </summary>
        public ReadOnlyArray<(FileArtifact fileArtifact, FileMaterializationInfo fileInfo, PipOutputOrigin pipOutputOrigin)> OutputContent
        {
            get
            {
                EnsureSealed();
                return m_outputContent;
            }
        }

        /// <nodoc />
        public bool MustBeConsideredPerpetuallyDirty
        {
            get
            {
                EnsureSealed();
                return m_mustBeConsideredPerpetuallyDirty;
            }

            set
            {
                EnsureUnsealed();
                InnerUnsealedState.MustBeConsideredPerpetuallyDirty |= value;
            }
        }

        /// <nodoc />
        public ReadOnlyArray<AbsolutePath> DynamicallyObservedFiles
        {
            get
            {
                EnsureSealed();
                return m_dynamicallyObservedFiles;
            }

            set
            {
                EnsureUnsealed();
                InnerUnsealedState.DynamicallyObservedFiles = value;
            }
        }

        /// <nodoc />
        public ReadOnlyArray<AbsolutePath> DynamicallyObservedEnumerations
        {
            get
            {
                EnsureSealed();
                return m_dynamicallyObservedEnumerations;
            }

            set
            {
                EnsureUnsealed();
                InnerUnsealedState.DynamicallyObservedEnumerations = value;
            }
        }

        /// <summary>
        /// Directory outputs.
        /// </summary>
        public ReadOnlyArray<(DirectoryArtifact directoryArtifact, ReadOnlyArray<FileArtifact> fileArtifactArray)> DirectoryOutputs
        {
            get
            {
                EnsureSealed();
                return m_directoryOutputs;
            }
        }

        /// <summary>
        /// Whether or not the execution result is sealed
        /// </summary>
        public bool IsSealed { get; private set; }

        #endregion Reported State

        /// <summary>
        /// Creates a new sealed execution result with the given information
        /// </summary>
        public static ExecutionResult CreateSealed(
            PipResultStatus result,
            int numberOfWarnings,
            ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)> outputContent,
            ReadOnlyArray<(DirectoryArtifact, ReadOnlyArray<FileArtifact>)> directoryOutputs,
            ProcessPipExecutionPerformance performanceInformation,
            WeakContentFingerprint? fingerprint,
            IReadOnlyList<ReportedFileAccess> fileAccessViolationsNotWhitelisted,
            IReadOnlyList<ReportedFileAccess> whitelistedFileAccessViolations,
            bool mustBeConsideredPerpetuallyDirty,
            ReadOnlyArray<AbsolutePath> dynamicallyObservedFiles,
            ReadOnlyArray<AbsolutePath> dynamicallyObservedEnumerations,
            IReadOnlySet<AbsolutePath> allowedUndeclaredSourceReads,
            IReadOnlySet<AbsolutePath> absentPathProbesUnderOutputDirectories,
            TwoPhaseCachingInfo twoPhaseCachingInfo,
            PipCacheDescriptorV2Metadata pipCacheDescriptorV2Metadata,
            bool converged,
            ObservedPathSet? pathSet,
            CacheLookupPerfInfo cacheLookupStepDurations)
        {
            var processExecutionResult =
                new ExecutionResult
                {
                    m_result = result,
                    m_numberOfWarnings = numberOfWarnings,
                    m_outputContent = outputContent,
                    m_directoryOutputs = directoryOutputs,
                    SharedDynamicDirectoryWriteAccesses = ComputeSharedDynamicAccessesFrom(directoryOutputs),
                    m_performanceInformation = performanceInformation,
                    m_weakFingerprint = fingerprint,
                    m_fileAccessViolationsNotWhitelisted = fileAccessViolationsNotWhitelisted,
                    m_whitelistedFileAccessViolations = whitelistedFileAccessViolations,
                    m_mustBeConsideredPerpetuallyDirty = mustBeConsideredPerpetuallyDirty,
                    m_dynamicallyObservedFiles = dynamicallyObservedFiles,
                    m_dynamicallyObservedEnumerations = dynamicallyObservedEnumerations,
                    m_allowedUndeclaredSourceReads = allowedUndeclaredSourceReads,
                    m_absentPathProbesUnderOutputDirectories = absentPathProbesUnderOutputDirectories,
                    m_twoPhaseCachingInfo = twoPhaseCachingInfo,
                    m_pipCacheDescriptorV2Metadata = pipCacheDescriptorV2Metadata,
                    Converged = converged,
                    IsSealed = true,
                    m_pathSet = pathSet,
                    m_cacheLookupPerfInfo = cacheLookupStepDurations
                };
            return processExecutionResult;
        }

        /// <summary>
        /// Creates a sealed result from another sealed result, altering the status.
        /// </summary>
        /// <param name="convergedCacheResult">The result containing the cache hit information (namely output content and observations).</param>
        /// <returns>A new sealed result with output content and result status from the converged result.</returns>
        /// <remarks>
        /// <paramref name="convergedCacheResult"/> contains only empty dynamically observed observations like dynamically observed files and
        /// dynamically observed enumerations. These observations are needed by incremental scheduling. Given that convergence means cache hit 
        /// based on the result of execution, the dynamic observations from the execution can be used as the converged result.
        /// </remarks>
        public ExecutionResult CreateSealedConvergedExecutionResult(ExecutionResult convergedCacheResult)
        {
            Contract.Requires(convergedCacheResult.Converged);
            EnsureSealed();

            return CreateSealed(
                convergedCacheResult.Result,
                NumberOfWarnings,
                convergedCacheResult.OutputContent,
                convergedCacheResult.DirectoryOutputs,
                PerformanceInformation,
                WeakFingerprint,
                FileAccessViolationsNotWhitelisted,
                WhitelistedFileAccessViolations,
                convergedCacheResult.MustBeConsideredPerpetuallyDirty,
                // Converged result does not have values for the following dynamic observations. Use the observations from this result.
                DynamicallyObservedFiles,
                DynamicallyObservedEnumerations,
                AllowedUndeclaredReads,
                AbsentPathProbesUnderOutputDirectories,
                TwoPhaseCachingInfo,
                PipCacheDescriptorV2Metadata,
                converged: true,
                pathSet: convergedCacheResult.PathSet,
                cacheLookupStepDurations: convergedCacheResult.m_cacheLookupPerfInfo);
        }

        /// <summary>
        /// Creates a sealed result from another sealed result, altering the status.
        /// </summary>
        /// <param name="result">The new status to be set in the new sealed result.</param>
        /// <returns>A new sealed result with replaced status field.</returns>
        public ExecutionResult CloneSealedWithResult(PipResultStatus result)
        {
            EnsureSealed();
            return CreateSealed(
                result,
                NumberOfWarnings,
                OutputContent,
                DirectoryOutputs,
                PerformanceInformation,
                WeakFingerprint,
                FileAccessViolationsNotWhitelisted,
                WhitelistedFileAccessViolations,
                MustBeConsideredPerpetuallyDirty,
                DynamicallyObservedFiles,
                DynamicallyObservedEnumerations,
                AllowedUndeclaredReads,
                AbsentPathProbesUnderOutputDirectories,
                TwoPhaseCachingInfo,
                PipCacheDescriptorV2Metadata,
                Converged,
                PathSet,
                CacheLookupPerfInfo);
        }

        /// <summary>
        /// Records the hash of an output of the pip. All static outputs must be reported, even those that were already up-to-date.
        /// </summary>
        public void ReportOutputContent(FileArtifact artifact, in FileMaterializationInfo hash, PipOutputOrigin origin)
        {
            EnsureUnsealed();
            InnerUnsealedState.OutputContent.Add((artifact, hash, origin));
        }

        /// <summary>
        /// Record the result of running process in the sandbox
        /// </summary>
        public void ReportSandboxedExecutionResult(SandboxedProcessPipExecutionResult executionResult)
        {
            EnsureUnsealed();
            m_numberOfWarnings = executionResult.NumberOfWarnings;
            InnerUnsealedState.ExecutionResult = executionResult;
            SharedDynamicDirectoryWriteAccesses = executionResult.SharedDynamicDirectoryWriteAccesses;
        }

        /// <summary>
        /// Records the start and stop time for a pip.
        /// </summary>
        public void ReportExecutionSpan(DateTime executionStart, DateTime executionStop)
        {
            Contract.Requires(executionStart.Kind == DateTimeKind.Utc);
            Contract.Requires(executionStop.Kind == DateTimeKind.Utc);
            InnerUnsealedState.ExecutionStart = executionStart;
            InnerUnsealedState.ExecutionStop = executionStop;
        }

        /// <summary>
        /// Record unexpected file access counters
        /// </summary>
        public void ReportUnexpectedFileAccesses(UnexpectedFileAccessCounters unexpectedFileAccessCounters)
        {
            InnerUnsealedState.UnexpectedFileAccessCounters = unexpectedFileAccessCounters;
        }

        /// <summary>
        /// Records the output directory along with its contents as strings.
        /// </summary>
        public void ReportDirectoryOutput(DirectoryArtifact directoryArtifact, IReadOnlyList<FileArtifact> contents)
        {
            EnsureUnsealed();
            InnerUnsealedState.DirectoryOutputs.Add((directoryArtifact, ReadOnlyArray<FileArtifact>.From(contents)));
        }

        private void EnsureSealed()
        {
            Contract.Assert(IsSealed, "Must be sealed to retrieve state");
        }

        private void EnsureUnsealed()
        {
            Contract.Assert(!IsSealed, "Cannot be modified after sealing");
        }

        /// <summary>
        /// Gets a failure result without run information. An error must be logged before calling this method.
        /// </summary>
        public static ExecutionResult GetFailureNotRunResult(LoggingContext loggingContext)
        {
            var result = new ExecutionResult();
            result.SetResult(loggingContext, PipResultStatus.Failed);
            result.Seal();

            return result;
        }

        /// <summary>
        /// Disallow further modifications, finalize state, and allow reading state
        /// </summary>
        public void Seal()
        {
            if (!IsSealed)
            {
                Contract.Assert(InnerUnsealedState.Result.HasValue, "Result must be set.");

                if (m_unsealedState != null)
                {
                    m_result = m_unsealedState.Result.Value;
                    m_outputContent = ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.From(m_unsealedState.OutputContent);
                    m_directoryOutputs = ReadOnlyArray<(DirectoryArtifact, ReadOnlyArray<FileArtifact>)>.From(m_unsealedState.DirectoryOutputs);
                    
                    // If the result from the sandbox was not reported, that means this pip came from the cache, and therefore
                    // the shared dynamic accesses need to be populated from the already reported output directories
                    if (!m_unsealedState.SandboxedResultReported)
                    {
                        SharedDynamicDirectoryWriteAccesses = ComputeSharedDynamicAccessesFrom(m_directoryOutputs);
                    }

                    m_mustBeConsideredPerpetuallyDirty = m_unsealedState.MustBeConsideredPerpetuallyDirty;
                    m_dynamicallyObservedFiles = m_unsealedState.DynamicallyObservedFiles;
                    m_dynamicallyObservedEnumerations = m_unsealedState.DynamicallyObservedEnumerations;
                    m_allowedUndeclaredSourceReads = m_unsealedState.AllowedUndeclaredSourceReads;
                    m_absentPathProbesUnderOutputDirectories = m_unsealedState.AbsentPathProbesUnderOutputDirectories;

                    SandboxedProcessPipExecutionResult processResult = m_unsealedState.ExecutionResult;
                    if (processResult != null && processResult.Status != SandboxedProcessPipExecutionStatus.PreparationFailed)
                    {
                        if (!(processResult.Status == SandboxedProcessPipExecutionStatus.Succeeded ||
                            processResult.Status == SandboxedProcessPipExecutionStatus.ExecutionFailed ||
                            processResult.Status == SandboxedProcessPipExecutionStatus.Canceled ||
                            processResult.Status == SandboxedProcessPipExecutionStatus.FileAccessMonitoringFailed ||
                            processResult.Status == SandboxedProcessPipExecutionStatus.OutputWithNoFileAccessFailed ||
                            processResult.Status == SandboxedProcessPipExecutionStatus.MismatchedMessageCount))
                        {
                            Contract.Assert(false, "Invalid execution status: " + processResult.Status);
                        }
                            
                        Contract.Assert(
                            processResult.PrimaryProcessTimes != null,
                            "Execution counters are available when the status is not PreparationFailed");
                        Contract.Assert(
                            m_unsealedState.UnexpectedFileAccessCounters.HasValue,
                            "File access counters are available when the status is not PreparationFailed");
                        Contract.Assert(
                            m_unsealedState.FileAccessViolationsNotWhitelisted.IsValid,
                            "File access violations not set when the status is not PreparationFailed");

                        TimeSpan wallClockTime = (TimeSpan)processResult.PrimaryProcessTimes?.TotalWallClockTime;
                        JobObject.AccountingInformation jobAccounting = processResult.JobAccountingInformation ??
                                                                        default(JobObject.AccountingInformation);
                        m_fileAccessViolationsNotWhitelisted = m_unsealedState.FileAccessViolationsNotWhitelisted.Value;
                        m_whitelistedFileAccessViolations = m_unsealedState.WhitelistedFileAccessViolations.Value;

                        m_performanceInformation = new ProcessPipExecutionPerformance(
                            m_result.ToExecutionLevel(),
                            m_unsealedState.ExecutionStart,
                            m_unsealedState.ExecutionStop,
                            fingerprint: m_weakFingerprint?.Hash ?? FingerprintUtilities.ZeroFingerprint,
                            processExecutionTime: wallClockTime,
                            fileMonitoringViolations: ConvertFileMonitoringViolationCounters(m_unsealedState.UnexpectedFileAccessCounters.Value),
                            ioCounters: jobAccounting.IO,
                            userTime: jobAccounting.UserTime,
                            kernelTime: jobAccounting.KernelTime,
                            peakMemoryUsage: jobAccounting.PeakMemoryUsage,
                            numberOfProcesses: jobAccounting.NumberOfProcesses,
                            workerId: 0);
                    }
                }
                else
                {
                    m_outputContent = ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty;
                    m_directoryOutputs = ReadOnlyArray<(DirectoryArtifact, ReadOnlyArray<FileArtifact>)>.Empty;
                    m_dynamicallyObservedFiles = ReadOnlyArray<AbsolutePath>.Empty;
                    m_dynamicallyObservedEnumerations = ReadOnlyArray<AbsolutePath>.Empty;
                    m_allowedUndeclaredSourceReads = CollectionUtilities.EmptySet<AbsolutePath>();
                    m_absentPathProbesUnderOutputDirectories = CollectionUtilities.EmptySet<AbsolutePath>();
                }

                m_unsealedState = null;
                IsSealed = true;
            }
        }

        private static ReadOnlyDictionary<AbsolutePath, IReadOnlyCollection<AbsolutePath>> ComputeSharedDynamicAccessesFrom(ReadOnlyArray<(DirectoryArtifact, ReadOnlyArray<FileArtifact>)> directoryOutputs)
        {
            var sharedDynamicAccesses = directoryOutputs
                .Where(kvp => kvp.Item1.IsSharedOpaque)
                .ToDictionary(kvp => kvp.Item1.Path, kvp => (IReadOnlyCollection<AbsolutePath>) kvp.Item2.SelectArray(fileArtifact => fileArtifact.Path));

            return new ReadOnlyDictionary<AbsolutePath, IReadOnlyCollection<AbsolutePath>>(sharedDynamicAccesses);
        }

        private static FileMonitoringViolationCounters ConvertFileMonitoringViolationCounters(UnexpectedFileAccessCounters counters)
        {
            return new FileMonitoringViolationCounters(
                numFileAccessViolationsNotWhitelisted: counters.NumFileAccessViolationsNotWhitelisted,
                numFileAccessesWhitelistedAndCacheable: counters.NumFileAccessesWhitelistedAndCacheable,
                numFileAccessesWhitelistedButNotCacheable: counters.NumFileAccessesWhitelistedButNotCacheable);
        }

        private sealed class UnsealedState
        {
            private SandboxedProcessPipExecutionResult m_executionResult;

            public bool SandboxedResultReported { get; private set; }
            public Optional<IReadOnlyList<ReportedFileAccess>> FileAccessViolationsNotWhitelisted;
            public Optional<IReadOnlyList<ReportedFileAccess>> WhitelistedFileAccessViolations;
            public PipResultStatus? Result;
            public SandboxedProcessPipExecutionResult ExecutionResult
            {
                get => m_executionResult;
                set {
                    SandboxedResultReported = true;
                    m_executionResult = value;
                }
            }
            public UnexpectedFileAccessCounters? UnexpectedFileAccessCounters;
            public DateTime ExecutionStart;
            public DateTime ExecutionStop;
            public bool MustBeConsideredPerpetuallyDirty;
            public ReadOnlyArray<AbsolutePath> DynamicallyObservedFiles = ReadOnlyArray<AbsolutePath>.Empty;
            public ReadOnlyArray<AbsolutePath> DynamicallyObservedEnumerations = ReadOnlyArray<AbsolutePath>.Empty;
            public IReadOnlySet<AbsolutePath> AllowedUndeclaredSourceReads = CollectionUtilities.EmptySet<AbsolutePath>();
            public IReadOnlySet<AbsolutePath> AbsentPathProbesUnderOutputDirectories = CollectionUtilities.EmptySet<AbsolutePath>();

            public readonly List<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)> OutputContent =
                new List<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>();

            public readonly List<(DirectoryArtifact, ReadOnlyArray<FileArtifact>)> DirectoryOutputs =
                new List<(DirectoryArtifact, ReadOnlyArray<FileArtifact>)>();

            public UnsealedState()
            {
                ExecutionStart = DateTime.UtcNow;
                ExecutionStop = ExecutionStart;
            }
        }
    }
}
