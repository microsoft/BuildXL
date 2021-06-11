// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    /// <summary>
    /// Interface that represents a global content metadata store which routes requests to a remote machine
    /// </summary>
    public class ClientContentMetadataStore : StartupShutdownSlimBase, IContentMetadataStore
    {
        public bool AreBlobsSupported => _configuration.AreBlobsSupported;

        protected override Tracer Tracer { get; } = new Tracer(nameof(ClientContentMetadataStore));

        private readonly IClientFactory<IContentMetadataService> _metadataServiceClientFactory;

        private readonly IGlobalLocationStore _globalStore;

        private readonly IRetryPolicy _retryPolicy;

        private readonly ClientContentMetadataStoreConfiguration _configuration;

        public ClientContentMetadataStore(IGlobalLocationStore globalStore, IClientFactory<IContentMetadataService> metadataServiceClientFactory, ClientContentMetadataStoreConfiguration configuration)
        {
            _globalStore = globalStore;
            _metadataServiceClientFactory = metadataServiceClientFactory;
            _configuration = configuration;

            _retryPolicy = RetryPolicyFactory.GetExponentialPolicy(
                _ => true,
                // We use an absurdly high retry count because the actual operation timeout is controlled through
                // PerformOperationAsync in ExecuteAsync.
                1_000_000,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(30));
        }

        protected override Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            return _metadataServiceClientFactory.StartupAsync(context);
        }

        protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            return _metadataServiceClientFactory.ShutdownAsync(context);
        }

        private Task<TResult> ExecuteAsync<TResult>(
            OperationContext context,
            Func<IContentMetadataService, Task<TResult>> executeAsync,
            string extraStartMessage = null,
            Func<TResult, string> extraEndMessage = null,
            [CallerMemberName] string caller = null)
            where TResult : ResultBase
        {
            var attempt = -1;
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                context =>
                {
                    return _retryPolicy.ExecuteAsync(async () =>
                    {
                        attempt++;

                        var result = await context.PerformOperationAsync(Tracer, async () =>
                        {
                            var client = await _metadataServiceClientFactory.CreateClientAsync(context);
                            return await executeAsync(client);
                        },
                        extraStartMessage: extraStartMessage,
                        extraEndMessage: r => $"Attempt=[{attempt}] {extraEndMessage(r)}",
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
                extraStartMessage: extraStartMessage,
                extraEndMessage: r => $"Attempts=[{attempt + 1}] {extraEndMessage(r)}",
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
                }, context.Token);

                if (response.Succeeded)
                {
                    return Result.Success(response.Entries);
                }
                else
                {
                    return new Result<IReadOnlyList<ContentLocationEntry>>(response.ErrorMessage, response.Diagnostics);
                }
            },
            extraEndMessage: r => {
                if (!r.Succeeded)
                {
                    var csv = string.Join(",", contentHashes);
                    return $"Hashes=[{csv}]";
                }
                else
                {
                    var entries = r.Value;
                    var csv = string.Join(",", contentHashes.Zip(entries, (hash, entry) => $"{hash}:{entry.Locations.Count}"));
                    return $"Hashes=[{csv}]";
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
                }, context.Token);

                if (response.Succeeded)
                {
                    return BoolResult.Success;
                }
                else
                {
                    return new BoolResult(response.ErrorMessage, response.Diagnostics);
                }
            },
            extraEndMessage: _ => {
                var csv = string.Join(",", contentHashes.Select(s => s.Hash));
                return $"MachineId=[{machineId}] Touch=[{touch}] Hashes=[{csv}]";
            });
        }

        public Task<PutBlobResult> PutBlobAsync(OperationContext context, ShortHash hash, byte[] blob)
        {
            return ExecuteAsync(context, async service =>
            {
                var response = await service.PutBlobAsync(new PutBlobRequest()
                {
                    ContextId = context.TracingContext.TraceId,
                    ContentHash = hash,
                    Blob = blob,
                }, context.Token);

                return response.ToPutBlobResult(hash, blob.Length);
            },
            extraEndMessage: _ => $"Hash=[{hash}] Size=[{blob.Length}]");
        }

        public Task<GetBlobResult> GetBlobAsync(OperationContext context, ShortHash hash)
        {
            return ExecuteAsync(context, async service =>
            {
                var response = await service.GetBlobAsync(new GetBlobRequest()
                {
                    ContextId = context.TracingContext.TraceId,
                    ContentHash = hash,
                }, context.Token);

                return response.ToGetBlobResult(hash);
            },
            extraEndMessage: r => $"Hash=[{hash}] Size=[{r.Blob?.Length ?? -1}]");
        }
    }
}
