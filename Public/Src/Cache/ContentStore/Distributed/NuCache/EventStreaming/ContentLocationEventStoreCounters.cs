// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming
{
    /// <summary>
    /// Performance counters available for <see cref="ContentLocationDatabase"/>.
    /// </summary>
    public enum ContentLocationEventStoreCounters
    {
        //
        // Dispatch events counters
        //

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        DispatchAddLocations = 1,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        DispatchRemoveLocations,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        DispatchTouch,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        DispatchReconcile,

        /// <nodoc />
        DispatchAddLocationsHashes,

        /// <nodoc />
        DispatchRemoveLocationsHashes,

        /// <nodoc />
        DispatchTouchHashes,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        ProcessEvents,

        /// <nodoc />
        ReceivedEventBatchCount,

        /// <nodoc />
        ReceivedEventsCount,

        /// <nodoc />
        ReceivedMessagesTotalSize,

        /// <nodoc />
        MessagesWithoutSenderMachine,

        /// <nodoc />
        FilteredEvents,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        Deserialization,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        DispatchEvents,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        GetAndDeserializeReconcileData,

        //
        // Send events counters
        //

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        PublishAddLocations,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        PublishRemoveLocations,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        PublishTouchLocations,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        PublishReconcile,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        StartProcessing,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        SuspendProcessing,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        SendEvents,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        Serialization,

        /// <nodoc />
        SentEventBatchCount,

        /// <nodoc />
        SentEventsCount,

        /// <nodoc />
        SentMessagesTotalSize,

        /// <nodoc />
        SentAddLocationsEvents,
        /// <nodoc />
        SentAddLocationsHashes,

        /// <nodoc />
        SentRemoveLocationsEvents,

        /// <nodoc />
        SentRemoveLocationsHashes,

        /// <nodoc />
        SentTouchLocationsEvents,

        /// <nodoc />
        SentTouchLocationsHashes,

        /// <nodoc />
        SentReconcileEvents,
    }
}
