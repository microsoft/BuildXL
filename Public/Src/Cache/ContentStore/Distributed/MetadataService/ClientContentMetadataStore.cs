// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities;
using Grpc.Core;
using Microsoft.WindowsAzure.Storage.RetryPolicies;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Interface that represents a global content metadata store which routes requests to a remote machine
    /// </summary>
    public class ClientContentMetadataStore : StartupShutdownSlimBase, IContentMetadataStore
    {
        public bool AreBlobsSupported => false;

        private MachineId MachineId => _globalStore.ClusterState.PrimaryMachineId;

        protected override Tracer Tracer { get; } = new Tracer(nameof(ClientContentMetadataStore));

        private readonly IClientFactory<IContentMetadataService> _metadataServiceClientFactory;

        private readonly IGlobalLocationStore _globalStore;

        private readonly Utils.IRetryPolicy _retryPolicy;

        private readonly ClientContentMetadataStoreConfiguration _configuration;

        public ClientContentMetadataStore(IGlobalLocationStore globalStore, IClientFactory<IContentMetadataService> metadataServiceClientFactory, ClientContentMetadataStoreConfiguration configuration)
        {
            _globalStore = globalStore;
            _metadataServiceClientFactory = metadataServiceClientFactory;
            _configuration = configuration;

            _retryPolicy = RetryPolicyFactory.GetExponentialPolicy(exception =>
            {
                return true;
            });
        }

        protected override Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            return _metadataServiceClientFactory.StartupAsync(context);
        }

        protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            return _metadataServiceClientFactory.ShutdownAsync(context);
        }

        private Task<TResult> ExecuteAsync<TResult>(OperationContext context, Func<IContentMetadataService, Task<TResult>> executeAsync, [CallerMemberName] string caller = null)
            where TResult : ResultBase
        {
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                context =>
                {
                    var attempt = -1;
                    return _retryPolicy.ExecuteAsync(async () =>
                    {
                        attempt++;

                        var result = await context.PerformOperationAsync(Tracer, async () =>
                        {
                            var client = await _metadataServiceClientFactory.CreateClientAsync(context);
                            return await executeAsync(client);
                        },
                        extraEndMessage: _ => $"Attempt=[{attempt}]",
                        caller: caller,
                        traceErrorsOnly: true);

                        // Because we capture exceptions inside the PerformOperation, we need to make sure that they
                        // get propagated for the retry policy to kick in.
                        if (!result.Succeeded && result.HasException)
                        {
                            throw result.Exception;
                        }

                        return result;
                    }, context.Token);
                },
                caller: caller,
                traceErrorsOnly: true,
                timeout: _configuration.OperationTimeout);
        }

        public Task<Result<IReadOnlyList<ContentLocationEntry>>> GetBulkAsync(OperationContext context, IReadOnlyList<ShortHash> contentHashes)
        {
            return ExecuteAsync(context, async service =>
            {
                var response = await service.GetContentLocationsAsync(new GetContentLocationsRequest()
                {
                    ContextId = context.TracingContext.TraceId,
                    Hashes = contentHashes,
                });

                if (response.Succeeded)
                {
                    return Result.Success<IReadOnlyList<ContentLocationEntry>>(response.Entries);
                }
                else
                {
                    return new Result<IReadOnlyList<ContentLocationEntry>>(response.ErrorMessage, response.Diagnostics);
                }                    
            });
        }

        public Task<BoolResult> RegisterLocationAsync(OperationContext context, MachineId machineId, IReadOnlyList<ShortHashWithSize> contentHashes, bool touch)
        {
            return ExecuteAsync(context, async service =>
            {
                var response = await service.RegisterContentLocationsAsync(new RegisterContentLocationsRequest()
                {
                    ContextId = context.TracingContext.TraceId,
                    Hashes = contentHashes,
                    MachineId = machineId,
                });

                if (response.Succeeded)
                {
                    return BoolResult.Success;
                }
                else
                {
                    return new BoolResult(response.ErrorMessage, response.Diagnostics);
                }
            });
        }

        public Task<PutBlobResult> PutBlobAsync(OperationContext context, ShortHash hash, byte[] blob)
        {
            throw new NotImplementedException();
        }

        public Task<GetBlobResult> GetBlobAsync(OperationContext context, ShortHash hash)
        {
            throw new NotImplementedException();
        }
    }
}
