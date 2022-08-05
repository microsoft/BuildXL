// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Net;
using System.Threading.Tasks;
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
    public class AzureBlobStorageContentStoreConfiguration
    {
        public AzureBlobStorageCredentials Credentials { get; set; } = AzureBlobStorageCredentials.StorageEmulator;

        public string ContainerName { get; set; } = "default";

        public string FolderName { get; set; } = "content/default";

        public TimeSpan StorageInteractionTimeout { get; set; } = TimeSpan.FromMinutes(30);

        public BlobDownloadStrategyConfiguration BlobDownloadStrategyConfiguration { get; set; } = new BlobDownloadStrategyConfiguration();

        public AzureBlobStorageContentSession.BulkPinStrategy BulkPinStrategy { get; set; } = AzureBlobStorageContentSession.BulkPinStrategy.Individual;
    }

    public class AzureBlobStorageContentStore : StartupShutdownBase, IContentStore
    {
        protected override Tracer Tracer { get; } = new Tracer(nameof(AzureBlobStorageContentStore));

        private readonly AzureBlobStorageContentStoreConfiguration _configuration;

        private readonly CloudBlobClient _v9Client;
        private readonly CloudBlobContainer _v9Container;
        private readonly CloudBlobDirectory _v9Directory;

        private readonly BlobServiceClient _v12Client;
        private readonly BlobContainerClient _v12Container;

        public AzureBlobStorageContentStore(AzureBlobStorageContentStoreConfiguration configuration)
        {
            _configuration = configuration;

            _v9Client = _configuration.Credentials!.CreateCloudBlobClient();
            _v9Container = _v9Client.GetContainerReference(_configuration.ContainerName);
            _v9Directory = _v9Container.GetDirectoryReference(_configuration.FolderName);

            _v12Client = _configuration.Credentials!.CreateBlobServiceClient(new BlobClientOptions(Azure.Storage.Blobs.BlobClientOptions.ServiceVersion.V2021_02_12));
            _v12Container = _v12Client.GetBlobContainerClient(_configuration.ContainerName);
        }

        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            ApplyServicePointSettings();

            return await EnsureContainerExists(context);
        }

        private void ApplyServicePointSettings()
        {
            // The following is used only pre-.NET Core, but it is important to set these for those usages.

#pragma warning disable SYSLIB0014 // Type or member is obsolete
            var servicePoint = ServicePointManager.FindServicePoint(_v9Client.BaseUri);
#pragma warning restore SYSLIB0014 // Type or member is obsolete

            // See: https://github.com/Azure/azure-storage-net-data-movement#best-practice
            var connectionLimit = Environment.ProcessorCount * 8;
            if (servicePoint.ConnectionLimit < connectionLimit)
            {
                servicePoint.ConnectionLimit = connectionLimit;
            }
            servicePoint.UseNagleAlgorithm = false;
            servicePoint.Expect100Continue = false;
        }

        internal Task<Result<bool>> EnsureContainerExists(OperationContext context)
        {
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                async context =>
                {
                    return Result.Success(await _v9Container.CreateIfNotExistsAsync(
                        accessType: BlobContainerPublicAccessType.Off,
                        options: null,
                        operationContext: null,
                        cancellationToken: context.Token));
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

        public CreateSessionResult<IReadOnlyContentSession> CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin)
        {
            using var guard = TrackShutdown(context, default);
            var operationContext = guard.Context;

            return operationContext.PerformOperation<CreateSessionResult<IReadOnlyContentSession>>(Tracer, () =>
            {
                return new CreateSessionResult<IReadOnlyContentSession>(CreateSessionCore(context, name, implicitPin));
            },
            traceOperationStarted: false,
            messageFactory: _ => $"Name=[{name}] ImplicitPin=[{implicitPin}]");
        }

        public CreateSessionResult<IContentSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
        {
            using var guard = TrackShutdown(context, default);
            var operationContext = guard.Context;

            return operationContext.PerformOperation<CreateSessionResult<IContentSession>>(Tracer, () =>
            {
                return new CreateSessionResult<IContentSession>(CreateSessionCore(context, name, implicitPin));
            },
            traceOperationStarted: false,
            messageFactory: _ => $"Name=[{name}] ImplicitPin=[{implicitPin}]");
        }

        private IContentSession CreateSessionCore(Context context, string name, ImplicitPin implicitPin)
        {
            return new AzureBlobStorageContentSession(
                new AzureBlobStorageContentSession.Configuration(
                    Name: name,
                    ImplicitPin: implicitPin,
                    Parent: this,
                    StorageInteractionTimeout: _configuration.StorageInteractionTimeout,
                    BlobDownloadStrategyConfiguration: _configuration.BlobDownloadStrategyConfiguration,
                    BulkPinStrategy: _configuration.BulkPinStrategy));
        }

        public Task<DeleteResult> DeleteAsync(Context context, ContentHash contentHash, DeleteContentOptions? deleteOptions)
        {
            return Task.FromResult(new DeleteResult(DeleteResult.ResultCode.ContentNotDeleted, contentHash, -1));
        }

        public Task<GetStatsResult> GetStatsAsync(Context context)
        {
            return Task.FromResult(new GetStatsResult(errorMessage: $"{nameof(AzureBlobStorageContentStore)} does not support {nameof(GetStatsAsync)}"));
        }

        public void PostInitializationCompleted(Context context, BoolResult result)
        {
            // Unused on purpose
        }

        internal CloudBlockBlob GetCloudBlockBlobReference(ContentHash contentHash)
        {
            return _v9Directory.GetBlockBlobReference($"{contentHash}.blob");
        }

        internal BlobBatchClient GetBlobBatchClient()
        {
            return _v12Container.GetBlobBatchClient();
        }

        internal BlobClient GetBlobClient(ContentHash contentHash)
        {
            return _v12Container.GetBlobClient($"{_configuration.FolderName}/{contentHash}.blob");
        }
    }
}
