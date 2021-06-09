// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    /// <summary>
    /// This interface represents a place where we store persistent logs for usage within the
    /// <see cref="ContentMetadataEventStream"/>. 
    ///
    /// Logs can be appended to until they are sealed, after which point no more appends are allowed. Only sealed logs
    /// can be read from.
    ///
    /// Persistent logs are expected to never be removed, unless they are garbage collected through
    /// <see cref="GarbageCollectAsync(OperationContext, CheckpointLogId)"/>.
    ///
    /// Since log writing to the write behind storage happens in the background, it is fine for it to not be the as
    /// fast as <see cref="IWriteAheadEventStorage"/>. However, this storage does need to guarantee that logs are
    /// persisted.
    /// </summary>
    public interface IWriteBehindEventStorage : IStartupShutdownSlim
    {
        /// <summary>
        /// Appends the blob to the blob specified by the cursor
        /// </summary>
        Task<BoolResult> AppendAsync(OperationContext context, BlockReference cursor, Stream stream);

        /// <summary>
        /// Reads the blob specified by the cursor
        /// </summary>
        Task<Result<Optional<Stream>>> ReadAsync(OperationContext context, CheckpointLogId logId);

        /// <summary>
        /// Checks if the log is sealed (i.e. writes completed successfully and there are no further writes possible)
        /// </summary>
        Task<Result<bool>> IsSealedAsync(OperationContext context, CheckpointLogId logId);

        /// <summary>
        /// Seals the log (i.e. writes completed successfully and there are no further writes possible)
        /// </summary>
        Task<BoolResult> SealAsync(OperationContext context, CheckpointLogId logId);

        /// <summary>
        /// Garbage collects all blobs in the storage specified up to the latest represented by the cursor
        /// </summary>
        Task<BoolResult> GarbageCollectAsync(OperationContext context, CheckpointLogId logId);
    }
}
