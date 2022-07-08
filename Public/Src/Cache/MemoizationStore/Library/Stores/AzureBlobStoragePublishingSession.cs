// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO.MemoryMappedFiles;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;

namespace BuildXL.Cache.ContentStore.Distributed.Blobs
{
    /// <nodoc />
    public class AzureBlobStoragePublishingSessionConfiguration
    {
        /// <nodoc />
        public string SessionName { get; set; }

        /// <nodoc />
        public string PersonalAccessToken { get; set; }

        /// <nodoc />
        public AzureBlobStoragePublishingStore Parent { get; set; }
    }

    /// <nodoc />
    public class AzureBlobStoragePublishingSession : PublishingSessionBase<AzureBlobStoragePublishingSessionConfiguration>
    {
        /// <nodoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(AzureBlobStoragePublishingSession));

        /// <nodoc />
        public AzureBlobStoragePublishingSession(
            AzureBlobStoragePublishingSessionConfiguration configuration,
            Func<IContentSession> localContentSessionFactory,
            SemaphoreSlim fingerprintPublishingGate,
            SemaphoreSlim contentPublishingGate)
            : base(configuration,
                  localContentSessionFactory,
                  fingerprintPublishingGate,
                  contentPublishingGate)
        {
        }

        /// <nodoc />
        protected override async Task<ICachePublisher> CreateCachePublisherCoreAsync(
            OperationContext context,
            AzureBlobStoragePublishingSessionConfiguration configuration)
        {
            var credentials = new AzureBlobStorageCredentials(connectionString: configuration.PersonalAccessToken);

            var blobMetadataStore = new AzureBlobStorageMetadataStore(new BlobMetadataStoreConfiguration()
            {
                Credentials = credentials,
            });

            var database = new MetadataStoreMemoizationDatabase(blobMetadataStore);
            var memoizationStore = new DatabaseMemoizationStore(database);

            // TODO: we must propagate settings here
            var contentStore = new AzureBlobStorageContentStore(new AzureBlobStorageContentStoreConfiguration()
                {
                    Credentials = credentials,
                });
            var cache = new OneLevelCache(
                contentStoreFunc: () => contentStore,
                memoizationStoreFunc: () => memoizationStore,
                id: Guid.NewGuid(),
                passContentToMemoization: false);

            await cache.StartupAsync(context).ThrowIfFailureAsync();
            var session = cache.CreateSession(context, configuration.SessionName, ImplicitPin.None).ThrowIfFailure();

            return new CacheSessionPublisherWrapper(cache, session.Session);
        }
    }
}
