// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.ContentStore.Distributed.NuCache.CopyScheduling
{
    /// <summary>
    /// Assigns a priority to each request that the <see cref="PrioritizedCopyScheduler"/> can handle.
    /// </summary>
    internal interface ICopySchedulerPriorityAssigner
    {
        /// <summary>
        /// Maximum priority that can be produced by the assignment. It should be a priority that's attainable by some
        /// requests, not a supremum.
        /// </summary>
        public int MaxPriority { get; }

        /// <summary>
        /// Categorizes a request into a priority class.
        /// </summary>
        /// <remarks>
        /// Priority can range from 0 to <see cref="MaxPriority"/> (inclusive). It is important to guarantee that this
        /// is true, since out of bounds accesses can occur otherwise.
        /// </remarks>
        public int Prioritize(CopyOperationBase request);
    }
}
