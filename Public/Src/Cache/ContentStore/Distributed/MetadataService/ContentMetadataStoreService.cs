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
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Server;
using static Grpc.Core.Server;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    /// <summary>
    /// Interface that represents a content metadata service backed by a <see cref="IContentMetadataStore"/>
    /// </summary>
    public class ContentMetadataService : StartupShutdownComponentBase, IContentMetadataService, IGrpcServiceEndpoint
    {
        protected override Tracer Tracer { get; } = new Tracer(nameof(ContentMetadataService));

        private readonly IContentMetadataStore _store;
        private Context _startupContext;

        public ContentMetadataService(IContentMetadataStore store)
        {
            _store = store;
            LinkLifetime(store);
        }

        /// <inheritdoc />
        public override Task<BoolResult> StartupAsync(Context context)
        {
            _startupContext = context;
            return base.StartupAsync(context);
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

                return $"Hash=[{request.ContentHash}] Size=[{r.Value.Blob.Length}]";
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
                        extraEndMessage: r => string.Join(" ", extraEndMessage(r), request.BlockId?.ToString(), $"Retry=[{r.GetValueOrDefault()?.ShouldRetry}]"));

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

        /// <inheritdoc />
        public void BindServices(ServiceDefinitionCollection services)
        {
            var textWriterAdapter = new TextWriterAdapter(
                _startupContext,
                Interfaces.Logging.Severity.Info,
                component: nameof(ContentMetadataService),
                operation: "Grpc");
            services.AddCodeFirst<IContentMetadataService>(this, MetadataServiceSerializer.BinderConfiguration, textWriterAdapter);
        }
    }
}
