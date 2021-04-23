// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;

namespace BuildXL.Cache.MemoizationStore.Service
{
    /// <nodoc />
    public class ServiceClientPublishingCacheSession : ServiceClientCacheSession
    {
        /// <nodoc />
        public ServiceClientPublishingCacheSession(
            string name,
            ImplicitPin implicitPin,
            ILogger logger,
            IAbsFileSystem fileSystem,
            ServiceClientContentSessionTracer sessionTracer,
            ServiceClientContentStoreConfiguration configuration,
            PublishingCacheConfiguration publishingConfig,
            string pat)
            : base(
                  name,
                  implicitPin,
                  logger,
                  fileSystem,
                  sessionTracer,
                  configuration,
                  () => CreateRpcClient(fileSystem, sessionTracer, configuration, publishingConfig, pat))
        {
        }

        private static IRpcClient CreateRpcClient(
            IAbsFileSystem fileSystem,
            ServiceClientContentSessionTracer sessionTracer,
            ServiceClientContentStoreConfiguration configuration,
            PublishingCacheConfiguration publishingConfig,
            string pat)
            => new GrpcPublishingCacheClient(
                sessionTracer,
                fileSystem,
                configuration.RpcConfiguration,
                configuration.Scenario,
                publishingConfig,
                pat);
    }
}
