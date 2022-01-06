// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using System.IO;

namespace BuildXL.Processes
{
    /// <summary>
    /// Information for process retrial, including the reason and the location (same worker vs. different worker) for retry.
    /// </summary>
    public class RetryInfo
    {
        /// <summary>
        /// Reason for retry.
        /// </summary>
        public RetryReason RetryReason { get; }

        /// <summary>
        /// Mode for retry.
        /// </summary>
        public RetryMode RetryMode { get; }

        /// <summary>
        /// Constructor.
        /// </summary>
        private RetryInfo(RetryReason retryReason, RetryMode retryMode)
        {
            RetryReason = retryReason;
            RetryMode = retryMode;
        }

        private static RetryInfo RetryInline(RetryReason retryReason) => new (retryReason, RetryMode.Inline);

        private static RetryInfo RetryByReschedule(RetryReason retryReason) => new (retryReason, RetryMode.Reschedule);

        private static RetryInfo RetryInlineFirstThenByReschedule(RetryReason retryReason) => new (retryReason, RetryMode.Both);

        /// <summary>
        /// Checks if a process can be retried inline.
        /// </summary>
        public bool CanBeRetriedInline() => RetryMode.CanBeRetriedInline();

        /// <summary>
        /// Checks if a process can be retried by requeuing it to the scheduler.
        /// </summary>
        public bool CanBeRetriedByReschedule() => RetryMode.CanBeRetriedByReschedule();

        /// <summary>
        /// Serializes this instance through an instance of <see cref="BinaryWriter"/>.
        /// </summary>
        public void Serialize(BinaryWriter writer)
        {
            writer.Write((int)RetryReason);
            writer.Write((int)RetryMode);
        }

        /// <summary>
        /// Deserializes an instance of <see cref="RetryInfo"/> from an instance of <see cref="BinaryReader"/>.
        /// </summary>
        public static RetryInfo Deserialize(BinaryReader reader)
        {
            var retryReason = (RetryReason)reader.ReadInt32();
            var retryLocation = (RetryMode)reader.ReadInt32();

            return new RetryInfo(retryReason, retryLocation);
        }

        /// <summary>
        /// Gets the default retry information (e.g., retry location) for a given <see cref="RetryReason"/>.
        /// </summary>
        public static RetryInfo GetDefault(RetryReason reason)
        {
            switch (reason)
            {
                case RetryReason.ResourceExhaustion:
                case RetryReason.ProcessStartFailure:
                case RetryReason.TempDirectoryCleanupFailure:
                case RetryReason.StoppedWorker:
                    return RetryByReschedule(reason);

                case RetryReason.OutputWithNoFileAccessFailed:
                case RetryReason.MismatchedMessageCount:
                case RetryReason.AzureWatsonExitCode:
                case RetryReason.UserSpecifiedExitCode:
                    return RetryInline(reason);

                case RetryReason.VmExecutionError:
                    return RetryInlineFirstThenByReschedule(reason);

                case RetryReason.RemoteFallback:
                    return RetryByReschedule(reason);

                default:
                    throw Contract.AssertFailure("Default not defined for RetryReason: " + reason.ToString());
            }
        }
    }

    /// <summary>
    /// Extensions for <see cref="RetryInfo"/>.
    /// </summary>
    public static class RetryInfoExtensions
    {
        /// <summary>
        /// Returns true if the process can be retried inline; or false otherwise, including null instance.
        /// </summary>
        public static bool CanBeRetriedInlineOrFalseIfNull(this RetryInfo @this) => @this?.CanBeRetriedInline() ?? false;

        /// <summary>
        /// Returns true if the process can be retried by requeuing it back to the scheduler; or false otherwise, including null instance.
        /// </summary>
        public static bool CanBeRetriedByRescheduleOrFalseIfNull(this RetryInfo @this) => @this?.CanBeRetriedByReschedule() ?? false;
    }

    /// <summary>
    /// Mode or mechanism for retrying processes.
    /// </summary>
    public enum RetryMode
    {
        /// <summary>
        /// Asks the pip executor to re-execute the process without requeuing (sending the process back) to the scheduler.
        /// </summary>
        /// <remarks>
        /// This retry mode guarantees that the process will be retried on the same worker.
        /// </remarks>
        Inline = 0,

