// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;

namespace BuildXL.Cache.MemoizationStore.Service
{
    /// <summary>
    /// gRPC client whose cache sessions will trigger L3 publishing behaviors in the server.
    /// </summary>
    public class GrpcPublishingCacheClient : GrpcCacheClient
    {
        private readonly string _serializedPublishingConfig;
        private readonly string _pat;

        /// <nodoc />
        public GrpcPublishingCacheClient(
            ServiceClientContentSessionTracer tracer,
            IAbsFileSystem fileSystem,
            ServiceClientRpcConfiguration configuration,
            string scenario,
            PublishingCacheConfiguration publishingConfig,
            string pat)
            : base(tracer, fileSystem, configuration, scenario, Capabilities.All)
        {
            Contract.Requires(publishingConfig is not null);
            _serializedPublishingConfig = DynamicJson.Serialize(publishingConfig);
            _pat = pat;
        }

        /// <inheritdoc />
        public override Task<BoolResult> CreateSessionAsync(
            OperationContext context,
            string name,
            string cacheName,
            ImplicitPin implicitPin) => CreateSessionAsync(context, name, cacheName, implicitPin, _serializedPublishingConfig, _pat);
    }
}
