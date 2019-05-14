// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

extern alias Async;

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Cache.MemoizationStore.Service
{
    /// <summary>
    /// An ICache fronting a cache in a separate process.
    /// </summary>
    public class ServiceClientCache : ServiceClientContentStore, ICache
    {
        /// <inheritdoc />
        public Guid Id { get; } = Guid.NewGuid();

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(ServiceClientCache));

        /// <nodoc />
        public ServiceClientCache(
            ILogger logger,
            IAbsFileSystem fileSystem,
            ServiceClientContentStoreConfiguration configuration)
            : base(logger, fileSystem, configuration)
        {
        }

        /// <nodoc />
        public new CreateSessionResult<IReadOnlyCacheSession> CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin)
        {
            var operationContext = OperationContext(context);
            return operationContext.PerformOperation(
                Tracer,
                () =>
                {
                    var session = new ServiceClientCacheSession(name, implicitPin, Logger, FileSystem, SessionTracer, Configuration);
                    return new CreateSessionResult<IReadOnlyCacheSession>(session);
                });
        }

        /// <nodoc />
        public new CreateSessionResult<ICacheSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
        {
            var operationContext = OperationContext(context);
            return operationContext.PerformOperation(
                Tracer,
                () =>
                {
                    var session = new ServiceClientCacheSession(name, implicitPin, Logger, FileSystem, SessionTracer, Configuration);
                    return new CreateSessionResult<ICacheSession>(session);
                });
        }

        /// <nodoc />
        public Async::System.Collections.Generic.IAsyncEnumerable<StructResult<StrongFingerprint>> EnumerateStrongFingerprints(Context context)
        {
            return AsyncEnumerable.Empty<StructResult<StrongFingerprint>>();
        }
    }
}
