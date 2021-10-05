// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities;
#if MICROSOFT_INTERNAL
using Microsoft.Caching.Redis;
#else
using StackExchange.Redis;
#endif

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Implements a memoization database through Redis.
    /// You can imagine the schema in Redis to be:
    ///     WeakFingerprint -> Dictionary(Selector, (ContentHashList:byte[], replacementToken:string))
    /// The difference is that, since Redis does not have the concept of value tuple, we duplicate each Selector, prefixed with either a 1 or a 0, depending if we're storing the ContentHasList or the token.
    /// The motivation behind using the token (which is just a GUID created on adds) is to avoid comparing the two byte arrays.
    ///
    /// NOTE: This code is essentially copied from RedisMemoizationDatabase which should be deprecated.
    /// </summary>
    internal class RedisMemoizationAdapter
    {
        private readonly SerializationPool _serializationPool = new SerializationPool();

        private static readonly Selector[] EmptySelectors = Array.Empty<Selector>();

        private readonly RaidedRedisDatabase _redis;

        private RedisMemoizationConfiguration Configuration { get; }

        /// <nodoc />
        public RedisMemoizationAdapter(
            RaidedRedisDatabase redis,
            RedisMemoizationConfiguration configuration)
        {
            _redis = redis;
            Configuration = configuration;
        }

        private string GetKey(Fingerprint weakFingerprint) => $"WF_{weakFingerprint.Serialize()}";

        public async Task<Result<bool>> CompareExchangeAsync(
            OperationContext context,
            StrongFingerprint strongFingerprint,
            SerializedMetadataEntry replacement,
            string expectedReplacementToken)
        {
            var key = GetKey(strongFingerprint.WeakFingerprint);
            var replacementBytes = replacement.Data;

            byte[] selectorBytes = SerializeSelector(strongFingerprint.Selector, isReplacementToken: false);
            byte[] tokenFieldNameBytes = SerializeSelector(strongFingerprint.Selector, isReplacementToken: true);

            var (primaryResult, secondaryResult) = await _redis.ExecuteRaidedAsync<bool>(
                context,
                async (redis, cancellationToken) =>
                {
                    using var nestedContext = new CancellableOperationContext(context, cancellationToken);

                    return await redis.ExecuteBatchAsync(
                        nestedContext,
                        batch =>
                        {
                            var task = batch.CompareExchangeAsync(
                                key,
                                selectorBytes,
                                tokenFieldNameBytes,
                                expectedReplacementToken,
                                replacementBytes,
                                replacement.ReplacementToken);
                            batch.KeyExpireAsync(key, Configuration.ExpiryTime).FireAndForget(nestedContext);
                            return task;
                        },
                        RedisOperation.CompareExchange);
                },
                retryWindow: Configuration.SlowOperationCancellationTimeout);

            Contract.Assert(primaryResult != null || secondaryResult != null);

            if (primaryResult?.Succeeded == true || secondaryResult?.Succeeded == true)
            {
                // One of the operations is successful.
                return (primaryResult?.Value ?? false) || (secondaryResult?.Value ?? false);
            }

            // All operations failed, propagating the error back to the caller.
            var failure = primaryResult ?? secondaryResult;
            Contract.Assert(!failure.Succeeded);
            return new Result<bool>(failure);
        }

        /// <inheritdoc />
        public async Task<Result<SerializedMetadataEntry>> GetContentHashListAsync(
            OperationContext context, StrongFingerprint strongFingerprint)
        {
            var key = GetKey(strongFingerprint.WeakFingerprint);
            byte[] selectorBytes = SerializeSelector(strongFingerprint.Selector, isReplacementToken: false);
            byte[] replacementTokenFieldName = SerializeSelector(strongFingerprint.Selector, isReplacementToken: true);

            var (primaryResult, secondaryResult) = await _redis.ExecuteRaidedAsync<SerializedMetadataEntry>(
                context,
                async (redis, cancellationToken) =>
                {
                    using var nestedContext = new CancellableOperationContext(context, cancellationToken);

                    return await redis.ExecuteBatchAsync(
                        context,
                        async batch =>
                        {
                            var metadataBytesTask = batch.AddOperation(key, b => b.HashGetAsync(key, selectorBytes));
                            var replacementTokenTask = batch.AddOperation(key, b => b.HashGetAsync(key, replacementTokenFieldName));

                            return new SerializedMetadataEntry()
                            {
                                Data = await metadataBytesTask,
                                ReplacementToken = await replacementTokenTask
                            };
                        },
                        RedisOperation.GetContentHashList);
                },
                retryWindow: Configuration.SlowOperationCancellationTimeout);

            Contract.Assert(primaryResult != null || secondaryResult != null);
            return primaryResult ?? secondaryResult;
        }

        /// <inheritdoc />
        public async Task<Result<LevelSelectors>> GetLevelSelectorsAsync(OperationContext context, Fingerprint weakFingerprint, int level)
        {
            var weakFingerprintKey = GetKey(weakFingerprint);

            var (primaryResult, secondaryResult) = await _redis.ExecuteRaidedAsync<IEnumerable<Selector>>(context, async (redis, cancellationToken) =>
            {
                using var nestedContext = new CancellableOperationContext(context, cancellationToken);
                var keys = await redis.GetHashKeysAsync(nestedContext, weakFingerprintKey, nestedContext.Context.Token);

                return keys.Select(r => DeserializeSelector(r)).Where(t => !t.isReplacementToken).Select(t => t.selector).ToList();
            }, retryWindow: null);
            Contract.Assert(primaryResult != null || secondaryResult != null);

            var result = (primaryResult?.GetValueOrDefault() ?? EmptySelectors).Concat(secondaryResult?.GetValueOrDefault() ?? EmptySelectors).Distinct();
            return LevelSelectors.Single<List<Selector>>(result.ToList());
        }

        private (Selector selector, bool isReplacementToken) DeserializeSelector(byte[] selectorBytes)
        {
            return _serializationPool.Deserialize(selectorBytes, reader =>
            {
                var isReplacementToken = reader.ReadBoolean();
                var selector = Selector.Deserialize(reader);
                return (selector, isReplacementToken);
            });
        }

        private byte[] SerializeSelector(Selector selector, bool isReplacementToken)
        {
            using var pooled = _serializationPool.SerializePooled(selector, isReplacementToken, (isReplacementToken, selector, writer) =>
            {
                writer.Write(isReplacementToken);
                selector.Serialize(writer);
            });

            // Return a clone because the buffer will be returned to the pool and reused
            return pooled.ToArray();
        }
    }
}
