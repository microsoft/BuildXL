// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
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
    }

    public class AzureBlobStorageContentStore : StartupShutdownBase, IContentStore
    {
        protected override Tracer Tracer { get; } = new Tracer(nameof(AzureBlobStorageContentStore));

        private readonly AzureBlobStorageContentStoreConfiguration _configuration;

        private readonly CloudBlobClient _client;
        private readonly CloudBlobContainer _container;
        private readonly CloudBlobDirectory _directory;

        public AzureBlobStorageContentStore(AzureBlobStorageContentStoreConfiguration configuration)
        {
            _configuration = configuration;

            _client = _configuration.Credentials!.CreateCloudBlobClient();
            _container = _client.GetContainerReference(_configuration.ContainerName);
            _directory = _container.GetDirectoryReference(_configuration.FolderName);
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
            var servicePoint = ServicePointManager.FindServicePoint(_client.BaseUri);
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
                    return Result.Success(await _container.CreateIfNotExistsAsync(
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
                    BlobDownloadStrategyConfiguration: _configuration.BlobDownloadStrategyConfiguration));
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
            return _directory.GetBlockBlobReference($"{contentHash}.blob");
        }
    }
}
