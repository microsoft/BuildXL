// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Cache.MemoizationStore.Service
{
    /// <summary>
    /// Cache whose sessions trigger L3 publishing in the service.
    /// </summary>
    public class ServiceClientPublishingCache : ServiceClientCache, IPublishingCache
    {
        private readonly PublishingCacheConfiguration _publishingConfig;
        private readonly string _pat;

        /// <nodoc />
        public ServiceClientPublishingCache(
            ILogger logger,
            IAbsFileSystem fileSystem,
            ServiceClientContentStoreConfiguration configuration,
            PublishingCacheConfiguration publishingConfig,
            string pat)
            : base(logger, fileSystem, configuration)
        {
            Contract.Assert(publishingConfig != null, "Publishing configuration should not be null");

            _publishingConfig = publishingConfig;
            _pat = pat;
        }

        /// <inheritdoc />
        public override CreateSessionResult<ICacheSession> CreateSession(
            Context context,
            string name,
            ImplicitPin implicitPin)
        {
            return CreatePublishingSession(context, name, implicitPin, _publishingConfig, _pat);
        }

        /// <inheritdoc />
        public CreateSessionResult<ICacheSession> CreatePublishingSession(
            Context context,
            string name,
            ImplicitPin implicitPin,
            PublishingCacheConfiguration publishingConfig,
            string pat)
        {
            var operationContext = OperationContext(context);
            return operationContext.PerformOperation(
                Tracer,
                () =>
                {
                    Tracer.Info(context, $"Creating cache service client session with name=[{name}] and publishing enabled. {nameof(ICacheSession.AddOrGetContentHashListAsync)} calls to the session will trigger publishing in the service.");

                    return new CreateSessionResult<ICacheSession>(new ServiceClientPublishingCacheSession(
                        new OperationContext(context),
                        name,
                        implicitPin,
                        Logger,
                        FileSystem,
                        SessionTracer,
                        Configuration,
                        publishingConfig,
                        pat));
                });
        }
    }
}
