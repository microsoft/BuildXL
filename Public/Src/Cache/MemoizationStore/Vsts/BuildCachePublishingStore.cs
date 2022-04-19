// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

#nullable enable

namespace BuildXL.Cache.MemoizationStore.Vsts
{
    /// <summary>
    /// Publishes metadata to the BuildCache service.
    /// </summary>
    public class BuildCachePublishingStore : StartupShutdownSlimBase, IPublishingStore
    {
        /// <nodoc />
        protected readonly IAbsFileSystem FileSystem;

        /// <nodoc />
        protected readonly SemaphoreSlim FingerprintPublishingGate;

        /// <nodoc />
        protected readonly SemaphoreSlim ContentPublishingGate;

        /// <summary>
        /// The publishing store needs somewhere to get content from in case it needs to publish a
        /// content hash list's contents. This should point towards some locally available cache.
        /// </summary>
        protected readonly IContentStore ContentSource;

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(BuildCachePublishingStore));

        /// <nodoc />
        public BuildCachePublishingStore(IContentStore contentSource, IAbsFileSystem fileSystem, int concurrencyLimit)
        {
            ContentSource = contentSource;
            FileSystem = fileSystem;

            FingerprintPublishingGate = new SemaphoreSlim(concurrencyLimit);
            ContentPublishingGate = new SemaphoreSlim(concurrencyLimit);
        }

        /// <inheritdoc />
        public virtual Result<IPublishingSession> CreateSession(Context context, string name, PublishingCacheConfiguration config, string pat)
        {
            if (config is not BuildCacheServiceConfiguration buildCacheConfig)
            {
                return new Result<IPublishingSession>($"Configuration is not a {nameof(BuildCacheServiceConfiguration)}. Actual type: {config.GetType().FullName}");
            }

            var contentSessionResult = ContentSource.CreateSession(context, $"{name}-contentSource", ImplicitPin.None);
            if (!contentSessionResult.Succeeded)
            {
                return new Result<IPublishingSession>(contentSessionResult);
            }

            var configuration = new BuildCachePublishingSessionConfiguration()
            {
                BuildCacheConfiguration = buildCacheConfig,
                SessionName = name,
                PersonalAccessToken = pat,
            };

            return new Result<IPublishingSession>(new BuildCachePublishingSession(configuration, contentSessionResult.Session, FingerprintPublishingGate, ContentPublishingGate));
        }
    }
}
