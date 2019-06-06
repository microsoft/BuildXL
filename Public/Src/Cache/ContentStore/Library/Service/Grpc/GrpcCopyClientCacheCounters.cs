// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// Performance counters available for <see cref="GrpcCopyClientCache"/>.
    /// </summary>
    public enum GrpcCopyClientCacheCounters
    {
        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        Cleanup,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        ClientCreationTime,

        /// <nodoc />
        ClientsCreated,

        /// <nodoc />
        ClientsCleaned,

        /// <nodoc />
        ClientsReused,
    }
}
