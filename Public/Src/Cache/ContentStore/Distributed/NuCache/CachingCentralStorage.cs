// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tasks;
using ContentStore.Grpc;
#nullable enable
namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// <see cref="CentralStorage"/> which uses uses local CAS as cache aside
    /// </summary>
    public class CachingCentralStorage : CentralStorage
    {
        private const string StorageIdSeparator = "||DCS||";
        protected DistributedCentralStoreConfiguration Configuration { get; }
        private const string CacheSubFolderName = "dcs";

        private readonly CentralStorage _fallbackStorage;

        // Choosing MD5 hash type as hash type for peer to peer storage somewhat arbitrarily. However, it has the nice
        // property of not being a standard hash type used for normal CAS content so traffic can easily be differentiated
        public static readonly HashType HashType = HashType.MD5;
        protected FileSystemContentStoreInternal PrivateCas { get; }

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(CachingCentralStorage));

        /// <inheritdoc />
        protected override string PreprocessStorageId(string storageId) => storageId;

        /// <nodoc />
        public CachingCentralStorage(
            DistributedCentralStoreConfiguration configuration,
            CentralStorage fallbackStorage,
            IAbsFileSystem fileSystem)
        {
            Configuration = configuration;
            _fallbackStorage = fallbackStorage;

            var maxRetentionMb = (int)Math.Ceiling(configuration.MaxRetentionGb * 1024);
            var softRetentionMb = (int)(maxRetentionMb * 0.8);

            var cacheFolder = configuration.CacheRoot / CacheSubFolderName;

            // Create a private CAS for storing checkpoint data
            // Avoid introducing churn into primary CAS
            PrivateCas = new FileSystemContentStoreInternal(
                fileSystem,
                SystemClock.Instance,
                cacheFolder,
                new ConfigurationModel(
                    new ContentStoreConfiguration(new MaxSizeQuota(hardExpression: maxRetentionMb + "MB", softExpression: softRetentionMb + "MB")),
                    ConfigurationSelection.RequireAndUseInProcessConfiguration),
                settings: new ContentStoreSettings()
                {
                    TraceFileSystemContentStoreDiagnosticMessages = Configuration.TraceFileSystemContentStoreDiagnosticMessages,
                    SelfCheckSettings = Configuration.SelfCheckSettings,
                });
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            await PrivateCas.StartupAsync(context).ThrowIfFailure();

            return await base.StartupCoreAsync(context);
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            await PrivateCas.ShutdownAsync(context).ThrowIfFailure();

            return await base.ShutdownCoreAsync(context);
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> TouchBlobCoreAsync(OperationContext context, AbsolutePath file, string storageId, bool isUploader, bool isImmutable)
        {
            var (hash, fallbackStorageId) = ParseCompositeStorageId(storageId);

            // Need to touch in fallback storage as well so it knows the content is still in use
            var touchTask = _fallbackStorage.TouchBlobAsync(context, file, fallbackStorageId, isUploader, isImmutable).ThrowIfFailure();

            // Ensure content is present in private CAS
            var putTask = PutFileAsync(context, file, hash, isImmutable);

            await Task.WhenAll(touchTask, putTask);

            return BoolResult.Success;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> TryGetFileCoreAsync(OperationContext context, string storageId, AbsolutePath targetFilePath, bool isImmutable)
        {
            // Get the content from peers or fallback
            return await TryGetAndPutFileAsync(context, storageId, targetFilePath, isImmutable);
        }

        /// <inheritdoc />
        protected override async Task<Result<string>> UploadFileCoreAsync(OperationContext context, AbsolutePath file, string blobName, bool garbageCollect = false)
        {
            // Add the file to CAS and register with global content location store.
            var putResult = await PutFileAsync(context, file, hash: null, isUpload: true);

            string fallbackStorageId = await _fallbackStorage.UploadFileAsync(
                context,
                file,
                name: $"{blobName}.{putResult.ContentHash.Serialize(delimiter: '.')}",
                garbageCollect).ThrowIfFailureAsync();

            return CreateCompositeStorageId(putResult.ContentHash, fallbackStorageId);
        }

        protected virtual async Task<Result<ContentHashWithSize>> TryGetAndPutFileAsync(OperationContext context, string storageId, AbsolutePath targetFilePath, bool isImmutable)
        {
            var (hash, fallbackStorageId) = ParseCompositeStorageId(storageId);
            if (hash != null)
            {
                var fileAccessMode = isImmutable ? FileAccessMode.ReadOnly : FileAccessMode.Write;
                var fileRealizationMode = isImmutable ? FileRealizationMode.Any : FileRealizationMode.Copy;

                if (PrivateCas.Contains(hash.Value) || await TryRetrieveFromExternalCacheAsync(context, hash.Value))
                {
                    // First attempt to place file from content store
                    var placeResult = await PrivateCas.PlaceFileAsync(context, hash.Value, targetFilePath, fileAccessMode, FileReplacementMode.ReplaceExisting, fileRealizationMode, pinRequest: null);
                    if (placeResult.IsPlaced())
                    {
                        return Result.Success(new ContentHashWithSize(hash.Value, placeResult.FileSize));
                    }
                }
            }

            Counters[CentralStorageCounters.TryGetFileFromFallback].Increment();
            return await TryGetFromFallbackAndPutAsync(context, targetFilePath, fallbackStorageId, isImmutable);
        }

        protected async override Task<BoolResult> PruneInternalCacheCoreAsync(OperationContext context, string storageId)
        {
            var (hash, fallbackStorageId) = ParseCompositeStorageId(storageId);
            if (hash is null)
            {
                return new BoolResult(errorMessage: $"Could not parse content hash from storage id `{storageId}`");
            }

            return await PrivateCas.DeleteAsync(context, hash.Value, new Interfaces.Stores.DeleteContentOptions()
            {
                DeleteLocalOnly = true,
            });
        }

        protected virtual Task<bool> TryRetrieveFromExternalCacheAsync(OperationContext context, ContentHash hash)
        {
            return BoolTask.False;
        }

        private string CreateCompositeStorageId(ContentHash hash, string fallbackStorageId)
        {
            // Storage id format:
            // {Hash}{StorageIdSeparator}{fallbackStorageId}
            return string.Join(StorageIdSeparator, hash, fallbackStorageId);
        }

        internal static (ContentHash? hash, string fallbackStorageId) ParseCompositeStorageId(string storageId)
        {
            if (storageId.Contains(StorageIdSeparator))
            {
                // Storage id is a composite id. Split out parts
                var parts = storageId.Split(new[] { StorageIdSeparator }, StringSplitOptions.None);
                Contract.Assert(parts.Length == 2);
                return (hash: new ContentHash(parts[0]), fallbackStorageId: parts[1]);
            }
            else
            {
                // Storage id is not a composite id. This happens when we get ids from when the distributed central
                // storage is disabled. Just return the full storage id as the fallback storage id
                return (hash: null, fallbackStorageId: storageId);
            }
        }

        /// <summary>
        /// Try to get from the fallback and put in the CAS
        /// </summary>
        private async Task<ContentHashWithSize> TryGetFromFallbackAndPutAsync(OperationContext context, AbsolutePath targetFilePath, string fallbackStorageId, bool isImmutable)
        {
            // In the success case the content will be put at targetFilePath
            await _fallbackStorage.TryGetFileAsync(context, fallbackStorageId, targetFilePath, isImmutable).ThrowIfFailure();

            var placementFileRealizationMode = isImmutable ? FileRealizationMode.Any : FileRealizationMode.Copy;
            var putResult = await PrivateCas.PutFileAsync(context, targetFilePath, placementFileRealizationMode, HashType, pinRequest: null).ThrowIfFailure();

            return new ContentHashWithSize(putResult.ContentHash, putResult.ContentSize);
        }

        protected virtual async Task<PutResult> PutFileAsync(OperationContext context, AbsolutePath file, ContentHash? hash, bool isImmutable = false, bool isUpload = false)
        {
            var putFileRealizationMode = isImmutable ? FileRealizationMode.Any : FileRealizationMode.Copy;

            PutResult putResult;
            if (hash != null)
            {
                putResult = await PrivateCas.PutFileAsync(context, file, putFileRealizationMode, hash.Value, pinRequest: null).ThrowIfFailure();
            }
            else
            {
                putResult = await PrivateCas.PutFileAsync(context, file, putFileRealizationMode, HashType, pinRequest: null).ThrowIfFailure();
            }

            return putResult;
        }

        /// <summary>
        /// Tries to get the content hash and size based on the storage id
        /// </summary>
        public bool TryGetContentInfo(string storageId, out ContentHash hash, out long size)
        {
            var parsed = ParseCompositeStorageId(storageId);
            if (parsed.hash != null)
            {
                hash = parsed.hash.Value;
                return PrivateCas.Contains(hash, out size);
            }

            hash = default;
            size = default;
            return false;
        }

        /// <summary>
        /// Opens stream to content in inner content store
        /// </summary>
        public virtual Task<OpenStreamResult> StreamContentAsync(Context context, ContentHash contentHash)
        {
            return PrivateCas.OpenStreamAsync(context, contentHash, pinRequest: null);
        }

        /// <summary>
        /// Checks whether the inner content store has the content
        /// </summary>
        public virtual bool HasContent(ContentHash contentHash)
        {
            return PrivateCas.Contains(contentHash);
        }
    }
}
