// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// Cycle detector statistics
    /// </summary>
    public sealed class CycleDetectorStatistics
    {
        /// <summary>
        /// How many threads were created for cycle detection efforts
        /// </summary>
        public long CycleDetectionThreadsCreated;

        /// <summary>
        /// As part of cycle detection efforts, how many thunk chains were added for potential consideration
        /// </summary>
        public long CycleDetectionChainsAdded;

        /// <summary>
        /// As part of cycle detection efforts, how many thunk chains were removed before they were processed at all by the background thread of the cycle detector
        /// </summary>
        public long CycleDetectionChainsRemovedBeforeProcessing;

        /// <summary>
        /// As part of cycle detection efforts, how many thunk chains were removed after they were fully processed by the background thread of the cycle detector
        /// </summary>
        public long CycleDetectionChainsRemovedAfterProcessing;

        /// <summary>
        /// As part of cycle detection efforts, how many thunk chains were removed before they were fully processed by the background thread of the cycle detector
        /// </summary>
        public long CycleDetectionChainsAbandonedWhileProcessing;
    }
}
