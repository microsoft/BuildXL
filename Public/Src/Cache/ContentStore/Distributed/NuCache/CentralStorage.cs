// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Tracing;

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
        /// Indicates whether the central storage instance supports SAS download urls
        /// </summary>
        public virtual bool SupportsSasUrls { get; }

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
                extraEndMessage: _ => $"File=[{file}] Name=[{name}]");
        }

        /// <summary>
        /// Touches a blob to indicate that the blob is still active.
        /// </summary>
        public Task<BoolResult> TouchBlobAsync(OperationContext context, AbsolutePath file, string storageId, bool isUploader, bool isImmutable = false)
        {
            storageId = PreprocessStorageId(storageId);
            return context.PerformOperationAsync(
                Tracer,
                () => TouchBlobCoreAsync(context, file, storageId, isUploader, isImmutable),
                counter: Counters[CentralStorageCounters.TouchBlob],
                extraEndMessage: _ => $"File=[{file}] StorageId=[{storageId}]",
                traceOperationStarted: false);
        }

        /// <summary>
        /// Try to get the blob specified by a given <paramref name="storageId"/> and store it in a <paramref name="targetFilePath"/>.
        /// </summary>
        public Task<BoolResult> TryGetFileAsync(OperationContext context, string storageId, AbsolutePath targetFilePath, bool isImmutable = false)
        {
            storageId = PreprocessStorageId(storageId);
            return context.PerformOperationAsync(
                Tracer,
                () => TryGetFileCoreAsync(context, storageId, targetFilePath, isImmutable),
                counter: Counters[CentralStorageCounters.TryGetFile],
                extraEndMessage: _ => $"StorageId=[{storageId}] TargetFilePath=[{targetFilePath}]",
                traceOperationStarted: false);
        }

        /// <summary>
        /// Attempts to get a SAS url which can be used to do
        /// </summary>
        public Task<Result<string>> TryGetSasUrlAsync(OperationContext context, string storageId, DateTime expiry)
        {
            Contract.Assert(SupportsSasUrls, "Storage must support SAS urls in order to call TryGetSasUrl");
            storageId = PreprocessStorageId(storageId);
            return context.PerformOperationAsync(
                Tracer,
                () => TryGetSasUrlCore(context, storageId, expiry),
                extraEndMessage: _ => $"[{storageId}]",
                traceOperationStarted: false);
        }

        /// <summary>
        /// <see cref="TryGetSasUrlAsync"/>
        /// </summary>
        protected virtual Task<Result<string>> TryGetSasUrlCore(OperationContext context, string storageId, DateTime expiry) => throw Contract.AssertFailure("SAS urls are not supported");

        /// <summary>
        /// <see cref="UploadFileAsync"/>
        /// </summary>
        protected abstract Task<Result<string>> UploadFileCoreAsync(OperationContext context, AbsolutePath file, string name, bool garbageCollect = false);

        /// <summary>
        /// <see cref="TouchBlobAsync"/>
        /// </summary>
        protected abstract Task<BoolResult> TouchBlobCoreAsync(OperationContext context, AbsolutePath file, string storageId, bool isUploader, bool isImmutable);

        /// <summary>
        /// <see cref="TryGetFileAsync"/>
        /// </summary>
        protected abstract Task<BoolResult> TryGetFileCoreAsync(OperationContext context, string storageId, AbsolutePath targetFilePath, bool isImmutable);
    }
}
