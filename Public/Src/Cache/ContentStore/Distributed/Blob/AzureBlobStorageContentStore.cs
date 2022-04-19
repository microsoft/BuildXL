// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stores;
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

        public string ContainerName { get; set; } = "blobcontentstore";

        public string FolderName { get; set; } = "blobcontentstore";

        public TimeSpan StorageInteractionTimeout { get; set; } = TimeSpan.FromMinutes(30);
    }

    public class AzureBlobStorageContentStore : StartupShutdownComponentBase, IContentStore
    {
        protected override Tracer Tracer { get; } = new Tracer(nameof(AzureBlobStorageContentStore));

        private readonly AzureBlobStorageContentStoreConfiguration _configuration;

        internal IContentStore ContentStore { get; }

        private readonly CloudBlobClient _client;
        private readonly CloudBlobContainer _container;
        private readonly CloudBlobDirectory _directory;

        public AzureBlobStorageContentStore(AzureBlobStorageContentStoreConfiguration configuration, Func<IContentStore> contentStoreFactory, bool ownsContentStore = true)
        {
            _configuration = configuration;
            ContentStore = contentStoreFactory();

            _client = _configuration.Credentials!.CreateCloudBlobClient();
            _container = _client.GetContainerReference(_configuration.ContainerName);
            _directory = _container.GetDirectoryReference(_configuration.FolderName);

            if (ownsContentStore)
            {
                LinkLifetime(ContentStore);
            }
        }

        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            await base.StartupCoreAsync(context).ThrowIfFailure();
            await EnsureContainerExists(context).ThrowIfFailure();
            return BoolResult.Success;
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

        public static AzureBlobStorageContentStore WithFileSystemContentStore(
            AzureBlobStorageContentStoreConfiguration configuration,
            AbsolutePath contentStoreRootPath,
            ContentStoreConfiguration? contentStoreConfiguration = null,
            ContentStoreSettings? contentStoreSettings = null)
        {
            contentStoreConfiguration ??= ContentStoreConfiguration.CreateWithMaxSizeQuotaMB(1024);
            contentStoreSettings ??= ContentStoreSettings.DefaultSettings;

            var contentStoreFactory = () => new FileSystemContentStore(
                fileSystem: new PassThroughFileSystem(),
                clock: SystemClock.Instance,
                rootPath: contentStoreRootPath,
                configurationModel: new ConfigurationModel(
                    inProcessConfiguration: contentStoreConfiguration,
                    selection: ConfigurationSelection.RequireAndUseInProcessConfiguration),
                distributedStore: null,
                settings: contentStoreSettings,
                coldStorage: null);

            return new AzureBlobStorageContentStore(configuration, contentStoreFactory);
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
            return AzureBlobStorageContentSession.Create(
                context,
                new AzureBlobStorageContentSession.Configuration(
                    Name: name,
                    ImplicitPin: implicitPin,
                    Parent: this,
                    StorageInteractionTimeout: _configuration.StorageInteractionTimeout));
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
