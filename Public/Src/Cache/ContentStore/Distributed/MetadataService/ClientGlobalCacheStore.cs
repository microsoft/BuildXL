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
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities.Tracing;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    /// <summary>
    /// A global content metadata store client which routes requests to a remote machine.
    /// </summary>
    public class ClientGlobalCacheStore : StartupShutdownComponentBase, IGlobalCacheStore
    {
        public bool AreBlobsSupported => _configuration.AreBlobsSupported;

        /// <inheritdoc />
        public override bool AllowMultipleStartupAndShutdowns => true;

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(ClientGlobalCacheStore));

        private readonly IClientAccessor<IGlobalCacheService> _serviceClientFactory;

        private readonly IRetryPolicy _retryPolicy;
        private readonly IRetryPolicy _noRetryPolicy;

        private readonly ClientContentMetadataStoreConfiguration _configuration;

        public ClientGlobalCacheStore(
            IClientAccessor<IGlobalCacheService> metadataServiceClientFactory,
            ClientContentMetadataStoreConfiguration configuration)
        {
            _serviceClientFactory = metadataServiceClientFactory;
            _configuration = configuration;
            _noRetryPolicy = RetryPolicyFactory.GetLinearPolicy(_ => false, 0, TimeSpan.Zero);
            _retryPolicy = RetryPolicyFactory.GetExponentialPolicy(
                _ => true,
                // We use an absurdly high retry count because the actual operation timeout is controlled through
                // PerformOperationAsync in ExecuteAsync.
                1_000_000,
                TimeSpan.FromMilliseconds(5),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromMilliseconds(5));

            LinkLifetime(_serviceClientFactory);
        }

        private async Task<TResult> ExecuteAsync<TResult>(
            OperationContext originalContext,
            Func<OperationContext, IGlobalCacheService, Task<TResult>> executeAsync,
            Func<TResult, string?> extraEndMessage,
            string? extraStartMessage = null,
            bool shouldRetry = true,
            [CallerMemberName] string caller = null!)
            where TResult : ResultBase
        {
            var attempt = -1;
            using var contextWithShutdown = TrackShutdown(originalContext);
            var context = contextWithShutdown.Context;

            return await context.PerformOperationWithTimeoutAsync(
                Tracer,
                context =>
                {
                    var policy = shouldRetry ? _retryPolicy : _noRetryPolicy;
                    return policy.ExecuteAsync(async () =>
                    {
                        await Task.Yield();

                        attempt++;

                        var stopwatch = StopwatchSlim.Start();
                        TimeSpan clientCreationTime = TimeSpan.Zero;

                        var result = await context.PerformOperationAsync(Tracer, () =>
                            {
                                return _serviceClientFactory.UseAsync(context, service =>
                                {
                                    clientCreationTime = stopwatch.Elapsed;
                                    return executeAsync(context, service);
                                });
                            },
                            extraStartMessage: extraStartMessage,
                            extraEndMessage: r => $"Attempt=[{attempt}] ClientCreationTimeMs=[{clientCreationTime.TotalMilliseconds}] {extraEndMessage(r)}",
                            caller: caller,
                            traceErrorsOnly: true
                        );

                        await Task.Yield();

                        // Because we capture exceptions inside the PerformOperation, we need to make sure that they
                        // get propagated for the retry policy to kick in.
                        if (result.Exception != null)
                        {
                            result.ReThrow();
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

        public Task<Result<GetClusterUpdatesResponse>> GetClusterUpdatesAsync(OperationContext context, GetClusterUpdatesRequest request)
        {
            return ExecuteAsync(context, async (context, service) =>
            {
                var response = await service.GetClusterUpdatesAsync(request with
                {
                    ContextId = context.TracingContext.TraceId,
                }, context.Token);

                return response.ToResult(r => r);
            },
            extraEndMessage: r => $"Request=[{request}] Response=[{r.GetValueOrDefault()}]",
            shouldRetry: false);
        }

        public Task<Result<HeartbeatMachineResponse>> HeartbeatAsync(OperationContext context, HeartbeatMachineRequest request)
        {
            return ExecuteAsync(context, async (context, service) =>
            {
                var response = await service.HeartbeatAsync(request with
                {
                    ContextId = context.TracingContext.TraceId,
                }, context.Token);

                return response.ToResult(r => r);
            },
            extraEndMessage: r => $"Request=[{request}] Response=[{r.GetValueOrDefault()}]",
            shouldRetry: false);
        }

        public Task<Result<IReadOnlyList<ContentLocationEntry>>> GetBulkAsync(OperationContext context, IReadOnlyList<ShortHash> contentHashes)
        {
            return ExecuteAsync(context, async (context, service) =>
            {
                var response = await service.GetContentLocationsAsync(new GetContentLocationsRequest()
                {
                    ContextId = context.TracingContext.TraceId,
                    Hashes = contentHashes,
                }, context.Token);

                return response.ToResult(r => response.Entries);
            },
            extraEndMessage: r =>
            {
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

        public ValueTask<BoolResult> RegisterLocationAsync(OperationContext context, MachineId machineId, IReadOnlyList<ShortHashWithSize> contentHashes, bool touch)
        {
            return new ValueTask<BoolResult>(
                ExecuteAsync(context, async (context, service) =>
                {
                    var response = await service.RegisterContentLocationsAsync(new RegisterContentLocationsRequest()
                    {
                        ContextId = context.TracingContext.TraceId,
                        Hashes = contentHashes,
                        MachineId = machineId,
                    }, context.Token);

                    return response.ToBoolResult();
                },
                extraEndMessage: _ =>
                {
                    var csv = string.Join(",", contentHashes.Select(s => s.Hash));
                    return $"MachineId=[{machineId}] Touch=[{touch}] Hashes=[{csv}]";
                }));
        }

        public Task<PutBlobResult> PutBlobAsync(OperationContext context, ShortHash hash, byte[] blob)
        {
            return ExecuteAsync(context, async (context, service) =>
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
            return ExecuteAsync(context, async (context, service) =>
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

        public Task<Result<bool>> CompareExchangeAsync(
            OperationContext context,
            StrongFingerprint strongFingerprint,
            SerializedMetadataEntry replacement,
            string expectedReplacementToken)
        {
            return ExecuteAsync(context, async (context, service) =>
            {
                var response = await service.CompareExchangeAsync(new CompareExchangeRequest()
                {
                    ContextId = context.TracingContext.TraceId,
                    StrongFingerprint = strongFingerprint,
                    Replacement = replacement,
                    ExpectedReplacementToken = expectedReplacementToken
                }, context.Token);

                return response.ToResult(r => r.Exchanged);
            },
            extraEndMessage: r => $"Exchanged=[{r.GetValueOrDefault()}]");
        }

        public Task<Result<LevelSelectors>> GetLevelSelectorsAsync(OperationContext context, Fingerprint weakFingerprint, int level)
        {
            return ExecuteAsync(context, async (context, service) =>
            {
                var response = await service.GetLevelSelectorsAsync(new GetLevelSelectorsRequest()
                {
                    ContextId = context.TracingContext.TraceId,
                    WeakFingerprint = weakFingerprint,
                    Level = level,
                }, context.Token);

                return response.ToResult(r => new LevelSelectors(r.Selectors, r.HasMore));
            },
            extraEndMessage: r => $"Count=[{r.GetValueOrDefault()?.Selectors.Count}] HasMore=[{r.GetValueOrDefault()?.HasMore ?? false}]");
        }

        public Task<Result<SerializedMetadataEntry>> GetContentHashListAsync(OperationContext context, StrongFingerprint strongFingerprint)
        {
            return ExecuteAsync(context, async (context, service) =>
            {
                var response = await service.GetContentHashListAsync(new GetContentHashListRequest()
                {
                    ContextId = context.TracingContext.TraceId,
                    StrongFingerprint = strongFingerprint,
                }, context.Token);

                return response.ToResult(r => r.MetadataEntry, isNullAllowed: true);
            },
            // TODO: What to log here?
            extraEndMessage: r => r.GetValueOrDefault()?.ToString());
        }
    }
}
