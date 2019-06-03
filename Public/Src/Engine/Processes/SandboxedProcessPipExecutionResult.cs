// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Text;
using BuildXL.Processes.Containers;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Processes
{
    /// <summary>
    /// Overall completion status of a sandboxed process.
    /// </summary>
    public enum SandboxedProcessPipExecutionStatus
    {
        /// <summary>
        /// The sandboxed process was unable to run.
        /// </summary>
        PreparationFailed,

        /// <summary>
        /// The sandboxed process executed but terminated with failures.
        /// </summary>
        ExecutionFailed,

        /// <summary>
        /// The sandboxed process executed successfully.
        /// </summary>
        Succeeded,

        /// <summary>
        /// The execution was canceled by the user.
        /// </summary>
        Canceled,

        /// <summary>
        /// File accesses may not have been property observed.
        /// </summary>
        FileAccessMonitoringFailed,

        /// <summary>
        /// There is an output produced with file access observed.
        /// </summary>
        OutputWithNoFileAccessFailed,

        /// <summary>
        /// There is a mismatch between messages sent by pip children processes and messages received.
        /// </summary>
        MismatchedMessageCount,

        /// <summary>
        /// The sandboxed process should be retried due to exit code.
        /// </summary>
        ShouldBeRetriedDueToUserSpecifiedExitCode,

        /// <summary>
        /// The sandboxed process should be retried due to Azure Watson's 0xDEAD exit code.
        /// </summary>
        ShouldBeRetriedDueToAzureWatsonExitCode,
    }

    /// <summary>
    /// Result of running a <see cref="BuildXL.Pips.Operations.Process" /> pip in a <see cref="SandboxedProcess" />.
    /// </summary>
    public sealed class SandboxedProcessPipExecutionResult
    {
        /// <summary>
        /// Result with <see cref="SandboxedProcessPipExecutionStatus.PreparationFailed"/> and no execution-time information
        /// (execution-time fields are defaulted, possibly to null; for other statuses they are guaranteed present).
        /// Contains the error code of the failure, if the process was started.
        /// </summary>
        /// <param name="numberOfProcessLaunchRetries">Number of process launch retries</param>
        /// <param name="exitCode">The error code form execution of the process if it ran</param>
        /// <param name="detouringStatuses">The detours statuses recorded for this pip</param>
        /// <param name="maxDetoursHeapSize">The max detours heap size for the processes of this pip</param>
        /// <returns>A new instance of SandboxedProcessPipExecutionResult.</returns>
        internal static SandboxedProcessPipExecutionResult PreparationFailure(
            int numberOfProcessLaunchRetries = 0,
            int exitCode = 0,
            IReadOnlyList<ProcessDetouringStatusData> detouringStatuses = null,
            long maxDetoursHeapSize = 0)
        {
            return new SandboxedProcessPipExecutionResult(
                SandboxedProcessPipExecutionStatus.PreparationFailed,
                observedFileAccesses: default(SortedReadOnlyArray<ObservedFileAccess, ObservedFileAccessExpandedPathComparer>),
                sharedDynamicDirectoryWriteAccesses: null,
                encodedStandardError: null,
                encodedStandardOutput: null,
                numberOfWarnings: 0,
                unexpectedFileAccesses: null,
                primaryProcessTimes: null,
                jobAccountingInformation: null,
                numberOfProcessLaunchRetries: numberOfProcessLaunchRetries,
                exitCode: exitCode,
                sandboxPrepMs: 0,
                processSandboxedProcessResultMs: 0,
                processStartTime: 0L,
                allReportedFileAccesses: null,
                detouringStatuses: detouringStatuses,
                maxDetoursHeapSize: maxDetoursHeapSize,
                containerConfiguration: ContainerConfiguration.DisabledIsolation);
        }

        internal static SandboxedProcessPipExecutionResult DetouringFailure(SandboxedProcessPipExecutionResult result)
        {
            return new SandboxedProcessPipExecutionResult(
                SandboxedProcessPipExecutionStatus.FileAccessMonitoringFailed,
                observedFileAccesses: result.ObservedFileAccesses,
                sharedDynamicDirectoryWriteAccesses: result.SharedDynamicDirectoryWriteAccesses,
                encodedStandardError: result.EncodedStandardError,
                encodedStandardOutput: result.EncodedStandardOutput,
                numberOfWarnings: result.NumberOfWarnings,
                unexpectedFileAccesses: result.UnexpectedFileAccesses,
                primaryProcessTimes: result.PrimaryProcessTimes,
                jobAccountingInformation: result.JobAccountingInformation,
                numberOfProcessLaunchRetries: result.NumberOfProcessLaunchRetries,
                exitCode: result.ExitCode,
                sandboxPrepMs: result.SandboxPrepMs,
                processSandboxedProcessResultMs: result.ProcessSandboxedProcessResultMs,
                processStartTime: result.ProcessStartTimeMs,
                allReportedFileAccesses: result.AllReportedFileAccesses,
                detouringStatuses: result.DetouringStatuses,
                maxDetoursHeapSize: result.MaxDetoursHeapSizeInBytes,
                containerConfiguration: result.ContainerConfiguration);
        }

        internal static SandboxedProcessPipExecutionResult RetryProcessDueToUserSpecifiedExitCode(
            int numberOfProcessLaunchRetries,
            int exitCode,
            ProcessTimes primaryProcessTimes,
            JobObject.AccountingInformation? jobAccountingInformation,
            IReadOnlyList<ProcessDetouringStatusData> detouringStatuses,
            long sandboxPrepMs,
            long processSandboxedProcessResultMs,
            long processStartTime,
            long maxDetoursHeapSize,
            ContainerConfiguration containerConfiguration)
        {
            return new SandboxedProcessPipExecutionResult(
                SandboxedProcessPipExecutionStatus.ShouldBeRetriedDueToUserSpecifiedExitCode,
                observedFileAccesses: default(SortedReadOnlyArray<ObservedFileAccess, ObservedFileAccessExpandedPathComparer>),
                sharedDynamicDirectoryWriteAccesses: default(Dictionary<AbsolutePath, IReadOnlyCollection<AbsolutePath>>),
                encodedStandardError: null,
                encodedStandardOutput: null,
                numberOfWarnings: 0,
                unexpectedFileAccesses: null,
                primaryProcessTimes: primaryProcessTimes,
                jobAccountingInformation: jobAccountingInformation,
                numberOfProcessLaunchRetries: numberOfProcessLaunchRetries,
                exitCode: exitCode,
                sandboxPrepMs: sandboxPrepMs,
                processSandboxedProcessResultMs: processSandboxedProcessResultMs,
                processStartTime: processStartTime,
                allReportedFileAccesses: null,
                detouringStatuses: detouringStatuses,
                maxDetoursHeapSize: maxDetoursHeapSize,
                containerConfiguration: containerConfiguration);
        }

        internal static SandboxedProcessPipExecutionResult RetryProcessDueToAzureWatsonExitCode(
            int numberOfProcessLaunchRetries,
            int exitCode,
            ProcessTimes primaryProcessTimes,
            JobObject.AccountingInformation? jobAccountingInformation,
            IReadOnlyList<ProcessDetouringStatusData> detouringStatuses,
            long sandboxPrepMs,
            long processSandboxedProcessResultMs,
            long processStartTime,
            long maxDetoursHeapSize,
            ContainerConfiguration containerConfiguration)
        {
            return new SandboxedProcessPipExecutionResult(
                SandboxedProcessPipExecutionStatus.ShouldBeRetriedDueToAzureWatsonExitCode,
                observedFileAccesses: default(SortedReadOnlyArray<ObservedFileAccess, ObservedFileAccessExpandedPathComparer>),
                sharedDynamicDirectoryWriteAccesses: default(Dictionary<AbsolutePath, IReadOnlyCollection<AbsolutePath>>),
                encodedStandardError: null,
                encodedStandardOutput: null,
                numberOfWarnings: 0,
                unexpectedFileAccesses: null,
                primaryProcessTimes: primaryProcessTimes,
                jobAccountingInformation: jobAccountingInformation,
                numberOfProcessLaunchRetries: numberOfProcessLaunchRetries,
                exitCode: exitCode,
                sandboxPrepMs: sandboxPrepMs,
                processSandboxedProcessResultMs: processSandboxedProcessResultMs,
                processStartTime: processStartTime,
                allReportedFileAccesses: null,
                detouringStatuses: detouringStatuses,
                maxDetoursHeapSize: maxDetoursHeapSize,
                containerConfiguration: containerConfiguration);
        }

        internal static SandboxedProcessPipExecutionResult MismatchedMessageCountFailure(SandboxedProcessPipExecutionResult result)
            => new SandboxedProcessPipExecutionResult(
                   SandboxedProcessPipExecutionStatus.MismatchedMessageCount,
                   result.ObservedFileAccesses,
                   result.SharedDynamicDirectoryWriteAccesses,
                   result.EncodedStandardOutput,
                   result.EncodedStandardError,
                   result.NumberOfWarnings,
                   result.UnexpectedFileAccesses,
                   result.PrimaryProcessTimes,
                   result.JobAccountingInformation,
                   result.NumberOfProcessLaunchRetries,
                   result.ExitCode,
                   result.SandboxPrepMs,
                   result.ProcessSandboxedProcessResultMs,
                   result.ProcessStartTimeMs,
                   result.AllReportedFileAccesses,
                   result.DetouringStatuses,
                   result.MaxDetoursHeapSizeInBytes,
                   result.ContainerConfiguration);

        /// <summary>
        /// Indicates if the pip succeeded.
        /// </summary>
        public readonly SandboxedProcessPipExecutionStatus Status;

        /// <summary>
        /// All write accesses that were detected on shared dynamic directories
        /// </summary>
        /// <remarks>
        /// Keys are the paths to each shared dynamic directory. Values are paths where
        /// write attempts occurred.
        /// </remarks>
        public readonly IReadOnlyDictionary<AbsolutePath, IReadOnlyCollection<AbsolutePath>> SharedDynamicDirectoryWriteAccesses;

        /// <summary>
        /// Observed accesses that were reported explicitly (e.g. as part of a directory dependency).
        /// </summary>
        public readonly SortedReadOnlyArray<ObservedFileAccess, ObservedFileAccessExpandedPathComparer> ObservedFileAccesses;

        /// <summary>
        /// The max Detours heap size for processes of this pip.
        /// </summary>
        public readonly long MaxDetoursHeapSizeInBytes;

        /// <summary>
        /// Context containing counters for unexpected file accesses reported so far (whitelisted, cacheable-whitelisted, etc),
        /// and a list of those unexpected file accesses which were not whitelisted (violations).
        /// Note that these counts includes both error and warning level violations, depending on enforcement configuration.
        /// Additional unexpected accesses may be reported using this context (e.g. based on deferred validation of <see cref="ObservedFileAccesses"/>).
        /// Note that this field is present (non-null) even if there were no unexpected accesses.
        /// </summary>
        public readonly FileAccessReportingContext UnexpectedFileAccesses;

        /// <summary>
        /// Optional path to standard output.
        /// </summary>
        public readonly Tuple<AbsolutePath, Encoding> EncodedStandardOutput;

        /// <summary>
        /// Optional path to standard error.
        /// </summary>
        public readonly Tuple<AbsolutePath, Encoding> EncodedStandardError;

        /// <summary>
        /// Numbers of warnings in <code>EncodedStandardOutput</code> and <code>EncodedStandardError</code>.
        /// </summary>
        public readonly int NumberOfWarnings;

        /// <summary>
        /// Gets the timings of the primary process (the one started directly). This does not account for any child processes.
        /// </summary>
        public readonly ProcessTimes PrimaryProcessTimes;

        /// <summary>
        /// If available, gets the accounting information for the job representing the entire process tree that was executed (i.e., including child processes).
        /// </summary>
        public readonly JobObject.AccountingInformation? JobAccountingInformation;

        /// <summary>
        /// All reported file accesses.
        /// </summary>
        /// <remarks>
        /// This field is null if <see cref=" BuildXL.Utilities.Configuration.ISandboxConfiguration.LogObservedFileAccesses"/> is false or <see cref=" BuildXL.Utilities.Configuration.IUnsafeSandboxConfiguration.MonitorFileAccesses"/> is false.
        /// </remarks>
        public readonly IReadOnlyList<ReportedFileAccess> AllReportedFileAccesses;

        /// <summary>
        /// Optional list of all Detouring Status messages received.
        /// </summary>
        public IReadOnlyList<ProcessDetouringStatusData> DetouringStatuses { get; internal set; }

        /// <summary>
        /// How the process was configured to run in a container, or a <see cref="ContainerConfiguration.DisabledIsolation"/> if no container was specified
        /// </summary>
        public ContainerConfiguration ContainerConfiguration { get; }

        /// <nodoc />
        public SandboxedProcessPipExecutionResult(
            SandboxedProcessPipExecutionStatus status,
            SortedReadOnlyArray<ObservedFileAccess, ObservedFileAccessExpandedPathComparer> observedFileAccesses,
            IReadOnlyDictionary<AbsolutePath, IReadOnlyCollection<AbsolutePath>> sharedDynamicDirectoryWriteAccesses,
            Tuple<AbsolutePath, Encoding> encodedStandardOutput,
            Tuple<AbsolutePath, Encoding> encodedStandardError,
            int numberOfWarnings,
            FileAccessReportingContext unexpectedFileAccesses,
            ProcessTimes primaryProcessTimes,
            JobObject.AccountingInformation? jobAccountingInformation,
            int numberOfProcessLaunchRetries,
            int exitCode,
            long sandboxPrepMs,
            long processSandboxedProcessResultMs,
            long processStartTime,
            IReadOnlyList<ReportedFileAccess> allReportedFileAccesses,
            IReadOnlyList<ProcessDetouringStatusData> detouringStatuses,
            long maxDetoursHeapSize,
            ContainerConfiguration containerConfiguration)
        {
            Contract.Requires(
                (status == SandboxedProcessPipExecutionStatus.PreparationFailed || status == SandboxedProcessPipExecutionStatus.ShouldBeRetriedDueToUserSpecifiedExitCode) ||
                observedFileAccesses.IsValid);
            Contract.Requires(
                (status == SandboxedProcessPipExecutionStatus.PreparationFailed || status == SandboxedProcessPipExecutionStatus.ShouldBeRetriedDueToUserSpecifiedExitCode) ||
                unexpectedFileAccesses != null);
            Contract.Requires((status == SandboxedProcessPipExecutionStatus.PreparationFailed) || primaryProcessTimes != null);
            Contract.Requires(encodedStandardOutput == null || (encodedStandardOutput.Item1.IsValid && encodedStandardOutput.Item2 != null));
            Contract.Requires(encodedStandardError == null || (encodedStandardError.Item1.IsValid && encodedStandardError.Item2 != null));
            Contract.Requires(numberOfWarnings >= 0);
            Contract.Requires(containerConfiguration != null);

            Status = status;
            ObservedFileAccesses = observedFileAccesses;
            UnexpectedFileAccesses = unexpectedFileAccesses;
            EncodedStandardOutput = encodedStandardOutput;
            EncodedStandardError = encodedStandardError;
            NumberOfWarnings = numberOfWarnings;
            PrimaryProcessTimes = primaryProcessTimes;
            JobAccountingInformation = jobAccountingInformation;
            NumberOfProcessLaunchRetries = numberOfProcessLaunchRetries;
            ExitCode = exitCode;
            SandboxPrepMs = sandboxPrepMs;
            ProcessSandboxedProcessResultMs = processSandboxedProcessResultMs;
            ProcessStartTimeMs = processStartTime;
            AllReportedFileAccesses = allReportedFileAccesses;
            DetouringStatuses = detouringStatuses;
            MaxDetoursHeapSizeInBytes = maxDetoursHeapSize;
            SharedDynamicDirectoryWriteAccesses = sharedDynamicDirectoryWriteAccesses;
            ContainerConfiguration = containerConfiguration;
        }

        /// <summary>
        /// Number of retries to execute this pip.
        /// </summary>
        public int NumberOfProcessLaunchRetries { get; internal set; }

        /// <summary>
        /// The exit code from the execution of this pip.
        /// </summary>
        public int ExitCode { get; internal set; }

        /// <summary>
        /// Duration of sandbox preparation in milliseconds
        /// </summary>
        public long SandboxPrepMs { get; internal set; }

        /// <summary>
        /// Duration of ProcessSandboxedProcessResult in milliseconds
        /// </summary>
        public long ProcessSandboxedProcessResultMs { get; set; }

        /// <summary>
        /// Duration of process start time in milliseconds
        /// </summary>
        public long ProcessStartTimeMs { get; set; }
    }
}
