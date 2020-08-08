// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.Processes
{
    /// <summary>
    /// Some Failing Processes can be retried.
    /// This class defines the details required for the retry.
    /// </summary>
    public class RetryInfo
    {
        /// <summary>
        /// Reason for retry
        /// </summary>
        public RetryReason RetryReason { get; }

        /// <summary>
        /// Location for retry.
        /// Caller can decide to retry at a different location
        /// </summary>
        public RetryLocation RetryLocation { get; }

        /// <nodoc/>
        private RetryInfo(RetryReason retryReason, RetryLocation retryLocation)
        {
            RetryReason = retryReason;
            RetryLocation = retryLocation;
        }

        /// <summary>
        /// Returns a RetryInfo object to be retried on the Same Worker
        /// </summary>
        public static RetryInfo RetryOnSameWorker(RetryReason retryReason)
        {
            return new RetryInfo(retryReason, RetryLocation.SameWorker);
        }

        /// <summary>
        /// Returns a RetryInfo object to be retried on a Different Worker
        /// </summary>
        public static RetryInfo RetryOnDifferentWorker(RetryReason retryReason)
        {
            return new RetryInfo(retryReason, RetryLocation.DifferentWorker);
        }

        /// <summary>
        /// Returns a RetryInfo object to be retried on the Same Worker first, and retried on another worker of it fails again
        /// </summary>
        public static RetryInfo RetryOnSameAndDifferentWorkers(RetryReason retryReason)
        {
            return new RetryInfo(retryReason, RetryLocation.Both);
        }

        private bool RetryAbleOnSameWorker() => RetryLocation == RetryLocation.SameWorker || RetryLocation == RetryLocation.Both;
        private bool RetryAbleOnDifferentWorker() => RetryLocation == RetryLocation.DifferentWorker || RetryLocation == RetryLocation.Both;

        /// <summary>
        /// Returns true if retry location is <see cref="RetryLocation.SameWorker"/> or <see cref="RetryLocation.Both"/> after a null check
        /// </summary>
        public static bool RetryAbleOnSameWorker(RetryInfo retryInfo) => retryInfo?.RetryAbleOnSameWorker() ?? false;

        /// <summary>
        /// Returns true if retry location is <see cref="RetryLocation.DifferentWorker"/> or <see cref="RetryLocation.Both"/> after a null check
        /// </summary>
        public static bool RetryAbleOnDifferentWorker(RetryInfo retryInfo) => retryInfo?.RetryAbleOnDifferentWorker() ?? false;

        /// <nodoc/>
        public void Serialize(BuildXLWriter writer)
        {
            writer.Write((int)RetryReason);
            writer.Write((int)RetryLocation);
        }

        /// <nodoc/>
        public static RetryInfo Deserialize(BuildXLReader reader)
        {
            var retryReason = (RetryReason)reader.ReadInt32();
            var retryLocation = (RetryLocation)reader.ReadInt32();

            return new RetryInfo(retryReason, retryLocation);
        }
    }

    /// <summary>
    /// Location where retry should occur.
    /// </summary>
    public enum RetryLocation
    {
        /// <summary>
        /// Retry on Same Worker
        /// </summary>
        SameWorker = 0,

        /// <summary>
        /// Retry on Different Worker
        /// </summary>
        DifferentWorker = 1,

        /// <summary>
        /// Retry on Same Worker before retrying on Different Worker
        /// Behavior mimics <see cref="SameWorker"/> until we reach the local retry limit, and then mimics <see cref="DifferentWorker"/>.
        /// </summary>
        Both = 2,
    }

    /// <summary>
    /// Reasons to retry failing pips
    /// </summary>
    public enum RetryReason
    {
        /// <summary>
        /// ResourceExhaustion
        /// Retried on Different Worker
        /// </summary>
        ResourceExhaustion = 0,

        /// <summary>
        /// ProcessStartFailure
        /// Retried on Different Worker
        /// </summary>
        ProcessStartFailure = 1,

        /// <summary>
        /// TempDirectoryCleanupFailure
        /// Retried on Different Worker
        /// </summary>
        TempDirectoryCleanupFailure = 2,

        /// <summary>
        /// Stopped worker
        /// Retried on Different Worker
        /// </summary>
        StoppedWorker = 3,

        /// <summary>
        /// There is an output produced with file access observed.
        /// Retried on Same Worker
        /// </summary>
        OutputWithNoFileAccessFailed = 4,

        /// <summary>
        /// There is a mismatch between messages sent by pip children processes and messages received.
        /// Retried on Same Worker
        /// </summary>
        MismatchedMessageCount = 5,

        /// <summary>
        /// The sandboxed process should be retried due to Azure Watson's 0xDEAD exit code.
        /// Retried on Same Worker
        /// </summary>
        AzureWatsonExitCode = 6,

        /// <summary>
        /// The sandboxed process should be retried due to exit code.
        /// Retried on Same Worker
        /// </summary>
        UserSpecifiedExitCode = 7,
    }

    /// <summary>
    /// Extensions
    /// </summary>
    public static class RetryReasonExtensions
    {
        /// <summary>
        /// Is retryable failure during the prep
        /// </summary>
        public static bool IsPrepRetryableFailure(RetryReason? retryReason)
        {
            if (retryReason == null)
            {
                return false;
            }

            switch (retryReason)
            {
                case RetryReason.ProcessStartFailure:
                case RetryReason.TempDirectoryCleanupFailure:
                    return true;
            }

            return false;
        }

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
            }

            return false;
        }
    }
}
