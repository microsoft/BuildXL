// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    /// <summary>
    /// This interface represents a place where we store volatile logs for usage within the
    /// <see cref="ContentMetadataEventStream"/>. 
    ///
    /// This is used as a fast, potentially volatile storage for the logs that are generated. The append to this log is
    /// performed inline with the operations, so if this is slow, then all operations are slow.
    ///
    /// We expect logs to be available in this storage until we manage to write them to the
    /// <see cref="IWriteBehindEventStorage"/>, which should typically happen a couple of seconds later. The only
    /// exception to this case is master crashes. When that happens, we expect the logs to be stored until the master
    /// comes back to life, which could be in the range of 5m to 20/30m later.
    ///
    /// If data loss happens in this case, then data will be lost for all of the system.
    /// </summary>
    public interface IWriteAheadEventStorage : IStartupShutdownSlim
    {
        /// <summary>
        /// Appends the blob to the blob specified by the cursor
        /// </summary>
        Task<BoolResult> AppendAsync(OperationContext context, BlockReference cursor, ReadOnlyMemory<byte> data);

        /// <summary>
        /// Reads the blob specified by the cursor
        /// </summary>
        Task<Result<Optional<ReadOnlyMemory<byte>>>> ReadAsync(OperationContext context, BlockReference cursor);

        /// <summary>
        /// Garbage collects all blobs in the storage specified up to the latest represented by the cursor
        /// </summary>
        Task<BoolResult> GarbageCollectAsync(OperationContext context, BlockReference cursor);
    }
}
