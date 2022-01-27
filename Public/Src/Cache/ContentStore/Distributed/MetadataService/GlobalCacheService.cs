// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using ProtoBuf.Grpc;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    /// <summary>
    /// Interface that represents a global cache service backed by a <see cref="IContentMetadataStore"/>
    /// </summary>
    public class GlobalCacheService : StartupShutdownComponentBase, IGlobalCacheService
    {
        protected override Tracer Tracer { get; } = new Tracer(nameof(GlobalCacheService));

        public override bool AllowMultipleStartupAndShutdowns => true;

        private readonly IContentMetadataStore _store;

        public GlobalCacheService(IContentMetadataStore store)
        {
            _store = store;
            LinkLifetime(store);
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

        /// <summary>
        /// An optimized low allocation version of <see cref="RegisterContentLocationsAsync"/>.
        /// </summary>
        // TODO: instead of manually doing all of that we should make our helpers more efficient and as allocation free as possible. Work Item: 1883860
        protected async ValueTask<ValueUnit> RegisterContentLocationsFastAsync(RegisterContentLocationsRequest request)
        {
            // This method is called extremely frequently in some cases so to avoid any unnecessary performance penalties
            // it does not use any helper methods.

            // Intentionally not tracking the shutdown here to avoid extra allocations and extra work that is rarely needed.
            // Plus this method should not be very long and we can just allow it to fail if the shutdown will cause some errors.
            // (consider adding a flag if we think it will be needed in the future).

            var tracingContext = new Context(request.ContextId, StartupLogger);
            var context = new OperationContext(tracingContext);

            try
            {
                await _store.RegisterLocationAsync(context, request.MachineId, request.Hashes, touch: false).ThrowIfFailure();
            }
            catch (Exception e)
            {
                // Checking the errors for the synchronous case.
                Tracer.Info(context, $"Content location registration failed {e}");
            }

            return ValueUnit.Void;
        }

        /// <inheritdoc />
        public Task<RegisterContentLocationsResponse> RegisterContentLocationsAsync(RegisterContentLocationsRequest request, CallContext callContext = default)
        {
            return ExecuteAsync(request, callContext, context =>
            {
                return _store.RegisterLocationAsync(context, request.MachineId, request.Hashes, touch: false).AsTask()
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
            var contextId = request.ContextId;
            contextId ??= MetadataServiceSerializer.TryGetContextId(callContext.RequestHeaders);
            var tracingContext = contextId != null
                ? new Context(contextId, StartupLogger)
                : new Context(StartupLogger);

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
                        // Removing this (i.e., enabling logging on all operations) overwhelms NLog, causing extreme
                        // memory usage growth until you run out of it.
                        traceErrorsOnly: true,
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