        /// <summary>
        /// Ask the pip executor to send the process back to the scheduler so that it goes through to the scheduler queue.
        /// </summary>
        /// <remarks>
        /// In distributed build, this retry mode can make the process re-execute on a different worker. In non distributed build,
        /// the process will be retried on the same worker until reaching retry limit.
        /// </remarks>
        Reschedule = 1,

        /// <summary>
        /// Retry inline first until reaching retry limit, then reschedule.
        /// </summary>
        /// <remarks>
        /// This mode is useful when we want to retry the process first on the same worker (guaranteed by <see cref="Inline"/>), then
        /// when failure persists, retry the process on a different worker.
        /// </remarks>
        Both = 2,
    }

    /// <summary>
    /// Reasons for retrying a failing process.
    /// </summary>
    public enum RetryReason
    {
        /// <summary>
        /// ResourceExhaustion
        /// </summary>
        ResourceExhaustion = 0,

        /// <summary>
        /// ProcessStartFailure
        /// </summary>
        ProcessStartFailure = 1,

        /// <summary>
        /// TempDirectoryCleanupFailure
        /// </summary>
        TempDirectoryCleanupFailure = 2,

        /// <summary>
        /// Stopped worker
        /// </summary>
        StoppedWorker = 3,

        /// <summary>
        /// There is an output produced with file access observed.
        /// </summary>
        OutputWithNoFileAccessFailed = 4,

        /// <summary>
        /// There is a mismatch between messages sent by pip children processes and messages received.
        /// </summary>
        MismatchedMessageCount = 5,

        /// <summary>
        /// The sandboxed process should be retried due to Azure Watson's 0xDEAD exit code.
        /// </summary>
        AzureWatsonExitCode = 6,

        /// <summary>
        /// The sandboxed process should be retried due to exit code.
        /// </summary>
        UserSpecifiedExitCode = 7,

        /// <summary>
        /// The sandboxed process may be retried due to failures caused during VM execution.
        /// </summary>
        VmExecutionError = 8,

        /// <summary>
        /// The sandboxed process may be retried due to fallback from remote execution.
        /// </summary>
        RemoteFallback = 9
    }

    /// <summary>
    /// Extensions for <see cref="RetryReason"/>.
    /// </summary>
    public static class RetryReasonExtensions
    {
        /// <summary>
        /// Is retryable failure during the pre process execution.
        /// </summary>
        public static bool IsPreProcessExecRetryableFailure(this RetryReason retryReason)
        {
            switch (retryReason)
            {
                case RetryReason.ProcessStartFailure:
                case RetryReason.TempDirectoryCleanupFailure:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Is retryable failure due to a pre process execution failure or due to remoting (VM or AnyBuild) infrastructure error.
        /// </summary>
        public static bool IsPreProcessExecOrRemotingInfraFailure(this RetryReason retryReason) =>
                retryReason.IsPreProcessExecRetryableFailure()
                || retryReason == RetryReason.VmExecutionError
                || retryReason == RetryReason.RemoteFallback;

        /// <summary>
        /// Is retryable failure due to Detours
        /// </summary>
        public static bool IsDetoursRetrableFailure(this RetryReason retryReason)
        {
            switch (retryReason)
            {
                case RetryReason.MismatchedMessageCount:
                case RetryReason.OutputWithNoFileAccessFailed:
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// Extensions for <see cref="RetryMode"/>.
    /// </summary>
    public static class RetryLocationExtensions
    {
        /// <summary>
        /// Checks if retry can be done inline by the pip executor.
        /// </summary>
        public static bool CanBeRetriedInline(this RetryMode @this) => @this == RetryMode.Inline || @this == RetryMode.Both;

        /// <summary>
        /// Checks if retry can be done by requeuing the process to the scheduler.
        /// </summary>
        public static bool CanBeRetriedByReschedule(this RetryMode @this) => @this == RetryMode.Reschedule || @this == RetryMode.Both;
    }
}
