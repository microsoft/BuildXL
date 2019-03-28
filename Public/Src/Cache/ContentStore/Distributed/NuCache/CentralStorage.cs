// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Tracing;
using BuildXL.Cache.ContentStore.Tracing;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Base implementation of a central storage for binary content.
    /// </summary>
    public abstract class CentralStorage : StartupShutdownBase
    {
        /// <nodoc />
        public CounterCollection<CentralStorageCounters> Counters { get; } = new CounterCollection<CentralStorageCounters>();

        /// <summary>
        /// Preprocess storage id to remove unsupported components
        /// </summary>
        protected virtual string PreprocessStorageId(string storageId) =>
            DistributedCentralStorage.ParseCompositeStorageId(storageId).fallbackStorageId;

        /// <summary>
        /// Upload a checkpoint represented by <paramref name="file"/> with a given <paramref name="name"/> and return the id.
        /// </summary>
        public Task<Result<string>> UploadFileAsync(OperationContext context, AbsolutePath file, string name, bool garbageCollect = false)
        {
            return context.PerformOperationAsync(
                Tracer,
                () => UploadFileCoreAsync(context, file, name, garbageCollect),
                counter: Counters[CentralStorageCounters.TryGetFile],
                extraStartMessage: $"[{name}|{file}]");
        }

        /// <summary>
        /// Touches a blob to indicate that the blob is still active.
        /// </summary>
        public Task<BoolResult> TouchBlobAsync(OperationContext context, AbsolutePath file, string storageId, bool isUploader)
        {
            storageId = PreprocessStorageId(storageId);
            return context.PerformOperationAsync(
                Tracer,
                () => TouchBlobCoreAsync(context, file, storageId, isUploader),
                counter: Counters[CentralStorageCounters.TouchBlob],
                extraStartMessage: $"[{storageId}]");
        }

        /// <summary>
        /// Try to get the blob specified by a given <paramref name="storageId"/> and store it in a <paramref name="targetFilePath"/>.
        /// </summary>
        public Task<BoolResult> TryGetFileAsync(OperationContext context, string storageId, AbsolutePath targetFilePath)
        {
            storageId = PreprocessStorageId(storageId);
            return context.PerformOperationAsync(
                Tracer,
                () => TryGetFileCoreAsync(context, storageId, targetFilePath),
                counter: Counters[CentralStorageCounters.TryGetFile],
                extraStartMessage: $"[{storageId}]");
        }

        /// <summary>
        /// <see cref="UploadFileAsync"/>
        /// </summary>
        protected abstract Task<Result<string>> UploadFileCoreAsync(OperationContext context, AbsolutePath file, string name, bool garbageCollect = false);

        /// <summary>
        /// <see cref="TouchBlobAsync"/>
        /// </summary>
        protected abstract Task<BoolResult> TouchBlobCoreAsync(OperationContext context, AbsolutePath file, string storageId, bool isUploader);

        /// <summary>
        /// <see cref="TryGetFileAsync"/>
        /// </summary>
        protected abstract Task<BoolResult> TryGetFileCoreAsync(OperationContext context, string storageId, AbsolutePath targetFilePath);
    }
}
