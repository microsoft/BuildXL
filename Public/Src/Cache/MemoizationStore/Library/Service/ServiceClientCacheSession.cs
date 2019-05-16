// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

extern alias Async;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Cache.MemoizationStore.Service
{
    /// <todoc />
    public class ServiceClientCacheSession : ServiceClientContentSession, ICacheSession, IReadOnlyMemoizationSessionWithLevelSelectors
    {
        private readonly GrpcCacheClient _rpcCacheClient;

        /// <nodoc />
        public ServiceClientCacheSession(
            string name,
            ImplicitPin implicitPin,
            ILogger logger,
            IAbsFileSystem fileSystem,
            ServiceClientContentSessionTracer sessionTracer,
            ServiceClientContentStoreConfiguration configuration)
            : base(name, implicitPin, logger, fileSystem, sessionTracer, configuration, () => GetRpcClient(fileSystem, sessionTracer, configuration))
        {
            // RpcClient is created by the base class constructor, but we know that this is the result of GetPrcClient call
            // that actually returns GrpcCacheClient instance.
            _rpcCacheClient = (GrpcCacheClient)RpcClient;
        }

        private static IRpcClient GetRpcClient(
            IAbsFileSystem fileSystem,
            ServiceClientContentSessionTracer sessionTracer,
            ServiceClientContentStoreConfiguration configuration)
        {
            var rpcConfiguration = configuration.RpcConfiguration;
            return new GrpcCacheClient(sessionTracer, fileSystem, rpcConfiguration.GrpcPort, configuration.Scenario, rpcConfiguration.HeartbeatInterval);
        }

        private Task<TResult> PerformOperationAsync<TResult>(Context context, CancellationToken cts, Func<OperationContext, GrpcCacheClient, Task<TResult>> func, [CallerMemberName]string caller = null) where TResult : ResultBase
        {
            return WithOperationContext(context, cts,
                operationContext =>
                {
                    return operationContext.PerformOperationAsync(
                        Tracer,
                        () => PerformRetries(operationContext, () => func(operationContext, _rpcCacheClient)),
                        caller: caller);
                });
        }

        /// <inheritdoc />
        public Task<AddOrGetContentHashListResult> AddOrGetContentHashListAsync(Context context, StrongFingerprint strongFingerprint, ContentHashListWithDeterminism contentHashListWithDeterminism, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return PerformOperationAsync(
                context,
                cts,
                (ctx, client) => client.AddOrGetContentHashListAsync(ctx, strongFingerprint, contentHashListWithDeterminism));
        }

        /// <inheritdoc />
        public Task<GetContentHashListResult> GetContentHashListAsync(Context context, StrongFingerprint strongFingerprint, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return PerformOperationAsync(
                context,
                cts,
                (ctx, client) => client.GetContentHashListAsync(ctx, strongFingerprint));
        }

        /// <inheritdoc />
        public Async::System.Collections.Generic.IAsyncEnumerable<GetSelectorResult> GetSelectors(Context context, Fingerprint weakFingerprint, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return this.GetSelectorsAsAsyncEnumerable(context, weakFingerprint, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<Result<LevelSelectors>> GetLevelSelectorsAsync(Context context, Fingerprint weakFingerprint, CancellationToken cts, int level)
        {
            return PerformOperationAsync(
                context,
                cts,
                (ctx, client) => client.GetLevelSelectorsAsync(ctx, weakFingerprint, level));
        }

        /// <inheritdoc />
        public Task<BoolResult> IncorporateStrongFingerprintsAsync(Context context, IEnumerable<Task<StrongFingerprint>> strongFingerprints, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return PerformOperationAsync(
                context,
                cts,
                (ctx, client) => client.IncorporateStrongFingerprintsAsync(ctx, strongFingerprints));
        }
    }
}
