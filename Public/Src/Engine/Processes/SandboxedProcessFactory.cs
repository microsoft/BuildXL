// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Tracing;

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
        public enum SandboxedProcessCounters
        {
            /// <summary>
            /// Aggregate time spent reporting file accesses (<see cref="SandboxedProcessReports.ReportLineReceived"/>, <see cref="SandboxedProcessReports.ReportFileAccess"/>)
            /// </summary>
            [CounterType(CounterType.Stopwatch)]
            HandleAccessReportDuration,

            /// <summary>
            /// Sum of all per-process-average report queue times (in microseconds).
            /// 
            /// "Report queue time" is the time from enqueuing an access report (happens inside the kernel) until 
            /// dequeuing it (happens in the interop layer).  To compute an approximation of the average across 
            /// all access reports, devide this number by <see cref="SandboxedProcessCount"/>.
            /// </summary>
            [CounterType(CounterType.Numeric)]
            SumOfAccessReportAvgQueueTimeUs,

            /// <summary>
            /// Sum of all per-process-average report creation times (in microseconds).
            /// 
            /// "Report creation time" is the time from intercepting a file operation (inside the kernel) until
            /// enqueuing an access report corresponding to that file operation.  To compute an approximation
            /// of the average across all access reports, devide this number by <see cref="SandboxedProcessCount"/>.
            /// </summary>
            [CounterType(CounterType.Numeric)]
            SumOfAccessReportAvgCreationTimeUs,

            /// <summary>
            /// Total number of access reports received from the sandbox.
            /// </summary>
            [CounterType(CounterType.Numeric)]
            AccessReportCount,

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
            /// Number of paths with directory symlinks that were discarded
            /// </summary>
            [CounterType(CounterType.Numeric)]
            DirectorySymlinkPathsDiscardedCount
        }

        /// <summary>
        /// Counters for sandboxed process execution.
        /// </summary>
        public static readonly CounterCollection<SandboxedProcessCounters> Counters = new CounterCollection<SandboxedProcessCounters>();

        /// <summary>
        /// Start a sand-boxed process asynchronously. The result will only be available once the process terminates.
        /// </summary>
        /// <exception cref="BuildXLException">
        /// Thrown if the process creation fails in a recoverable manner due do some obscure problem detected by the underlying
        /// ProcessCreate call.
        /// </exception>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "Object lives on via task result.")]
        public static Task<ISandboxedProcess> StartAsync(SandboxedProcessInfo info, bool forceSandboxing)
        {
            Contract.Requires(info != null);
            Contract.Requires(info.FileName != null);
            Contract.Requires(info.GetCommandLine().Length <= SandboxedProcessInfo.MaxCommandLineLength);

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
        public static ISandboxedProcess Create(SandboxedProcessInfo sandboxedProcessInfo, bool forceSandboxing)
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
                return new SandboxedProcessMac(sandboxedProcessInfo, ignoreReportedAccesses: sandboxKind == SandboxKind.MacOsKextIgnoreFileAccesses);
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
        private static ISandboxedProcess ProcessStart(object state)
        {
            Counters.IncrementCounter(SandboxedProcessCounters.SandboxedProcessCount);
            var stateTuple = (Tuple<SandboxedProcessInfo, bool>)state;
            SandboxedProcessInfo info = stateTuple.Item1;
            ISandboxedProcess result = null;
            try
            {
                result = Create(info, forceSandboxing: stateTuple.Item2);
                result.Start(); // this can take a while; performs I/O
            }
            catch
            {
                result?.Dispose();
                info.ProcessIdListener?.Invoke(-1);
                throw;
            }

            return result;
        }
    }
}
