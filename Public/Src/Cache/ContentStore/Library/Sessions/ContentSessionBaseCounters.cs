// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Sessions
{
    internal enum ContentSessionBaseCounters
    {
        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        GetStats,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        Pin,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        PinBulk,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        OpenStream,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        PlaceFile,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        PlaceFileBulk,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        PutStream,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        PutFile,

        /// <nodoc />
        PinRetries,

        /// <nodoc />
        OpenStreamRetries,

        /// <nodoc />
        PlaceFileRetries,

        /// <nodoc />
        PutStreamRetries,

        /// <nodoc />
        PutFileRetries,

        /// <nodoc />
        PinBulkRetries,

        /// <nodoc />
        PlaceFileBulkRetries,

        /// <nodoc />
        PinBulkFileCount,
    }
}
