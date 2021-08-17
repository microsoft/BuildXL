// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Server;
using static Grpc.Core.Server;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    /// <summary>
    /// Interface that represents a global cache service backed by a <see cref="IContentMetadataStore"/> and <see cref="IClusterManagementStore"/>
    /// </summary>
    public class GlobalCacheService : StartupShutdownComponentBase, IGlobalCacheService
    {
        protected override Tracer Tracer { get; } = new Tracer(nameof(GlobalCacheService));

        public override bool AllowMultipleStartupAndShutdowns => true;

        private readonly IContentMetadataStore _store;
        private Context _startupContext;
        protected readonly ClusterManagementStore ClusterManagementStore;

        public GlobalCacheService(IContentMetadataStore store, ClusterManagementStore clusterManagementStore)
        {
            _store = store;
            ClusterManagementStore = clusterManagementStore;
            LinkLifetime(store);
            LinkLifetime(clusterManagementStore);
        }

        /// <inheritdoc />
        public override Task<BoolResult> StartupAsync(Context context)
        {
            _startupContext = context;
            return base.StartupAsync(context);
        }

        /// <inheritdoc />
        public virtual Task<HeartbeatMachineResponse> HeartbeatAsync(HeartbeatMachineRequest request, CallContext callContext = default)
        {
            return ExecuteAsync(request, callContext, context =>
            {
                return ClusterManagementStore.HeartbeatAsync(context, request);
            },
            extraEndMessage: r => string.Join(" ", request, $"Result=[{r.GetValueOrDefault()?.ToString() ?? "Error"}]"));
        }

        /// <inheritdoc />
        public virtual Task<GetClusterUpdatesResponse> GetClusterUpdatesAsync(GetClusterUpdatesRequest request, CallContext callContext = default)
        {
            return ExecuteAsync(request, callContext, context =>
            {
                return ClusterManagementStore.GetClusterUpdatesAsync(context, request);
            },
            extraEndMessage: r => string.Join(" ", request, $"Result=[{r.GetValueOrDefault()?.ToString() ?? "Error"}]"));
        }

        /// <inheritdoc />
        public Task<GetContentLocationsResponse> GetContentLocationsAsync(GetContentLocationsRequest request, CallContext callContext = default)
        {
            return ExecuteAsync(request, callContext, context =>
            {
                return _store.GetBulkAsync(context, request.Hashes)
                    .SelectAsync(entries => new GetContentLocationsResponse()
                    {
                        Entries = entries
                    });
            },
            extraEndMessage: r => {
                if (!r.Succeeded)
                {
                    var csv = string.Join(",", request.Hashes);
                    return $"Hashes=[{csv}]";
                }
                else
                {
                    var entries = r.Value.Entries;
                    var csv = string.Join(",", request.Hashes.Zip(entries, (hash, entry) => $"{hash}:{entry.Locations.Count}"));
                    return $"Hashes=[{csv}]";
                }
            });
        }

        /// <inheritdoc />
        public Task<RegisterContentLocationsResponse> RegisterContentLocationsAsync(RegisterContentLocationsRequest request, CallContext callContext = default)
        {
            return ExecuteAsync(request, callContext, context =>
            {
                return _store.RegisterLocationAsync(context, request.MachineId, request.Hashes, touch: false)
                    .SelectAsync(_ => new RegisterContentLocationsResponse()
                    {
                        PersistRequest = true
                    });
            },
            extraEndMessage: r => {
                var csv = string.Join(",", request.Hashes.Select(h => h.Hash));
                return $"MachineId=[{request.MachineId}] Hashes=[{csv}]";
            });
        }

        public Task<PutBlobResponse> PutBlobAsync(PutBlobRequest request, CallContext callContext = default)
        {
            return ExecuteAsync(request, callContext, context =>
            {
                // NOTE: We don't persist put blob requests currently because they are not critical.
                // This can be enabled by setting PersistRequest=true
                return _store.PutBlobAsync(context, request.ContentHash, request.Blob)
                    .SelectAsync(_ => new PutBlobResponse());
            },
            extraEndMessage: _ => $"Hash=[{request.ContentHash}] Size=[{request.Blob.Length}]");
        }

        public Task<GetBlobResponse> GetBlobAsync(GetBlobRequest request, CallContext callContext = default)
        {
            return ExecuteAsync(request, callContext, context =>
            {
                return _store.GetBlobAsync(context, request.ContentHash)
                    .SelectAsync(r => new GetBlobResponse()
                    {
                        Blob = r.Blob,
                    });
            },
            extraEndMessage: r => {
                if (!r.Succeeded)
                {
                    return $"Hash=[{request.ContentHash}]";
                }

                return $"Hash=[{request.ContentHash}] Size=[{r.Value.Blob?.Length ?? -1}]";
            });
        }

        public Task<CompareExchangeResponse> CompareExchangeAsync(CompareExchangeRequest request, CallContext callContext = default)
        {
            return ExecuteAsync(request, callContext, context =>
            {
                return _store.CompareExchangeAsync(context, request.StrongFingerprint, request.Replacement, request.ExpectedReplacementToken)
                    .SelectAsync(r => new CompareExchangeResponse()
                    {
                        Exchanged = r.Value,

                        // Only persist if we succeeded in exchanging the value
                        PersistRequest = r.Value
                    });
            });
        }

        public Task<GetLevelSelectorsResponse> GetLevelSelectorsAsync(GetLevelSelectorsRequest request, CallContext callContext = default)
        {
            return ExecuteAsync(request, callContext, context =>
            {
                return _store.GetLevelSelectorsAsync(context, request.WeakFingerprint, request.Level)
                    .SelectAsync(r => new GetLevelSelectorsResponse()
                    {
                        HasMore = r.Value.HasMore,
                        Selectors = r.Value.Selectors
                    });
            });
        }

        public Task<GetContentHashListResponse> GetContentHashListAsync(GetContentHashListRequest request, CallContext callContext = default)
        {
            return ExecuteAsync(request, callContext, context =>
            {
                return _store.GetContentHashListAsync(context, request.StrongFingerprint)
                    .SelectAsync(r => new GetContentHashListResponse()
                    {
                        MetadataEntry = r.Value
                    });
            });
        }

        protected virtual Task<Result<TResponse>> ExecuteCoreAsync<TRequest, TResponse>(
            OperationContext context,
            TRequest request,
            Func<OperationContext, Task<Result<TResponse>>> executeAsync)
            where TRequest : ServiceRequestBase
            where TResponse : ServiceResponseBase, new()
        {
            return executeAsync(context);
        }

        protected virtual Task<TResponse> ExecuteAsync<TRequest, TResponse>(
            TRequest request,
            CallContext callContext,
            Func<OperationContext, Task<Result<TResponse>>> executeAsync,
            string extraStartMessage = null,
            Func<Result<TResponse>, string> extraEndMessage = null,
            [CallerMemberName] string caller = null)
            where TRequest : ServiceRequestBase
            where TResponse : ServiceResponseBase, new()
        {
            var tracingContext = new Context(request.ContextId, _startupContext.Logger);
            return WithOperationContext(
                tracingContext,
                callContext.CancellationToken,
                async context =>
                {
                    var result = await context.PerformOperationAsync(
                        Tracer,
                        () => ExecuteCoreAsync(context, request, executeAsync),
                        caller: caller,
                        traceOperationStarted: false,
                        extraStartMessage: extraStartMessage,
                        extraEndMessage: r => string.Join(" ", extraEndMessage?.Invoke(r), request.BlockId?.ToString(), $"Retry=[{r.GetValueOrDefault()?.ShouldRetry}]"));

                        if (result.Succeeded)
                        {
                            return result.Value;
                        }
                        else
                        {
                            var response = new TResponse()
                            {
                                ErrorMessage = result.ErrorMessage,
                                Diagnostics = result.Diagnostics
                            };

                            return response;
                        }
                });
        }
    }
}
