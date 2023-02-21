// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Net;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using Microsoft.WindowsAzure.Storage.Blob;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Blobs
{
    /// <summary>
    /// Configuration for <see cref="AzureBlobStorageContentStore"/>.
    /// </summary>
    public sealed class AzureBlobStorageContentStoreConfiguration
    {
        public AzureBlobStorageCredentials Credentials { get; init; } = AzureBlobStorageCredentials.StorageEmulator;

        public string ContainerName { get; init; } = "default";

        public string FolderName { get; init; } = "content/default";

        public TimeSpan StorageInteractionTimeout { get; init; } = TimeSpan.FromMinutes(30);

        public AzureBlobStorageContentSession.BulkPinStrategy BulkPinStrategy { get; init; } = AzureBlobStorageContentSession.BulkPinStrategy.Individual;

        public RetryOptions RetryOptions { get; set; } = ClientOptions.Default.Retry;
    }

    /// <summary>
    /// A <see cref="IContentStore"/> implementation backed by azure storage.
    /// </summary>
    public class AzureBlobStorageContentStore : StartupShutdownBase, IContentStore
    {
        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(AzureBlobStorageContentStore));

        private readonly AzureBlobStorageContentStoreConfiguration _configuration;

        private readonly BlobServiceClient _blobClient;
        private readonly BlobContainerClient _blobContainer;

        /// <nodoc />
        public AzureBlobStorageContentStore(AzureBlobStorageContentStoreConfiguration configuration)
        {
            _configuration = configuration;

            var options = CreateBlobClientOptions(configuration);
            _blobClient = _configuration.Credentials.CreateBlobServiceClient(options);
            _blobContainer = _blobClient.GetBlobContainerClient(_configuration.ContainerName);
        }

        private static BlobClientOptions CreateBlobClientOptions(AzureBlobStorageContentStoreConfiguration configuration)
        {
            var options = new BlobClientOptions(BlobClientOptions.ServiceVersion.V2021_02_12);
            // Copying the options because we can't provide them during construction.
            var retryOptions = configuration.RetryOptions;
            options.Retry.MaxDelay = retryOptions.MaxDelay;
            options.Retry.Mode = retryOptions.Mode;
            options.Retry.Delay = retryOptions.Delay;
            options.Retry.MaxRetries = retryOptions.MaxRetries;
            options.Retry.NetworkTimeout = retryOptions.NetworkTimeout;
            return options;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            return await EnsureContainerExists(context);
        }

        private Task<Result<bool>> EnsureContainerExists(OperationContext context)
        {
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                async context =>
                {
                    // The response is null if container doesn't exist.
                    bool exists = await _blobContainer.CreateIfNotExistsAsync(cancellationToken: context.Token) != null;
                    return Result.Success(exists);
                },
                traceOperationStarted: false,
                extraEndMessage: r =>
                {
                    var msg = $"Container=[{_configuration.ContainerName}]";

                    if (!r.Succeeded)
                    {
                        return msg;
                    }

                    return $"{msg} Created=[{r.Value}]";
                },
                timeout: _configuration.StorageInteractionTimeout);
        }

        /// <inheritdoc />
        public CreateSessionResult<IReadOnlyContentSession> CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin)
        {
            using var guard = TrackShutdown(context, default);
            var operationContext = guard.Context;

            return operationContext.PerformOperation(Tracer, () =>
            {
                return new CreateSessionResult<IReadOnlyContentSession>(CreateSessionCore(name, implicitPin));
            },
            traceOperationStarted: false,
            messageFactory: _ => $"Name=[{name}] ImplicitPin=[{implicitPin}]");
        }

        /// <inheritdoc />
        public CreateSessionResult<IContentSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
        {
            using var guard = TrackShutdown(context, default);
            var operationContext = guard.Context;

            return operationContext.PerformOperation(Tracer, () =>
            {
                return new CreateSessionResult<IContentSession>(CreateSessionCore(name, implicitPin));
            },
            traceOperationStarted: false,
            messageFactory: _ => $"Name=[{name}] ImplicitPin=[{implicitPin}]");
        }

        private IContentSession CreateSessionCore(string name, ImplicitPin implicitPin)
        {
            return new AzureBlobStorageContentSession(
                new AzureBlobStorageContentSession.Configuration(
                    Name: name,
                    ImplicitPin: implicitPin,
                    StorageInteractionTimeout: _configuration.StorageInteractionTimeout,
                    BulkPinStrategy: _configuration.BulkPinStrategy),
                store: this);
        }

        /// <inheritdoc />
        public Task<DeleteResult> DeleteAsync(Context context, ContentHash contentHash, DeleteContentOptions? deleteOptions)
        {
            return Task.FromResult(new DeleteResult(DeleteResult.ResultCode.ContentNotDeleted, contentHash, -1));
        }

        /// <inheritdoc />
        public Task<GetStatsResult> GetStatsAsync(Context context)
        {
            return Task.FromResult(new GetStatsResult(errorMessage: $"{nameof(AzureBlobStorageContentStore)} does not support {nameof(GetStatsAsync)}"));
        }

        /// <inheritdoc />
        public void PostInitializationCompleted(Context context, BoolResult result)
        {
            // Unused on purpose
        }

        internal BlobBatchClient GetBlobBatchClient()
        {
            return _blobContainer.GetBlobBatchClient();
        }

        internal BlobClient GetBlobClient(ContentHash contentHash)
        {
            return _blobContainer.GetBlobClient($"{_configuration.FolderName}/{contentHash}.blob");
        }
    }
}
