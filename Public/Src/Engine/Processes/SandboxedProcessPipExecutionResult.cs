// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        /// The execution was canceled by
        ///   (1) The user due to ctrl-c
        ///   (2) BuildXL due to resource exhaustion
        ///   (3) BuildXL due to user-specified pip timeout.
        /// </summary>
        Canceled,

        /// <summary>
        /// File accesses may not have been property observed.
        /// </summary>
        FileAccessMonitoringFailed,

        /// <summary>
        /// The sandboxed process was able to run, but BuildXL failed during post-processing the result of shared opaque directories
        /// </summary>
        SharedOpaquePostProcessingFailed,
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
        /// <param name="exitCode">The error code form execution of the process if it ran</param>
        /// <param name="detouringStatuses">The detours statuses recorded for this pip</param>
        /// <param name="maxDetoursHeapSize">The max detours heap size for the processes of this pip</param>
        /// <param name="pipProperties">Additional pip properties that need to be bubbled up to session telemetry</param>
        /// <returns>A new instance of SandboxedProcessPipExecutionResult.</returns>
        internal static SandboxedProcessPipExecutionResult PreparationFailure(
            int exitCode = 0,
            IReadOnlyList<ProcessDetouringStatusData> detouringStatuses = null,
            long maxDetoursHeapSize = 0,
            Dictionary<string, int> pipProperties = null)
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
                exitCode: exitCode,
                sandboxPrepMs: 0,
                processSandboxedProcessResultMs: 0,
                processStartTime: 0L,
                allReportedFileAccesses: null,
                detouringStatuses: detouringStatuses,
                maxDetoursHeapSize: maxDetoursHeapSize,
                containerConfiguration: ContainerConfiguration.DisabledIsolation,
                pipProperties: pipProperties,
                timedOut: false,
                createdDirectories: null);
        }

        internal static SandboxedProcessPipExecutionResult FailureButRetryAble(
            SandboxedProcessPipExecutionStatus status,
            RetryInfo retryInfo,
            long maxDetoursHeapSize = 0,
            ProcessTimes primaryProcessTimes = null)
        {
            return new SandboxedProcessPipExecutionResult(
                status,
                observedFileAccesses: default(SortedReadOnlyArray<ObservedFileAccess, ObservedFileAccessExpandedPathComparer>),
                sharedDynamicDirectoryWriteAccesses: null,
                encodedStandardError: null,
                encodedStandardOutput: null,
                numberOfWarnings: 0,
                unexpectedFileAccesses: null,
                primaryProcessTimes: primaryProcessTimes,
                jobAccountingInformation: null,
                exitCode: 0,
                sandboxPrepMs: 0,
                processSandboxedProcessResultMs: 0,
                processStartTime: 0L,
                allReportedFileAccesses: null,
                detouringStatuses: null,
                maxDetoursHeapSize: maxDetoursHeapSize,
                containerConfiguration: ContainerConfiguration.DisabledIsolation,
                pipProperties: null,
                timedOut: false,
                retryInfo: retryInfo,
                createdDirectories: null);
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
                exitCode: result.ExitCode,
                sandboxPrepMs: result.SandboxPrepMs,
                processSandboxedProcessResultMs: result.ProcessSandboxedProcessResultMs,
                processStartTime: result.ProcessStartTimeMs,
                allReportedFileAccesses: result.AllReportedFileAccesses,
                detouringStatuses: result.DetouringStatuses,
                maxDetoursHeapSize: result.MaxDetoursHeapSizeInBytes,
                containerConfiguration: result.ContainerConfiguration,
                pipProperties: result.PipProperties,
                timedOut: result.TimedOut,
                createdDirectories: result.CreatedDirectories);
        }

        internal static SandboxedProcessPipExecutionResult RetryProcessDueToUserSpecifiedExitCode(
            int exitCode,
            ProcessTimes primaryProcessTimes,
            JobObject.AccountingInformation? jobAccountingInformation,
            IReadOnlyList<ProcessDetouringStatusData> detouringStatuses,
            long sandboxPrepMs,
            long processSandboxedProcessResultMs,
            long processStartTime,
            long maxDetoursHeapSize,
            ContainerConfiguration containerConfiguration,
            Tuple<AbsolutePath, Encoding> encodedStandardError,
            Tuple<AbsolutePath, Encoding> encodedStandardOutput,
            Dictionary<string, int> pipProperties,
            IReadOnlyDictionary<AbsolutePath, IReadOnlyCollection<FileArtifactWithAttributes>> sharedDynamicDirectoryWriteAccesses,
            RetryInfo retryInfo = null)
        {
            return new SandboxedProcessPipExecutionResult(
                SandboxedProcessPipExecutionStatus.ExecutionFailed,
                observedFileAccesses: default(SortedReadOnlyArray<ObservedFileAccess, ObservedFileAccessExpandedPathComparer>),
                sharedDynamicDirectoryWriteAccesses: sharedDynamicDirectoryWriteAccesses,
                encodedStandardError: encodedStandardError,
                encodedStandardOutput: encodedStandardOutput,
                numberOfWarnings: 0,
                unexpectedFileAccesses: null,
                primaryProcessTimes: primaryProcessTimes,
                jobAccountingInformation: jobAccountingInformation,
                exitCode: exitCode,
                sandboxPrepMs: sandboxPrepMs,
                processSandboxedProcessResultMs: processSandboxedProcessResultMs,
                processStartTime: processStartTime,
                allReportedFileAccesses: null,
                detouringStatuses: detouringStatuses,
                maxDetoursHeapSize: maxDetoursHeapSize,
                containerConfiguration: containerConfiguration,
                pipProperties: pipProperties,
                timedOut: false,
                retryInfo: retryInfo ?? RetryInfo.GetDefault(RetryReason.UserSpecifiedExitCode),
                createdDirectories: null);
        }

        internal static SandboxedProcessPipExecutionResult MismatchedMessageCountFailure(SandboxedProcessPipExecutionResult result)
            => new SandboxedProcessPipExecutionResult(
                   SandboxedProcessPipExecutionStatus.FileAccessMonitoringFailed,
                   result.ObservedFileAccesses,
                   result.SharedDynamicDirectoryWriteAccesses,
                   result.EncodedStandardOutput,
                   result.EncodedStandardError,
                   result.NumberOfWarnings,
                   result.UnexpectedFileAccesses,
                   result.PrimaryProcessTimes,
                   result.JobAccountingInformation,
                   result.ExitCode,
                   result.SandboxPrepMs,
                   result.ProcessSandboxedProcessResultMs,
                   result.ProcessStartTimeMs,
                   result.AllReportedFileAccesses,
                   result.DetouringStatuses,
                   result.MaxDetoursHeapSizeInBytes,
                   result.ContainerConfiguration,
                   result.PipProperties,
                   result.TimedOut,
                   result.CreatedDirectories,
                   retryInfo: RetryInfo.GetDefault(RetryReason.MismatchedMessageCount));

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
        public readonly IReadOnlyDictionary<AbsolutePath, IReadOnlyCollection<FileArtifactWithAttributes>> SharedDynamicDirectoryWriteAccesses;

        /// <summary>
        /// Observed accesses that were reported explicitly (e.g. as part of a directory dependency).
        /// </summary>
        public readonly SortedReadOnlyArray<ObservedFileAccess, ObservedFileAccessExpandedPathComparer> ObservedFileAccesses;

        /// <summary>
        /// The max Detours heap size for processes of this pip.
        /// </summary>
        public readonly long MaxDetoursHeapSizeInBytes;

        /// <summary>
        /// Context containing counters for unexpected file accesses reported so far (allowlisted, cacheable-allowlisted, etc),
        /// and a list of those unexpected file accesses which were not allowlisted (violations).
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

        /// <summary>
        /// Extract a pip property and the count of that property, if a value matching the PipProperty regex was defined in the process output
        /// </summary>
        public Dictionary<string, int> PipProperties { get; set; }

        /// <summary>
        /// A flag to denote if the process was retried based on a User set retry code
        /// </summary>
        public bool HadUserRetries { get; set; }

        /// <summary>
        /// Indicates the reason for pip failure. Can be retried on the same or a different worker.
        /// </summary>
        public RetryInfo RetryInfo { get; set; }

        /// <summary>
        /// Collection of directories that were succesfully created during pip execution. 
        /// </summary>
        /// <remarks>
        /// Observe there is no guarantee those directories still exist. However, there was a point during the execution of the associated pip when these directories 
        /// were not there, the running pip created them and the creation was successful. 
        /// Only populated if allowed undeclared reads is on, since these are used for computing directory fingerprint enumeration when undeclared files are allowed.
        /// </remarks>
        public IReadOnlySet<AbsolutePath> CreatedDirectories { get; }

        /// <summary>
        /// How long the process has been suspended
        /// </summary>
        public long SuspendedDurationMs { get; set; }

        private bool ProcessCompletedExecution(SandboxedProcessPipExecutionStatus status) =>
            status != SandboxedProcessPipExecutionStatus.PreparationFailed && 
            status != SandboxedProcessPipExecutionStatus.Canceled &&
            status != SandboxedProcessPipExecutionStatus.ExecutionFailed;

        /// <nodoc />
        public SandboxedProcessPipExecutionResult(
            SandboxedProcessPipExecutionStatus status,
            SortedReadOnlyArray<ObservedFileAccess, ObservedFileAccessExpandedPathComparer> observedFileAccesses,
            IReadOnlyDictionary<AbsolutePath, IReadOnlyCollection<FileArtifactWithAttributes>> sharedDynamicDirectoryWriteAccesses,
            Tuple<AbsolutePath, Encoding> encodedStandardOutput,
            Tuple<AbsolutePath, Encoding> encodedStandardError,
            int numberOfWarnings,
            FileAccessReportingContext unexpectedFileAccesses,
            ProcessTimes primaryProcessTimes,
            JobObject.AccountingInformation? jobAccountingInformation,
            int exitCode,
            long sandboxPrepMs,
            long processSandboxedProcessResultMs,
            long processStartTime,
            IReadOnlyList<ReportedFileAccess> allReportedFileAccesses,
            IReadOnlyList<ProcessDetouringStatusData> detouringStatuses,
            long maxDetoursHeapSize,
            ContainerConfiguration containerConfiguration,
            Dictionary<string, int> pipProperties,
            bool timedOut,
            IReadOnlySet<AbsolutePath> createdDirectories,
            RetryInfo retryInfo = null)
        {
            Contract.Requires(!ProcessCompletedExecution(status) || observedFileAccesses.IsValid);
            Contract.Requires(!ProcessCompletedExecution(status) || unexpectedFileAccesses != null);
            Contract.Requires(!ProcessCompletedExecution(status) || primaryProcessTimes != null);
            Contract.Requires(encodedStandardOutput == null || (encodedStandardOutput.Item1.IsValid && encodedStandardOutput.Item2 != null));
            Contract.Requires(encodedStandardError == null || (encodedStandardError.Item1.IsValid && encodedStandardError.Item2 != null));
            Contract.Requires(numberOfWarnings >= 0);
            Contract.Requires(containerConfiguration != null);
            Contract.Requires(retryInfo == null || status != SandboxedProcessPipExecutionStatus.Succeeded);

            // Protect against invalid combinations of RetryLocation and RetryReason
            Contract.Requires(!retryInfo.CanBeRetriedInlineOrFalseIfNull() || retryInfo.RetryReason != RetryReason.ResourceExhaustion);
            Contract.Requires(!retryInfo.CanBeRetriedInlineOrFalseIfNull() || retryInfo.RetryReason != RetryReason.ProcessStartFailure);
            Contract.Requires(!retryInfo.CanBeRetriedInlineOrFalseIfNull() || retryInfo.RetryReason != RetryReason.TempDirectoryCleanupFailure);
            Contract.Requires(!retryInfo.CanBeRetriedInlineOrFalseIfNull() || retryInfo.RetryReason != RetryReason.StoppedWorker);

            Status = status;
            ObservedFileAccesses = observedFileAccesses;
            UnexpectedFileAccesses = unexpectedFileAccesses;
            EncodedStandardOutput = encodedStandardOutput;
            EncodedStandardError = encodedStandardError;
            NumberOfWarnings = numberOfWarnings;
            PrimaryProcessTimes = primaryProcessTimes;
            JobAccountingInformation = jobAccountingInformation;
            ExitCode = exitCode;
            SandboxPrepMs = sandboxPrepMs;
            ProcessSandboxedProcessResultMs = processSandboxedProcessResultMs;
            ProcessStartTimeMs = processStartTime;
            AllReportedFileAccesses = allReportedFileAccesses;
            DetouringStatuses = detouringStatuses;
            MaxDetoursHeapSizeInBytes = maxDetoursHeapSize;
            SharedDynamicDirectoryWriteAccesses = sharedDynamicDirectoryWriteAccesses;
            ContainerConfiguration = containerConfiguration;
            PipProperties = pipProperties;
            TimedOut = timedOut;
            RetryInfo = retryInfo;
            CreatedDirectories = createdDirectories ?? CollectionUtilities.EmptySet<AbsolutePath>();
        }

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

        /// <summary>
        /// Whether it is timedout
        /// </summary>
        public bool TimedOut { get; set; }
    }
}
