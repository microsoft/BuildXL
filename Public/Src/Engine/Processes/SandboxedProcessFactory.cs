// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Utilities.Core;

#nullable enable

namespace BuildXL.Processes
{
    /// <summary>
    /// Factory for creating and spawning processes.
    /// 
    /// Currently, if <see cref="FileAccessManifest.DisableDetours"/> is set, an instance of <see cref="UnsandboxedProcess"/>
    /// is returned; otherwise, <see cref="SandboxedProcess"/> is used.
    /// </summary>
    public static class SandboxedProcessFactory
    {
        /// <summary>
        /// Counter types for sandboxed process execution
        /// </summary>
        // TODO: Some of these are ProcessPipExecutor specific and should be moved there instead
        public enum SandboxedProcessCounters
        {
            /// <summary>
            /// Aggregate time spent reporting file accesses (<see cref="SandboxedProcessReports.ReportLineReceived"/>, <see cref="SandboxedProcessReports.ReportFileAccess"/>)
            /// </summary>
            [CounterType(CounterType.Stopwatch)]
            HandleAccessReportDuration,

            /// <summary>
            /// Total number of access reports received from the sandbox.
            /// </summary>
            [CounterType(CounterType.Numeric)]
            AccessReportCount,

            /// <summary>
            /// Number of access reports whose reported path exactly matched the manifest path.
            /// </summary>
            [CounterType(CounterType.Numeric)]
            AccessReportExactPathCount,

            /// <summary>
            /// Number of non-exact access report paths reused from a previous report for the same pip.
            /// </summary>
            [CounterType(CounterType.Numeric)]
            AccessReportPathCacheHitCount,

            /// <summary>
            /// Number of unique non-exact access report paths retained for a pip.
            /// </summary>
            [CounterType(CounterType.Numeric)]
            AccessReportPathCacheMissCount,

            /// <summary>
            /// Total number of executed sandboxed processes.
            /// </summary>
            [CounterType(CounterType.Numeric)]
            SandboxedProcessCount,

            /// <summary>
            /// Total life time of all sandboxed processes in milliseconds.
            /// </summary>
            [CounterType(CounterType.Numeric)]
            SandboxedProcessLifeTimeMs,

            /// <summary>
            /// Aggregate time spent checking paths for directory symlinks
            /// </summary>
            [CounterType(CounterType.Stopwatch)]
            DirectorySymlinkCheckingDuration,

            /// <summary>
            /// Number of paths queried for directory symlinks
            /// </summary>
            [CounterType(CounterType.Numeric)]
            DirectorySymlinkPathsQueriedCount,

            /// <summary>
            /// Number of paths checked for directory symlinks (cache misses)
            /// </summary>
            [CounterType(CounterType.Numeric)]
            DirectorySymlinkPathsCheckedCount,

            /// <summary>
            /// Duration of lazily deleting shared opaque outputs (when enabled)
            /// </summary>
            [CounterType(CounterType.Stopwatch)]
            SandboxedPipExecutorPhaseDeletingSharedOpaqueOutputs,

            /// <summary>
            /// Duration of (see "SandboxedProcessPipExecutor.ProcessSandboxedProcessResultAsync").
            /// </summary>
            [CounterType(CounterType.Stopwatch)]
            SandboxedPipExecutorPhaseProcessingSandboxProcessResult,

            /// <summary>
            /// Duration of processing process's standard outputs
            /// </summary>
            [CounterType(CounterType.Stopwatch)]
            SandboxedPipExecutorPhaseProcessingStandardOutputs,

            /// <summary>
            /// Duration of (see "SandboxedProcessPipExecutor.TryGetObservedFileAccesses")
            /// </summary>
            [CounterType(CounterType.Stopwatch)]
            SandboxedPipExecutorPhaseGettingObservedFileAccesses,

            /// <summary>
            /// Duration of logging process's outputs
            /// </summary>
            [CounterType(CounterType.Stopwatch)]
            SandboxedPipExecutorPhaseLoggingOutputs,

