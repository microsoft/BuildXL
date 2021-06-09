// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
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
            return ExecuteAsync(request, callContext, async context =>
            {
                var result = await _store.GetBulkAsync(context, request.Hashes);
                return result.Select(entries => new GetContentLocationsResponse()
                {
                    Entries = entries
                });
            });
        }

        /// <inheritdoc />
        public Task<RegisterContentLocationsResponse> RegisterContentLocationsAsync(RegisterContentLocationsRequest request, CallContext callContext = default)
        {
            return ExecuteAsync(request, callContext, async context =>
            {
                var result = await _store.RegisterLocationAsync(context, request.MachineId, request.Hashes, touch: false);
                if (result.Succeeded)
                {
                    return Result.Success(new RegisterContentLocationsResponse()
                    {
                        PersistRequest = true
                    });
                }
                else
                {
                    return Result.FromError<RegisterContentLocationsResponse>(result);
                }
            });
        }

        protected virtual async Task<TResponse> ExecuteCoreAsync<TRequest, TResponse>(
            OperationContext context,
            TRequest request,
            Func<OperationContext, Task<Result<TResponse>>> executeAsync,
            [CallerMemberName] string caller = null)
            where TRequest : ServiceRequestBase
            where TResponse : ServiceResponseBase, new()
        {
            var result = await context.PerformOperationAsync(
                Tracer,
                () => executeAsync(context),
                caller: caller,
                traceOperationStarted: false);

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
        }

        protected Task<TResponse> ExecuteAsync<TRequest, TResponse>(
            TRequest request,
            CallContext callContext,
            Func<OperationContext, Task<Result<TResponse>>> executeAsync,
            [CallerMemberName] string caller = null)
            where TRequest : ServiceRequestBase
            where TResponse : ServiceResponseBase, new()
        {
            var tracingContext = new Context(request.ContextId, _startupContext.Logger);
            return WithOperationContext(
                tracingContext,
                callContext.CancellationToken,
                context => ExecuteCoreAsync(context, request, executeAsync, caller));
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
