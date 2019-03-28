// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities.Tracing;

namespace BuildXL.Scheduler.IncrementalScheduling
{
    internal enum GraphChangeCounter
    {
        /// <summary>
        /// Duration for updating file producers.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        UpdateFileProducersDuration,

        /// <summary>
        /// Duration for updating directory producers.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        UpdateDirectoryProducersDuration,

        /// <summary>
        /// Duration for verifying if clean pips are still clean across graphs.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        VerifyCleanPipsAcrossDuration,

        /// <summary>
        /// Duration for marking nodes to be dirtied due to graph change.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        MarkDirtyNodesTransitivelyDuration,

        /// <summary>
        /// Duration for dirtying graph agnostic state due to graph change.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        DirtyGraphAgnosticStateDuration,

        /// <summary>
        /// Number of nodes marked dirty transitively due to graph change.
        /// </summary>
        PipsOfCurrentGraphGetDirtiedDueToGraphChangeCount,

        /// <summary>
        /// Number of pips from different graphs that get dirtied due to graph change.
        /// </summary>
        PipsOfOtherGraphsGetDirtiedDueToGraphChangeCount,

        /// <summary>
        /// Duration for processing graph change.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        TotalGraphChangeProcessingDuration
    }
}
