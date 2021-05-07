// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using ProtoBuf.Grpc.Server;
using static Grpc.Core.Server;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Interface that represents a content metadata service backed by a <see cref="IContentMetadataStore"/>
    /// </summary>
    public class ContentMetadataService : StartupShutdownSlimBase, IContentMetadataService, IGrpcServiceEndpoint
    {
        protected override Tracer Tracer { get; } = new Tracer(nameof(ContentMetadataService));

        private readonly IContentMetadataStore _store;
        private Context _startupContext;

        public ContentMetadataService(IContentMetadataStore store)
        {
            _store = store;
        }

        protected override Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            _startupContext = context;
            return _store.StartupAsync(context);
        }

        protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            return _store.ShutdownAsync(context);
        }

        public Task<GetContentLocationsResponse> GetContentLocationsAsync(GetContentLocationsRequest request)
        {
            return ExecuteAsync(request, async context =>
            {
                Result<IReadOnlyList<ContentLocationEntry>> result = await _store.GetBulkAsync(context, request.Hashes);
                return result.Select(entries => new GetContentLocationsResponse()
                {
                    Entries = entries
                });
            });
        }

        public Task<RegisterContentLocationsResponse> RegisterContentLocationsAsync(RegisterContentLocationsRequest request)
        {
            return ExecuteAsync(request, async context =>
            {
                var result = await _store.RegisterLocationAsync(context, request.MachineId, request.Hashes, touch: false);
                if (result.Succeeded)
                {
                    return Result.Success(new RegisterContentLocationsResponse());
                }
                else
                {
                    return Result.FromError<RegisterContentLocationsResponse>(result);
                }
            });
        }

        public async Task<TResponse> ExecuteAsync<TRequest, TResponse>(
            TRequest request,
            Func<OperationContext, Task<Result<TResponse>>> executeAsync,
            [CallerMemberName] string caller = null)
            where TRequest : ServiceRequestBase
            where TResponse : ServiceResponseBase, new()
        {
            var context = OperationContext(_startupContext.CreateNested(request.ContextId, Tracer.Name, caller));
            var result = await executeAsync(context);
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
