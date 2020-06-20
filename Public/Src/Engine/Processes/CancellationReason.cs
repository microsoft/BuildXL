// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Processes
{
    /// <summary>
    /// Cancellation reasons
    /// </summary>
    public enum CancellationReason
    {
        /// <summary>
        /// Not cancelled
        /// </summary>
        None = 0,

        /// <summary>
        /// ResourceExhaustion
        /// </summary>
        ResourceExhaustion = 1,

        /// <summary>
        /// ProcessStartFailure
        /// </summary>
        ProcessStartFailure = 2,

        /// <summary>
        /// TempDirectoryCleanupFailure
        /// </summary>
        TempDirectoryCleanupFailure = 3,

        /// <summary>
        /// Stopped worker
        /// </summary>
        StoppedWorker = 4,
    }

    /// <summary>
    /// Extensions
    /// </summary>
    public static class CancellationReasonExtensions
    {
        /// <summary>
        /// Is retryable failure during the prep
        /// </summary>
        public static bool IsPrepRetryableFailure(this CancellationReason cancellationReason)
        {
            switch (cancellationReason)
            {
                case CancellationReason.ProcessStartFailure:
                case CancellationReason.TempDirectoryCleanupFailure:
                    return true;
            }

            return false;
        }
    }
}
