// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    /// <summary>
    /// A global content metadata store client which routes requests to a remote machine.
    /// </summary>
    public class ClientGlobalCacheStore : GrpcCodeFirstClient<IGlobalCacheService>, IGlobalCacheStore
    {
        /// <inheritdoc />
        public override bool AllowMultipleStartupAndShutdowns => true;

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new(nameof(ClientGlobalCacheStore));

        public ClientGlobalCacheStore(
            IFixedClientAccessor<IGlobalCacheService> metadataServiceClientFactory,
            ClientContentMetadataStoreConfiguration configuration)
            : base(metadataServiceClientFactory, CreateRetryPolicy(configuration), SystemClock.Instance, configuration.OperationTimeout)
        {
        }

        private static IRetryPolicy CreateRetryPolicy(ClientContentMetadataStoreConfiguration configuration)
        {
            return RetryPolicyFactory.GetExponentialPolicy(
                _ => true,
                // We use an absurdly high retry count because the actual operation timeout is controlled through
                // PerformOperationAsync in ExecuteAsync.
                1_000_000,
                configuration.RetryMinimumWaitTime,
                configuration.RetryMaximumWaitTime,
                configuration.RetryDelta);
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

        public ValueTask<BoolResult> DeleteLocationAsync(OperationContext context, MachineId machineId, IReadOnlyList<ShortHash> contentHashes)
        {
            return new ValueTask<BoolResult>(
                ExecuteAsync(context, async (context, callOptions, service) =>
                {
                    var response = await service.DeleteContentLocationsAsync(new DeleteContentLocationsRequest()
                    {
                        ContextId = context.TracingContext.TraceId,
                        Hashes = contentHashes,
                        MachineId = machineId,
                    }, callOptions);

                    return response.ToBoolResult();
                },
                extraEndMessage: _ =>
                {
                    var csv = string.Join(",", contentHashes);
                    return $"MachineId=[{machineId}] Hashes=(#{contentHashes.Count})[{csv}]";
                }));
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

        /// <inheritdoc/>
        public Task<GetStatsResult> GetStatsAsync(Context context)
        {
            return Task.FromResult(
                new GetStatsResult(errorMessage: $"{nameof(ClientGlobalCacheStore)} does not support {nameof(GetStatsAsync)}"));
        }
    }
}
