// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Cache.ContentStore.Distributed.Blobs
{
    /// <nodoc />
    public class AzureBlobStoragePublishingCacheConfiguration : PublishingCacheConfiguration
    {

    }

    /// <nodoc />
    public class AzureBlobStoragePublishingStore : StartupShutdownSlimBase, IPublishingStore
    {
        /// <nodoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(AzureBlobStoragePublishingStore));

        private readonly SemaphoreSlim _fingerprintPublishingGate = new SemaphoreSlim(1);
        private readonly SemaphoreSlim _contentPublishingGate = new SemaphoreSlim(32);

        private readonly IContentStore _localContentStore;

        /// <nodoc />
        public AzureBlobStoragePublishingStore(IContentStore localContentStore)
        {
            _localContentStore = localContentStore;
        }

        /// <nodoc />
        public Result<IPublishingSession> CreateSession(Context context, string name, PublishingCacheConfiguration config, string pat)
        {
            if (!IsValidConfigurationType(config))
            {
                return new Result<IPublishingSession>($"Configuration is not a {nameof(AzureBlobStoragePublishingCacheConfiguration)}. Actual type: {config.GetType().FullName}");
            }

            using var cancellableContext = TrackShutdown(context);
            var operationContext = cancellableContext.Context;

            return operationContext.PerformOperation(Tracer, () =>
            {
                return Result.Success<IPublishingSession>(new AzureBlobStoragePublishingSession(new AzureBlobStoragePublishingSessionConfiguration()
                {
                    SessionName = name,
                    PersonalAccessToken = pat,
                    Parent = this,
                },
                // We need to pass in a context to create the session, so we do it like this instead of inside the
                // AzureBlobPublishingSession
                localContentSessionFactory: () =>
                    _localContentStore
                        .CreateSession(context, $"azure-blob-publishing-{name}", ImplicitPin.None)
                        .ThrowIfFailure()
                        .Session,
                fingerprintPublishingGate: _fingerprintPublishingGate,
                contentPublishingGate: _contentPublishingGate));
            });
        }

        /// <nodoc />
        public bool IsValidConfigurationType(PublishingCacheConfiguration config)
        {
            return config is AzureBlobStoragePublishingCacheConfiguration;
        }
    }
}