            /// <summary>
            /// Duration of (see "SandboxedProcessPipExecutor.RunAsync") inside of PipExecutor
            /// </summary>
            [CounterType(CounterType.Stopwatch)]
            PipExecutorPhaseRunningPip,

            /// <summary>
            /// Duration of "ExecutionResult.ReportSandboxedExecutionResult" inside of PipExecutor
            /// </summary>
            [CounterType(CounterType.Stopwatch)]
            PipExecutorPhaseReportingExeResult,

            /// <summary>
            /// Duration of PipExecutor.ValidateObservedFileAccesses
            /// </summary>
            [CounterType(CounterType.Stopwatch)]
            PipExecutorPhaseValidateObservedFileAccesses,

            /// <summary>
            /// Duration of computing IsDirty inside of PipExecutor.
            /// </summary>
            [CounterType(CounterType.Stopwatch)]
            PipExecutorPhaseComputingIsDirty,

            /// <summary>
            /// Duration of storing cache content inside of PipExecutor.
            /// </summary>
            [CounterType(CounterType.Stopwatch)]
            PipExecutorPhaseStoringCacheContent,

            /// <summary>
            /// Duration of computing strong fingerprint inside of PipExecutor
            /// </summary>
            [CounterType(CounterType.Stopwatch)]
            PipExecutorPhaseComputingStrongFingerprint,

            /// <summary>
            /// Duration of storing strong fingerprint to XLG inside of PipExecutor
            /// </summary>
            [CounterType(CounterType.Stopwatch)]
            PipExecutorPhaseStoringStrongFingerprintToXlg,

            /// <summary>
            /// Duration of flagging shared opaque outputs inside of Scheduler 
            /// </summary>
            [CounterType(CounterType.Stopwatch)]
            SchedulerPhaseFlaggingSharedOpaqueOutputs,

            /// <summary>
            /// Duration of analyzing file access violations inside of Scheduler
            /// </summary>
            [CounterType(CounterType.Stopwatch)]
            SchedulerPhaseAnalyzingFileAccessViolations,

            /// <summary>
            /// Duration of analyzing double writes inside of Scheduler
            /// </summary>
            [CounterType(CounterType.Stopwatch)]
            SchedulerPhaseAnalyzingDoubleWrites,

            /// <summary>
            /// Duration of reporting output content inside of Scheduler
            /// </summary>
            [CounterType(CounterType.Stopwatch)]
            SchedulerPhaseReportingOutputContent,

            /// <summary>
            /// Duration of initializing remoting process manager.
            /// </summary>
            [CounterType(CounterType.Stopwatch)]
            SandboxedPipExecutorInitializingRemoteProcessManager,

            /// <nodoc/>
            [CounterType(CounterType.Stopwatch)]
            PrepareOutputsDuration,

            /// <nodoc/>
            [CounterType(CounterType.Stopwatch)]
            PrepareDirectoryOutputsDuration,

            /// <summary>
            /// Duration of subset of (see "SandboxedProcessPipExecutor.TryGetObservedFileAccesses")
            /// </summary>
            [CounterType(CounterType.Stopwatch)]
            SandboxedPipExecutorPhaseInjectedAccesses,

            /// <summary>
            /// Duration of subset of (see "SandboxedProcessPipExecutor.TryGetObservedFileAccesses")
            /// </summary>
            [CounterType(CounterType.Stopwatch)]
            SandboxedPipExecutorPhaseGettingObservedProcessOutputs,

            /// <summary>
            /// Duration of subset of (see "SandboxedProcessPipExecutor.TryGetObservedFileAccesses")
            /// </summary>
            [CounterType(CounterType.Stopwatch)]
            SandboxedPipExecutorPhaseResolveAbsentAccesses,

            /// <summary>
            /// Duration of subset of (see "SandboxedProcessPipExecutor.TryGetObservedFileAccesses")
            /// </summary>
            [CounterType(CounterType.Stopwatch)]
            SandboxedPipExecutorPhaseBuildObservedFileAccesses,

