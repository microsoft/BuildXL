// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities.Authentication;

#nullable enable

namespace BuildXL.Cache.MemoizationStore.Vsts
{
    /// <nodoc />
    public class BuildCachePublishingSessionConfiguration
    {
        /// <nodoc />
        public BuildCacheServiceConfiguration BuildCacheConfiguration { get; init; } = new BuildCacheServiceConfiguration();

        /// <nodoc />
        public string SessionName { get; init; } = string.Empty;

        /// <nodoc />
        public string PersonalAccessToken { get; init; } = string.Empty;
    }

    /// <summary>
    /// Publishes metadata to the BuildCache service.
    /// </summary>
    public class BuildCachePublishingSession : PublishingSessionBase<BuildCachePublishingSessionConfiguration>
    {
        /// <nodoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(BuildCachePublishingSession));

        /// <nodoc />
        public BuildCachePublishingSession(
            BuildCachePublishingSessionConfiguration configuration,
            IContentSession sourceContentSession,
            SemaphoreSlim fingerprintPublishingGate,
            SemaphoreSlim contentPublishingGate)
            : base(configuration, () => sourceContentSession, fingerprintPublishingGate, contentPublishingGate)
        {
        }

        /// <nodoc />
        protected override async Task<ICachePublisher> CreateCachePublisherCoreAsync(OperationContext context, BuildCachePublishingSessionConfiguration configuration)
        {
            var credentialsFactory = new VssCredentialsFactory(
                configuration.PersonalAccessToken,
                helper: null,
                logger: m => Tracer.Info(context, m));

            var cache = BuildCacheCacheFactory.Create(
                PassThroughFileSystem.Default,
                context.TracingContext.Logger,
                credentialsFactory,
                configuration.BuildCacheConfiguration,
                writeThroughContentStoreFunc: null);

            await cache.StartupAsync(context).ThrowIfFailure();

            var sessionResult = cache.CreateSession(context, configuration.SessionName, ImplicitPin.None).ThrowIfFailure();
            var session = sessionResult.Session;

            Contract.Check(session is BuildCacheSession)?.Assert($"Session should be an instance of {nameof(BuildCacheSession)}. Actual type: {session.GetType()}");

            return new CacheSessionPublisherWrapper(cache, session);
        }
    }
}
