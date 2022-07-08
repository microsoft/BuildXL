// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Blobs
{
    public sealed class AzureBlobStorageContentSession : ContentSessionBase
    {
        public record Configuration(
            string Name,
            ImplicitPin ImplicitPin,
            AzureBlobStorageContentStore Parent,
            TimeSpan StorageInteractionTimeout,
            BlobDownloadStrategyConfiguration BlobDownloadStrategyConfiguration)
        {
        }

        protected override Tracer Tracer { get; } = new Tracer(nameof(AzureBlobStorageContentSession));

        private readonly Configuration _configuration;

        private readonly IAbsFileSystem _fileSystem = PassThroughFileSystem.Default;

        private readonly IClock _clock = SystemClock.Instance;

        private static readonly BlobRequestOptions DefaultBlobStorageRequestOptions = new BlobRequestOptions()
        {
            RetryPolicy = new Microsoft.WindowsAzure.Storage.RetryPolicies.ExponentialRetry(),
        };

        private readonly IBlobDownloadStrategy _downloadStrategy;

        public AzureBlobStorageContentSession(Configuration configuration)
            : base(configuration.Name)
        {
            _configuration = configuration;
            _downloadStrategy = BlobDownloadStrategyFactory.Create(configuration.BlobDownloadStrategyConfiguration);
        }

        #region IContentSession Implementation

        protected override Task<PinResult> PinCoreAsync(
            OperationContext context,
            ContentHash contentHash,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            if (contentHash.IsEmptyHash())
            {
                return Task.FromResult(new PinResult(PinResult.ResultCode.Success));
            }

            return PinRemoteAsync(context, contentHash);
        }

        protected override async Task<IEnumerable<Task<Indexed<PinResult>>>> PinCoreAsync(
            OperationContext context,
            IReadOnlyList<ContentHash> contentHashes,
            UrgencyHint urgencyHint,
            Counter retryCounter,
            Counter fileCounter)
        {
            // TODO: we can use blob batch API calls for deleting blobs using IfModifiedSince in the future for pinning in bulk
            var tasks = contentHashes.WithIndices().Select((tuple, _) =>
            {
                var (contentHash, index) = tuple;

                return PinCoreAsync(
                    context,
                    contentHash,
                    urgencyHint,
                    retryCounter).WithIndexAsync(index);
            }, Unit.Void);

            await TaskUtilities.SafeWhenAll(tasks);
            return tasks;
        }

        protected override async Task<OpenStreamResult> OpenStreamCoreAsync(
            OperationContext context,
            ContentHash contentHash,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            if (contentHash.IsEmptyHash())
            {
                return new OpenStreamResult(new MemoryStream(Array.Empty<byte>()).WithLength(0));
            }

            var stream = await OpenRemoteStreamAsync(context, contentHash).ThrowIfFailureAsync();
            return new OpenStreamResult(stream);
        }

        private Task<Result<StreamWithLength?>> OpenRemoteStreamAsync(OperationContext context, ContentHash contentHash)
        {
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                async context =>
                {
                    var reference = GetCloudBlockBlobReference(contentHash);

                    Stream readStream;
                    try
                    {
                        readStream = await reference.OpenReadAsync(
                            accessCondition: null,
                            options: DefaultBlobStorageRequestOptions,
                            operationContext: null,
                            cancellationToken: context.Token);
                    }
                    catch (StorageException exception)
                    {
                        // See: https://docs.microsoft.com/en-us/rest/api/storageservices/blob-service-error-codes
                        // See: https://docs.microsoft.com/en-us/rest/api/storageservices/Specifying-Conditional-Headers-for-Blob-Service-Operations#Subheading3
                        if (exception.RequestInformation.HttpStatusCode == (int)HttpStatusCode.NotFound)
                        {
                            return Result.Success<StreamWithLength?>(null, isNullAllowed: true);
                        }
                        else
                        {
                            throw;
                        }
                    }

                    return Result.Success<StreamWithLength?>(readStream.WithLength(readStream.Length));
                },
                traceOperationStarted: false,
                timeout: _configuration.StorageInteractionTimeout,
                extraEndMessage: r =>
                {
                    long size = -1;
                    if (r.Succeeded && r.Value is not null)
                    {
                        size = r.Value.Value.Length;
                    }

                    return $"ContentHash=[{contentHash}] Size=[{size}]";
                });
        }

        protected override async Task<PlaceFileResult> PlaceFileCoreAsync(
            OperationContext context,
            ContentHash contentHash,
            AbsolutePath path,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            if (replacementMode is FileReplacementMode.SkipIfExists or FileReplacementMode.FailIfExists && _fileSystem.FileExists(path))
            {
                return new PlaceFileResult(PlaceFileResult.ResultCode.NotPlacedAlreadyExists);
            }

            if (contentHash.IsEmptyHash())
            {
                _fileSystem.CreateEmptyFile(path);
                return new PlaceFileResult(PlaceFileResult.ResultCode.PlacedWithCopy, fileSize: 0);
            }

            var remoteDownloadResult = await PlaceRemoteFileAsync(context, contentHash, path, accessMode, replacementMode).ThrowIfFailureAsync();
            return new PlaceFileResult(remoteDownloadResult.ResultCode, remoteDownloadResult.FileSize ?? 0, source: PlaceFileResult.Source.BackingStore);
        }
        
        private Task<Result<RemoteDownloadResult>> PlaceRemoteFileAsync(OperationContext context, ContentHash contentHash, AbsolutePath path, FileAccessMode accessMode, FileReplacementMode replacementMode)
        {
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                async context =>
                {
                    var reference = GetCloudBlockBlobReference(contentHash);

                    var sasUrlQuery = reference.GetSharedAccessSignature(new SharedAccessBlobPolicy()
                    {
                        Permissions = SharedAccessBlobPermissions.Read,
                        SharedAccessExpiryTime = _clock.UtcNow + TimeSpan.FromDays(1),
                    });

                    var downloadUrl = reference.Uri.AbsoluteUri + sasUrlQuery;

                    var downloadRequest = new RemoteDownloadRequest()
                    {
                        ContentHash = contentHash,
                        AbsolutePath = path,
                        Reference = reference,
                        DownloadUrl = downloadUrl,
                    };

                    RemoteDownloadResult remoteDownloadResult;
                    try
                    {
                        remoteDownloadResult = await _downloadStrategy.DownloadAsync(context, downloadRequest);
                    }
                    catch
                    {
                        _fileSystem.DeleteFile(downloadRequest.AbsolutePath);
                        throw;
                    }

                    return Result.Success(remoteDownloadResult);
                },
                traceOperationStarted: false,
                timeout: _configuration.StorageInteractionTimeout,
                extraEndMessage: r =>
                {
                    var baseline = $"ContentHash=[{contentHash.ToShortString()}] Path=[{path}] AccessMode=[{accessMode}] ReplacementMode=[{replacementMode}]";
                    if (!r.Succeeded)
                    {
                        return baseline;
                    }

                    var d = r.Value;
                    return $"{baseline} {r.Value}";
                });
        }

        protected override async Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileCoreAsync(
            OperationContext context,
            IReadOnlyList<ContentHashWithPath> hashesWithPaths,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            var tasks = hashesWithPaths.WithIndices().Select((tuple, _) =>
            {
                var (contentHashWithPath, index) = tuple;

                return PlaceFileCoreAsync(
                    context,
                    contentHashWithPath.Hash,
                    contentHashWithPath.Path,
                    accessMode,
                    replacementMode,
                    realizationMode,
                    urgencyHint,
                    retryCounter).WithIndexAsync(index);
            }, Unit.Void);

            await TaskUtilities.SafeWhenAll(tasks);
            return tasks;
        }

        protected override async Task<PutResult> PutFileCoreAsync(
            OperationContext context,
            HashType hashType,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            using var streamWithLength = _fileSystem.Open(
                path,
                FileAccess.Read,
                FileMode.Open,
                FileShare.Read,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await PutStreamCoreAsync(context, hashType, streamWithLength.Stream, urgencyHint, retryCounter);
        }

        protected override async Task<PutResult> PutFileCoreAsync(
            OperationContext context,
            ContentHash contentHash,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            using var streamWithLength = _fileSystem.Open(
                path,
                FileAccess.Read,
                FileMode.Open,
                FileShare.Read,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await UploadFromStreamAsync(
                context,
                contentHash,
                streamWithLength.Stream,
                streamWithLength.Length);
        }

        protected override Task<PutResult> PutStreamCoreAsync(
            OperationContext context,
            ContentHash contentHash,
            Stream stream,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            // This is on purpose because we don't know what the stream implementation is here.
            long contentSize = -1;
            try
            {
                contentSize = stream.Length;
            }
            catch
#pragma warning disable ERP022 // Unobserved exception in a generic exception handler
            {
            }
#pragma warning restore ERP022 // Unobserved exception in a generic exception handler

            return UploadFromStreamAsync(context, contentHash, stream, contentSize);
        }

        protected override async Task<PutResult> PutStreamCoreAsync(
            OperationContext context,
            HashType hashType,
            Stream stream,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            // We can't hash while uploading because the name of the blob must be determined before uploading, so we
            // need to hash before.
            var position = stream.Position;
            var streamWithLength = stream.WithLength(stream.Length - position);
            var contentHash = await HashInfoLookup.GetContentHasher(hashType).GetContentHashAsync(streamWithLength);
            stream.Position = position;

            return await UploadFromStreamAsync(
                context,
                contentHash,
                streamWithLength.Stream,
                streamWithLength.Length);
        }

#endregion

        private Task<PinResult> PinRemoteAsync(
            OperationContext context,
            ContentHash contentHash)
        {
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                async context => {
                    var reference = GetCloudBlockBlobReference(contentHash);

                    bool exists = await reference.ExistsAsync(
                            options: DefaultBlobStorageRequestOptions,
                            operationContext: null,
                            cancellationToken: context.Token);

                    if (exists)
                    {
                        return new PinResult(
                            code: PinResult.ResultCode.Success,
                            lastAccessTime: reference.Properties.LastModified?.UtcDateTime,
                            contentSize: reference.Properties.Length);
                    }
                    else
                    {
                        return PinResult.ContentNotFound;
                    }
                },
                traceOperationStarted: false,
                extraEndMessage: _ => $"ContentHash=[{contentHash}]",
                timeout: _configuration.StorageInteractionTimeout);
        }

        private Task<PutResult> UploadFromStreamAsync(
            OperationContext context,
            ContentHash contentHash,
            Stream stream,
            long contentSize)
        {
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                async context =>
                {
                    var reference = GetCloudBlockBlobReference(contentHash);

                    // WARNING: remember implicit pin

                    // TODO: bandwidth checking?
                    // TODO: timeouts
                    bool contentAlreadyExistsInCache = false;
                    try
                    {
                        // TODO: setup parallel upload by using storage options (ParallelOperationThreadCount)
                        // TODO: setup cancellation time via storage options (MaximumExecutionTime / ServerTimeout)
                        // TODO: ideally here we'd also hash in the bg and cancel the op if it turns out the hash
                        // doesn't match as a protective measure against trusted puts with the wrong hash.
                        await reference.UploadFromStreamAsync(
                            stream,
                            accessCondition: AccessCondition.GenerateIfNotExistsCondition(),
                            options: DefaultBlobStorageRequestOptions,
                            operationContext: null,
                            cancellationToken: context.Token);
                    }
                    catch (StorageException exception)
                    {
                        // See: https://docs.microsoft.com/en-us/rest/api/storageservices/blob-service-error-codes
                        // See: https://docs.microsoft.com/en-us/rest/api/storageservices/Specifying-Conditional-Headers-for-Blob-Service-Operations#Subheading3
                        if (exception.RequestInformation.HttpStatusCode == (int)HttpStatusCode.PreconditionFailed
                            || exception.RequestInformation.ErrorCode == "BlobAlreadyExists")
                        {
                            contentAlreadyExistsInCache = true;
                        }
                        else
                        {
                            throw;
                        }
                    }

                    return new PutResult(contentHash, (int)contentSize, contentAlreadyExistsInCache);
                },
                traceOperationStarted: false,
                timeout: _configuration.StorageInteractionTimeout);
        }

        private CloudBlockBlob GetCloudBlockBlobReference(ContentHash contentHash)
        {
            return _configuration.Parent.GetCloudBlockBlobReference(contentHash);
        }
    }
}