            /// <summary>
            /// Number of sealed file access manifest path indexes that were created.
            /// </summary>
            [CounterType(CounterType.Numeric)]
            FileAccessManifestPathIndexCreatedCount,

            /// <summary>
            /// Number of sealed file access manifest path indexes that could not be constructed.
            /// </summary>
            [CounterType(CounterType.Numeric)]
            FileAccessManifestPathIndexConstructionFailureCount,

            /// <summary>
            /// Total bytes allocated for sealed file access manifest path indexes.
            /// </summary>
            [CounterType(CounterType.Numeric)]
            FileAccessManifestPathIndexBytes,

            /// <summary>
            /// Total paths stored in sealed file access manifest path indexes.
            /// </summary>
            [CounterType(CounterType.Numeric)]
            FileAccessManifestPathIndexPathCount,

            /// <summary>
            /// Total slots allocated in sealed file access manifest path indexes.
            /// </summary>
            [CounterType(CounterType.Numeric)]
            FileAccessManifestPathIndexSlotCount,

            /// <summary>
            /// Aggregate time spent constructing sealed file access manifest path indexes.
            /// </summary>
            [CounterType(CounterType.Stopwatch)]
            FileAccessManifestPathIndexConstructionDuration,

            /// <summary>
            /// Number of sealed manifest path lookups performed by traversing serialized path fragments.
            /// </summary>
            [CounterType(CounterType.Numeric)]
            FileAccessManifestPathIndexFallbackLookupCount,

            /// <summary>
            /// Number of sealed manifest path index promotions initiated after enough fallback lookups.
            /// </summary>
            [CounterType(CounterType.Numeric)]
            FileAccessManifestPathIndexPromotionCount,

            /// <summary>
            /// Number of file access manifest instances created or deserialized in sealed form.
            /// </summary>
            [CounterType(CounterType.Numeric)]
            FileAccessManifestSealedCount,

            /// <summary>
            /// Number of sealed file access manifests that reached at least one path lookup.
            /// </summary>
            [CounterType(CounterType.Numeric)]
            FileAccessManifestSealedLookupReached1Count,

            /// <summary>
            /// Number of sealed file access manifests that reached at least 1,024 path lookups.
            /// </summary>
            [CounterType(CounterType.Numeric)]
            FileAccessManifestSealedLookupReached1024Count,

            /// <summary>
            /// Number of sealed file access manifests that reached at least 4,096 path lookups.
            /// </summary>
            [CounterType(CounterType.Numeric)]
            FileAccessManifestSealedLookupReached4096Count,

            /// <summary>
            /// Number of sealed file access manifests that reached at least 16,384 path lookups.
            /// </summary>
            [CounterType(CounterType.Numeric)]
            FileAccessManifestSealedLookupReached16384Count,

            /// <summary>
            /// Number of sealed file access manifests that reached at least 65,536 path lookups.
            /// </summary>
            [CounterType(CounterType.Numeric)]
            FileAccessManifestSealedLookupReached65536Count,

            /// <summary>
            /// Number of sealed file access manifests that reached at least 262,144 path lookups.
            /// </summary>
            [CounterType(CounterType.Numeric)]
            FileAccessManifestSealedLookupReached262144Count,

            /// <summary>
            /// Number of serialized-fragment lookups performed while another thread constructed the path index.
            /// </summary>
            [CounterType(CounterType.Numeric)]
            FileAccessManifestPathIndexFallbackDuringConstructionCount,

            /// <summary>
            /// Number of created sealed path indexes containing at most 16,384 paths.
            /// </summary>
            [CounterType(CounterType.Numeric)]
            FileAccessManifestPathIndexSize16384OrLessCount,

            /// <summary>
            /// Number of created sealed path indexes containing between 16,385 and 65,536 paths.
            /// </summary>
            [CounterType(CounterType.Numeric)]
            FileAccessManifestPathIndexSize16385To65536Count,

