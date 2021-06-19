// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;
using BuildXL.Utilities;
#if MICROSOFT_INTERNAL
using Microsoft.Caching.Redis;
#else
using StackExchange.Redis;
#endif

namespace BuildXL.Cache.MemoizationStore.Distributed.Stores
{
    /// <summary>
    /// Implements a memoization database through Redis.
    /// You can imagine the schema in Redis to be:
    ///     WeakFingerprint -> Dictionary(Selector, (ContentHashList:byte[], replacementToken:string))
    /// The difference is that, since Redis does not have the concept of value tuple, we duplicate each Selector, prefixed with either a 1 or a 0, depending if we're storing the ContentHasList or the token.
    /// The motivation behind using the token (which is just a GUID created on adds) is to avoid comparing the two byte arrays.
    ///
    /// TODO: Deprecate. This is superseded by <see cref="RedisGlobalStore"/> via <see cref="RedisMemoizationAdapter"/>.
    /// </summary>
    internal class RedisMemoizationDatabase : MemoizationDatabase
    {
        private readonly RaidedRedisDatabase _redis;
        private readonly SerializationPool _serializationPool = new SerializationPool();

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(RedisMemoizationDatabase));

        private string GetKey(Fingerprint weakFingerprint) => $"WF_{weakFingerprint.Serialize()}";

        private RedisMemoizationConfiguration Configuration { get; }

        /// <nodoc />
        public RedisMemoizationDatabase(
            RaidedRedisDatabase redis,
            RedisMemoizationConfiguration configuration)
        {
            _redis = redis;
            Configuration = configuration;
        }

        /// <nodoc />
        public RedisMemoizationDatabase(
            RedisDatabaseAdapter primaryRedis,
            RedisDatabaseAdapter secondaryRedis,
            RedisMemoizationConfiguration configuration)
        {
            _redis = new RaidedRedisDatabase(Tracer, primaryRedis, secondaryRedis);
            Configuration = configuration;
        }

        /// <inheritdoc />
        protected override Task<Result<bool>> CompareExchangeCore(OperationContext context, StrongFingerprint strongFingerprint, string expectedReplacementToken, ContentHashListWithDeterminism expected, ContentHashListWithDeterminism replacement)
        {
            var newReplacementToken = Guid.NewGuid().ToString();
            return CompareExchangeInternalAsync(context, strongFingerprint, expectedReplacementToken, replacement, newReplacementToken);
        }

        private async Task<Result<bool>> CompareExchangeInternalAsync(
            OperationContext context,
            StrongFingerprint strongFingerprint,
            string expectedReplacementToken,
            ContentHashListWithDeterminism replacement,
            string newReplacementToken)
        {
            var key = GetKey(strongFingerprint.WeakFingerprint);
            var replacementMetadata = new MetadataEntry(replacement, DateTime.UtcNow);
            var replacementBytes = SerializeMetadataEntry(replacementMetadata);

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
                                newReplacementToken);
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
        public override Task<IEnumerable<Result<StrongFingerprint>>> EnumerateStrongFingerprintsAsync(OperationContext context)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        protected override async Task<ContentHashListResult> GetContentHashListCoreAsync(
            OperationContext context, StrongFingerprint strongFingerprint, bool preferShared)
        {
            var key = GetKey(strongFingerprint.WeakFingerprint);
            byte[] selectorBytes = SerializeSelector(strongFingerprint.Selector, isReplacementToken: false);
            byte[] replacementTokenFieldName = SerializeSelector(strongFingerprint.Selector, isReplacementToken: true);

            var (primaryResult, secondaryResult) = await _redis.ExecuteRaidedAsync<(byte[], string)>(
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
                            return ((byte[])await metadataBytesTask, (string)await replacementTokenTask);
                        },
                        RedisOperation.GetContentHashList);
                },
                retryWindow: Configuration.SlowOperationCancellationTimeout);
            Contract.Assert(primaryResult != null || secondaryResult != null);

            MetadataEntry? metadata = null;
            string replacementToken = null;

            if (primaryResult != null && primaryResult.Succeeded)
            {
                var (primaryMetadataBytes, primaryReplacementToken) = primaryResult.Value;
                if (primaryMetadataBytes != null)
                {
                    metadata = DeserializeMetadataEntry(primaryMetadataBytes);
                    replacementToken = primaryReplacementToken;
                }
            }

            if (secondaryResult != null && secondaryResult.Succeeded)
            {
                var (secondaryMetadataBytes, secondaryReplacementToken) = secondaryResult.Value;
                if (secondaryMetadataBytes != null)
                {
                    var secondaryMetadata = DeserializeMetadataEntry(secondaryMetadataBytes);
                    if ((metadata?.LastAccessTimeUtc ?? DateTime.MinValue) < secondaryMetadata.LastAccessTimeUtc)
                    {
                        metadata = secondaryMetadata;
                        replacementToken = secondaryReplacementToken;
                    }
                }
            }

            if (metadata == null)
            {
                return new ContentHashListResult(contentHashListInfo: default, string.Empty);
            }

            // Update the time, only if no one else has changed it in the mean time. We don't
            // really care if this succeeds or not, because if it doesn't it only means someone
            // else changed the stored value before this operation but after it was read.
            await CompareExchangeInternalAsync(
                context,
                strongFingerprint,
                replacementToken,
                metadata.Value.ContentHashListWithDeterminism,
                replacementToken).IgnoreFailure();

            return new ContentHashListResult(metadata.Value.ContentHashListWithDeterminism, replacementToken);
        }

        /// <inheritdoc />
        protected override async Task<Result<LevelSelectors>> GetLevelSelectorsCoreAsync(OperationContext context, Fingerprint weakFingerprint, int level)
        {
            var weakFingerprintKey = GetKey(weakFingerprint);

            var (primaryResult, secondaryResult) = await _redis.ExecuteRaidedAsync<RedisValue[]>(context, async (redis, cancellationToken) =>
            {
                using var nestedContext = new CancellableOperationContext(context, cancellationToken);
                return await redis.GetHashKeysAsync(nestedContext, weakFingerprintKey, nestedContext.Context.Token);
            }, retryWindow: null);
            Contract.Assert(primaryResult != null || secondaryResult != null);

            IEnumerable<RedisValue> enumerable = Array.Empty<RedisValue>();
            if (primaryResult != null && primaryResult.Succeeded)
            {
                enumerable = enumerable.Concat(primaryResult.Value);
            }

            if (secondaryResult != null && secondaryResult.Succeeded)
            {
                enumerable = enumerable.Concat(secondaryResult.Value);
            }

            var result = new HashSet<Selector>();
            foreach (var selectorBytes in enumerable)
            {
                var (selector, isReplacementToken) = DeserializeSelector(selectorBytes);

                if (!isReplacementToken)
                {
                    result.Add(selector);
                }
            }

            return LevelSelectors.Single<List<Selector>>(result.ToList());
        }

        private (Selector, bool isReplacementToken) DeserializeSelector(byte[] selectorBytes)
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
            return _serializationPool.Serialize(selector, isReplacementToken, (isReplacementToken, selector, writer) =>
            {
                writer.Write(isReplacementToken);
                selector.Serialize(writer);
            });
        }

        private byte[] SerializeMetadataEntry(MetadataEntry metadata)
        {
            return _serializationPool.Serialize(metadata, (metadata, writer) => metadata.Serialize(writer));
        }

        private MetadataEntry DeserializeMetadataEntry(byte[] bytes)
        {
            return _serializationPool.Deserialize(bytes, reader => MetadataEntry.Deserialize(reader));
        }
    }
}
