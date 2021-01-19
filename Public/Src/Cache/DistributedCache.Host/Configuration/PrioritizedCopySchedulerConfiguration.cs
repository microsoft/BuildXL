// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Utils;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache.CopyScheduling
{
    public enum PrioritizedCopySchedulerPriorityAssignmentStrategy
    {
        Default,
    }

    public record PrioritizedCopySchedulerConfiguration
    {
        /// <summary>
        /// Maximum number of concurrent copies
        /// </summary>
        public int MaximumConcurrentCopies { get; init; } = 1024;

        /// <summary>
        /// Which order to use when poping from the candidates' lists
        /// </summary>
        public SemaphoreOrder QueuePopOrder { get; init; } = SemaphoreOrder.FIFO;

        /// <summary>
        /// If we have less than this capacity, we don't do any reservation
        /// </summary>
        public int MinimumCapacityToAllowReservation { get; init; } = 256;

        /// <summary>
        /// When above <see cref="MinimumCapacityToAllowReservation"/>, the rate of total allowed copies in the current
        /// cycle to leave as free for the following ones
        /// </summary>
        public double ReservedCapacityPerCycleRate { get; init; } = 0.2;

        /// <summary>
        /// Maximum amount of time we are willing to wait for a copy to complete when we run out of capacity.
        /// </summary>
        public TimeSpan MaximumEmptyCycleWait { get; init; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Maximum number of pending copies inside the rejectable classes before we start rejecting copies
        /// </summary>
        public int MaximumPendingUntilRejection { get; init; } = 128;

        /// <summary>
        /// Strategy to use when computing the amount of copies to be performed on each priority class
        /// </summary>
        public PriorityQuotaStrategy PriorityQuotaStrategy { get; set; }

        /// <summary>
        /// Rate of leftover cycle to schedule when using <see cref="PriorityQuotaStrategy.FixedRate"/>
        /// </summary>
        public double PriorityQuotaFixedRate { get; set; } = 0.5;

        public PrioritizedCopySchedulerPriorityAssignmentStrategy PriorityAssignmentStrategy { get; }
    }
}