            /// <summary>
            /// Number of created sealed path indexes containing between 65,537 and 262,144 paths.
            /// </summary>
            [CounterType(CounterType.Numeric)]
            FileAccessManifestPathIndexSize65537To262144Count,

            /// <summary>
            /// Number of created sealed path indexes containing more than 262,144 paths.
            /// </summary>
            [CounterType(CounterType.Numeric)]
            FileAccessManifestPathIndexSizeGreaterThan262144Count,
        }

        /// <summary>
        /// Counters for sandboxed process execution.
        /// </summary>
        public static readonly CounterCollection<SandboxedProcessCounters> Counters = new ();

        /// <summary>
        /// Start a sandboxed process asynchronously. The result will only be available once the process terminates.
        /// </summary>
        /// <exception cref="BuildXLException">
        /// Thrown if the process creation fails in a recoverable manner due do some obscure problem detected by the underlying
        /// ProcessCreate call.
        /// </exception>
        public static Task<ISandboxedProcess> StartAsync(SandboxedProcessInfo info, bool forceSandboxing)
        {
            string cmdLine = info.GetCommandLine();
            if (cmdLine.Length > SandboxedProcessInfo.MaxCommandLineLength)
            {
                Contract.Requires(
                    cmdLine.Length <= SandboxedProcessInfo.MaxCommandLineLength,
                    $"Command line's length ({cmdLine.Length}) exceeds the max length {SandboxedProcessInfo.MaxCommandLineLength}");
            }

            if (info.TestRetries)
            {
                throw new BuildXLException("Test Retries exception.", new System.ComponentModel.Win32Exception(NativeIOConstants.ErrorPartialCopy));
            }

            // Process creation is expensive and involves a fair amount of I/O.
            // TODO: This should be scheduled on a separate I/O pool with plenty of threads.
            return Task.Factory.StartNew(ProcessStart, Tuple.Create(info, forceSandboxing));
        }

        /// <summary>
        /// Creates an instance of <see cref="ISandboxedProcess"/> based on the configuration parameters:
        ///     - if <see cref="SandboxedProcessInfo.SandboxKind"/> is <see cref="SandboxKind.None"/> and <paramref name="forceSandboxing"/> is false: creates 
        ///       an instance with sandboxing completely disabled.
        ///     - else: creates an instance that supports sandboxing.
        /// </summary>
        // TODO: move this to BuildXL.Native.Processes
        private static ISandboxedProcess Create(SandboxedProcessInfo sandboxedProcessInfo, bool forceSandboxing)
        {
            var sandboxKind = sandboxedProcessInfo.SandboxKind == SandboxKind.None && forceSandboxing
                ? SandboxKind.Default
                : sandboxedProcessInfo.SandboxKind;

            if (sandboxKind == SandboxKind.None)
            {
                return new UnsandboxedProcess(sandboxedProcessInfo);
            }
            else if (OperatingSystemHelper.IsUnixOS)
            {
                return new SandboxedProcessUnix(sandboxedProcessInfo);
            }
            else
            {
                return new SandboxedProcess(sandboxedProcessInfo);
            }
        }

        /// <summary>
        /// Entry point for an I/O task which creates a process.
        /// </summary>
        /// <remarks>
        /// This is a separate function and not inlined as an anonymous delegate, as VS seems to have trouble with those when
        /// measuring code coverage
        /// </remarks>
        private static ISandboxedProcess ProcessStart(object? state)
        {
            Counters.IncrementCounter(SandboxedProcessCounters.SandboxedProcessCount);
            var stateTuple = (Tuple<SandboxedProcessInfo, bool>)state!;
            SandboxedProcessInfo info = stateTuple.Item1;
            ISandboxedProcess? result = null;
            try
            {
                result = Create(info, forceSandboxing: stateTuple.Item2);
                result.Start(); // this can take a while; performs I/O
            }
            catch
            {
                result?.Dispose();
                throw;
            }

            return result;
        }
    }
}
