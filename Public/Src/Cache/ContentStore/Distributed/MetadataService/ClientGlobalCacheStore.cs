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
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using Grpc.Core;
using Polly;

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

        private readonly ClientContentMetadataStoreConfiguration _configuration;

        private readonly IClock _clock;

        private readonly IRetryPolicy _retryPolicy;

        public ClientGlobalCacheStore(
            IClientAccessor<IGlobalCacheService> metadataServiceClientFactory,
            ClientContentMetadataStoreConfiguration configuration)
        {
            _serviceClientFactory = metadataServiceClientFactory;
            _configuration = configuration;
            _clock = SystemClock.Instance;

            _retryPolicy = RetryPolicyFactory.GetExponentialPolicy(
                _ => true,
                // We use an absurdly high retry count because the actual operation timeout is controlled through
                // PerformOperationAsync in ExecuteAsync.
                1_000_000,
                _configuration.RetryMinimumWaitTime,
                _configuration.RetryMaximumWaitTime,
                _configuration.RetryDelta);

            LinkLifetime(_serviceClientFactory);
        }

        private async Task<TResult> ExecuteAsync<TResult>(
            OperationContext originalContext,
            Func<OperationContext, CallOptions, IGlobalCacheService, Task<TResult>> executeAsync,
            Func<TResult, string?> extraEndMessage,
            string? extraStartMessage = null,
            [CallerMemberName] string caller = null!)
            where TResult : ResultBase
        {
            var attempt = -1;
            using var contextWithShutdown = TrackShutdown(originalContext);
            var context = contextWithShutdown.Context;
            var callerAttempt = $"{caller}_Attempt";

            return await context.PerformOperationWithTimeoutAsync(
                Tracer,
                context =>
                {
                    var callOptions = new CallOptions(
                        headers: new Metadata()
                        {
                            MetadataServiceSerializer.CreateContextIdHeaderEntry(context.TracingContext.TraceId)
                        },
                        deadline: _clock.UtcNow + _configuration.OperationTimeout,
                        cancellationToken: context.Token);

                    return _retryPolicy.ExecuteAsync(async () =>
                    {
                        await Task.Yield();

                        attempt++;

                        var stopwatch = StopwatchSlim.Start();
                        var clientCreationTime = TimeSpan.Zero;

                        var result = await context.PerformOperationAsync(Tracer, () =>
                            {
                                return _serviceClientFactory.UseAsync(context, service =>
                                {
                                    clientCreationTime = stopwatch.Elapsed;

                                    return executeAsync(context, callOptions, service);
                                });
                            },
                            extraStartMessage: extraStartMessage,
                            extraEndMessage: r => $"Attempt=[{attempt}] ClientCreationTimeMs=[{clientCreationTime.TotalMilliseconds}] {extraEndMessage(r)}",
                            caller: callerAttempt,
                            traceErrorsOnly: true);

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

        public Task<Result<IReadOnlyList<ContentLocationEntry>>> GetBulkAsync(OperationContext context, IReadOnlyList<ShortHash> contentHashes)
        {
            return ExecuteAsync(context, async (context, callOptions, service) =>
            {
                var response = await service.GetContentLocationsAsync(new GetContentLocationsRequest()
                {
                    Hashes = contentHashes,
                }, callOptions);

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
                ExecuteAsync(context, async (context, callOptions, service) =>
                {
                    var response = await service.RegisterContentLocationsAsync(new RegisterContentLocationsRequest()
                    {
                        ContextId = context.TracingContext.TraceId,
                        Hashes = contentHashes,
                        MachineId = machineId,
                    }, callOptions);

                    return response.ToBoolResult();
                },
                extraEndMessage: _ =>
                {
                    var csv = string.Join(",", contentHashes.Select(s => s.Hash));
                    return $"MachineId=[{machineId}] Touch=[{touch}] Hashes=(#{contentHashes.Count})[{csv}]";
                }));
        }

        public Task<PutBlobResult> PutBlobAsync(OperationContext context, ShortHash hash, byte[] blob)
        {
            return ExecuteAsync(context, async (context, callOptions, service) =>
            {
                var response = await service.PutBlobAsync(new PutBlobRequest()
                {
                    ContentHash = hash,
                    Blob = blob,
                }, callOptions);

                return response.ToPutBlobResult(hash, blob.Length);
            },
            extraEndMessage: _ => $"Hash=[{hash}] Size=[{blob.Length}]");
        }

        public Task<GetBlobResult> GetBlobAsync(OperationContext context, ShortHash hash)
        {
            return ExecuteAsync(context, async (context, callOptions, service) =>
            {
                var response = await service.GetBlobAsync(new GetBlobRequest()
                {
                    ContentHash = hash,
                }, callOptions);

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
            return ExecuteAsync(context, async (context, callOptions, service) =>
            {
                var response = await service.CompareExchangeAsync(new CompareExchangeRequest()
                {
                    StrongFingerprint = strongFingerprint,
                    Replacement = replacement,
                    ExpectedReplacementToken = expectedReplacementToken
                }, callOptions);

                return response.ToResult(r => r.Exchanged);
            },
            extraEndMessage: r => $"Exchanged=[{r.GetValueOrDefault()}]");
        }

        public Task<Result<LevelSelectors>> GetLevelSelectorsAsync(OperationContext context, Fingerprint weakFingerprint, int level)
        {
            return ExecuteAsync(context, async (context, callOptions, service) =>
            {
                var response = await service.GetLevelSelectorsAsync(new GetLevelSelectorsRequest()
                {
                    WeakFingerprint = weakFingerprint,
                    Level = level,
                }, callOptions);

                return response.ToResult(r => new LevelSelectors(r.Selectors, r.HasMore));
            },
            extraEndMessage: r => $"Count=[{r.GetValueOrDefault()?.Selectors.Count}] HasMore=[{r.GetValueOrDefault()?.HasMore ?? false}]");
        }

        public Task<Result<SerializedMetadataEntry>> GetContentHashListAsync(OperationContext context, StrongFingerprint strongFingerprint)
        {
            return ExecuteAsync(context, async (context, callOptions, service) =>
            {
                var response = await service.GetContentHashListAsync(new GetContentHashListRequest()
                {
                    StrongFingerprint = strongFingerprint,
                }, callOptions);

                return response.ToResult(r => r.MetadataEntry, isNullAllowed: true);
            },
            // TODO: What to log here?
            extraEndMessage: r => r.GetValueOrDefault()?.ToString());
        }

        public Task<Result<MachineMapping>> RegisterMachineAsync(OperationContext context, MachineLocation machineLocation)
        {
            throw new NotImplementedException($"Attempt to use {nameof(ClientGlobalCacheStore)} for machine registration is unsupported");
        }

        public Task<BoolResult> ForceRegisterMachineAsync(OperationContext context, MachineMapping mapping)
        {
            throw new NotImplementedException($"Attempt to use {nameof(ClientGlobalCacheStore)} for machine registration is unsupported");
        }
    }
}
