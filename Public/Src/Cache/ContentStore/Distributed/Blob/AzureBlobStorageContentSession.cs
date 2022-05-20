// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
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
    internal sealed class AzureBlobStorageContentSession : ContentSessionBase
    {
        public record Configuration(
            string Name,
            ImplicitPin ImplicitPin,
            AzureBlobStorageContentStore Parent,
            TimeSpan StorageInteractionTimeout)
        {
        }

        protected override Tracer Tracer { get; } = new Tracer(nameof(AzureBlobStorageContentSession));

        private readonly Configuration _configuration;
        private readonly IContentSession _innerContentSession;

        private readonly IAbsFileSystem _fileSystem = PassThroughFileSystem.Default;

        private static readonly BlobRequestOptions DefaultBlobStorageRequestOptions = new BlobRequestOptions()
        {
            RetryPolicy = new Microsoft.WindowsAzure.Storage.RetryPolicies.ExponentialRetry(),
        };

        public static AzureBlobStorageContentSession Create(Context context, Configuration configuration)
        {
            var contentSession = configuration
                .Parent
                .ContentStore
                .CreateSession(
                    context,
                    $"{configuration.Name}-inner",
                    // We don't pin inside the inner content session. It is just used to ensure correctness when placing / putting files
                    ImplicitPin.None)
                .ThrowIfFailure();

            return new AzureBlobStorageContentSession(configuration, contentSession.Session!);
        }

        private AzureBlobStorageContentSession(Configuration configuration, IContentSession contentSession)
            : base(configuration.Name)
        {
            _configuration = configuration;
            _innerContentSession = contentSession;
        }

        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            await _innerContentSession.StartupAsync(context).ThrowIfFailure();
            return await base.StartupCoreAsync(context);
        }

        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            await _innerContentSession.ShutdownAsync(context).ThrowIfFailure();
            return await base.ShutdownCoreAsync(context);
        }

        #region IContentSession Implementation

        protected override Task<PinResult> PinCoreAsync(
            OperationContext context,
            ContentHash contentHash,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            return PinRemoteAsync(context, contentHash);
        }

        protected override async Task<IEnumerable<Task<Indexed<PinResult>>>> PinCoreAsync(
            OperationContext context,
            IReadOnlyList<ContentHash> contentHashes,
            UrgencyHint urgencyHint,
            Counter retryCounter,
            Counter fileCounter)
        {
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

        protected override Task<OpenStreamResult> OpenStreamCoreAsync(
            OperationContext context,
            ContentHash contentHash,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            return RunWithRecovery(
                () => _innerContentSession.OpenStreamAsync(
                    context,
                    contentHash,
                    context.Token,
                    urgencyHint),
                () => DownloadIntoSession(context, contentHash),
                shouldAttemptRecovery: r => r.Code == OpenStreamResult.ResultCode.ContentNotFound,
                // If we fail to download into the local content session, we will just return that the content has not
                // been found.
                tryConvertRecoveryError: r => new OpenStreamResult(
                                             other: r,
                                             code: OpenStreamResult.ResultCode.ContentNotFound,
                                             message: "Failed to obtain content from Azure Storage"));
        }

        protected override Task<PlaceFileResult> PlaceFileCoreAsync(
            OperationContext context,
            ContentHash contentHash,
            AbsolutePath path,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            return RunWithRecovery(
                () => _innerContentSession.PlaceFileAsync(
                    context,
                    contentHash,
                    path,
                    accessMode,
                    replacementMode,
                    realizationMode,
                    context.Token,
                    urgencyHint),
                () => DownloadIntoSession(context, contentHash),
                shouldAttemptRecovery: r => r.Code == PlaceFileResult.ResultCode.NotPlacedContentNotFound,
                // If we fail to download into the local content session, we will just return that the content has not
                // been found.
                tryConvertRecoveryError: r => new PlaceFileResult(
                                             other: r,
                                             code: PlaceFileResult.ResultCode.NotPlacedContentNotFound,
                                             message: "Failed to obtain content from Azure Storage"));
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
            // We can't hash while uploading because the name of the blob must be determined before uploading, so we
            // need to hash before.
            var putResult = await _innerContentSession.PutFileAsync(context, hashType, path, realizationMode, context.Token).ThrowIfFailureAsync();
            var contentHash = putResult.ContentHash;
            var openStreamResult = await _innerContentSession.OpenStreamAsync(context, contentHash, context.Token).ThrowIfFailureAsync();

            using var streamWithLength = openStreamResult.StreamWithLength!.Value;
            return await UploadFromStreamAsync(
                context,
                contentHash,
                streamWithLength.Stream,
                streamWithLength.Length);
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
            var putResult = await _innerContentSession.PutStreamAsync(context, hashType, stream, context.Token).ThrowIfFailureAsync();
            var contentHash = putResult.ContentHash;
            var openStreamResult = await _innerContentSession.OpenStreamAsync(context, contentHash, context.Token).ThrowIfFailureAsync();

            using var streamWithLength = openStreamResult.StreamWithLength!.Value;
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

                    try
                    {
                        await reference.FetchAttributesAsync(
                            accessCondition: AccessCondition.GenerateEmptyCondition(),
                            options: DefaultBlobStorageRequestOptions,
                            operationContext: null,
                            cancellationToken: context.Token);
                    }
                    catch (StorageException exception)
                    {
                        // See: https://docs.microsoft.com/en-us/rest/api/storageservices/blob-service-error-codes
                        // See: https://docs.microsoft.com/en-us/rest/api/storageservices/Specifying-Conditional-Headers-for-Blob-Service-Operations#Subheading3
                        if (exception.RequestInformation.HttpStatusCode == (int)HttpStatusCode.NotFound
                            || exception.RequestInformation.ErrorCode == "BlobNotFound")
                        {
                            return PinResult.ContentNotFound;
                        }
                        else
                        {
                            throw;
                        }
                    }

                    return new PinResult(
                        code: PinResult.ResultCode.Success,
                        lastAccessTime: reference.Properties.LastModified?.UtcDateTime,
                        contentSize: reference.Properties.Length);
                },
                traceOperationStarted: false,
                timeout: _configuration.StorageInteractionTimeout);
        }

        private Task<PutResult> DownloadIntoSession(
            OperationContext context,
            ContentHash contentHash)
        {
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                async context =>
                {
                    var reference = GetCloudBlockBlobReference(contentHash);

                    var readStream = await reference.OpenReadAsync(
                        accessCondition: null,
                        options: DefaultBlobStorageRequestOptions,
                        operationContext: null,
                        cancellationToken: context.Token);

                    return await _innerContentSession.PutStreamAsync(
                        context,
                        contentHash,
                        readStream,
                        context.Token);
                },
                traceOperationStarted: false,
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

        private async Task<TResult> RunWithRecovery<TResult, TRecovery>(
            Func<Task<TResult>> attempt,
            Func<Task<TRecovery>> recover,
            Func<TResult, bool> shouldAttemptRecovery,
            Func<TRecovery, TResult?> tryConvertRecoveryError)
            where TResult : ResultBase
            where TRecovery : ResultBase
        {
            var result = await attempt();
            if (result.Succeeded || !shouldAttemptRecovery(result))
            {
                return result;
            }

            var recovery = await recover();
            if (!recovery.Succeeded)
            {
                var converted = tryConvertRecoveryError(recovery);
                if (converted is null)
                {
                    recovery.ThrowIfFailure();
                }

                return converted!;
            }

            return await attempt();
        }

        private CloudBlockBlob GetCloudBlockBlobReference(ContentHash contentHash)
        {
            return _configuration.Parent.GetCloudBlockBlobReference(contentHash);
        }
    }
}
