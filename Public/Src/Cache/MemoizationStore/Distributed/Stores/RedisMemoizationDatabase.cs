// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
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

namespace BuildXL.Cache.MemoizationStore.Distributed.Stores
{
    /// <summary>
    /// Implements a memoization database through Redis.
    /// You can imagine the schema in Redis to be:
    ///     WeakFingerprint -> Dictionary(Selector, (ContentHashList:byte[], replacementToken:string))
    /// The difference is that, since Redis does not have the concept of value tuple, we duplicate each Selector, prefixed with either a 1 or a 0, depending if we're storing the ContentHasList or the token.
    /// The motivation behind using the token (which is just a GUID created on adds) is to avoid comparing the two byte arrays.
    /// </summary>
    internal class RedisMemoizationDatabase : MemoizationDatabase
    {
        private readonly RedisDatabaseAdapter _redis;
        private readonly IClock _clock;

        private readonly SerializationPool _serializationPool = new SerializationPool();

        private readonly TimeSpan _metadataExpiryTime;

        /// <inheritdoc />
        protected override Tracer Tracer => new Tracer(nameof(RedisMemoizationDatabase));

        private string GetKey(Fingerprint weakFingerprint) => $"WF_{weakFingerprint.Serialize()}";

        /// <nodoc />
        public RedisMemoizationDatabase(
            RedisDatabaseAdapter redis,
            IClock clock,
            TimeSpan metadataExpiryTime
            )
        {
            _redis = redis;
            _clock = clock;
            _metadataExpiryTime = metadataExpiryTime;
        }

        /// <inheritdoc />
        protected override async Task<Result<bool>> CompareExchangeCore(OperationContext context, StrongFingerprint strongFingerprint, string expectedReplacementToken, ContentHashListWithDeterminism expected, ContentHashListWithDeterminism replacement)
        {
            var newReplacementToken = Guid.NewGuid().ToString();
            return Result.Success(await CompareExchangeInternalAsync(context, strongFingerprint, expectedReplacementToken, expected, replacement, newReplacementToken));
        }

        private Task<bool> CompareExchangeInternalAsync(OperationContext context, StrongFingerprint strongFingerprint, string expectedReplacementToken, ContentHashListWithDeterminism expected, ContentHashListWithDeterminism replacement, string newReplacementToken)
        {
            var key = GetKey(strongFingerprint.WeakFingerprint);
            var replacementMetadata = new MetadataEntry(replacement, _clock.UtcNow);
            var replacementBytes = SerializeMetadataEntry(replacementMetadata);

            byte[] selectorBytes = SerializeSelector(strongFingerprint.Selector, isReplacementToken: false);
            byte[] tokenFieldNameBytes = SerializeSelector(strongFingerprint.Selector, isReplacementToken: true);

            return _redis.ExecuteBatchAsync(context, batch =>
            {
                var task = batch.CompareExchangeAsync(key, selectorBytes, tokenFieldNameBytes, expectedReplacementToken, replacementBytes, newReplacementToken);
                batch.KeyExpireAsync(key, _metadataExpiryTime).FireAndForget(context);
                return task;
            }, RedisOperation.CompareExchange);
        }

        /// <inheritdoc />
        public override Task<IEnumerable<StructResult<StrongFingerprint>>> EnumerateStrongFingerprintsAsync(OperationContext context)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        protected override async Task<Result<(ContentHashListWithDeterminism contentHashListInfo, string replacementToken)>> GetContentHashListCoreAsync(OperationContext context, StrongFingerprint strongFingerprint, bool preferShared)
        {
            var key = GetKey(strongFingerprint.WeakFingerprint);

            (byte[] metadataBytes, string replacementToken) = await _redis.ExecuteBatchAsync(context,
                async batch =>
                {
                    byte[] selectorBytes = SerializeSelector(strongFingerprint.Selector, isReplacementToken: false);
                    var metadataBytesTask = batch.AddOperation(key, b => b.HashGetAsync(key, selectorBytes));

                    byte[] replacementTokenFieldName = SerializeSelector(strongFingerprint.Selector, isReplacementToken: true);
                    var replacementTokenTask = batch.AddOperation(key, b => b.HashGetAsync(key, replacementTokenFieldName));

                    return ((byte[])await metadataBytesTask, (string)await replacementTokenTask);
                },
                RedisOperation.GetContentHashList);

            if (metadataBytes == null)
            {
                return new Result<(ContentHashListWithDeterminism contentHashListInfo, string replacementToken)>((default, string.Empty));
            }

            var metadata = DeserializeMetadataEntry(metadataBytes);

            // Update the time, only if no one else has changed it in the mean time. We don't
            // really care if this succeeds or not, because if it doesn't it only means someone
            // else changed the stored value before this operation but after it was read.
            await CompareExchangeInternalAsync(
                context,
                strongFingerprint,
                replacementToken,
                metadata.ContentHashListWithDeterminism,
                metadata.ContentHashListWithDeterminism,
                replacementToken);

            return new Result<(ContentHashListWithDeterminism, string)>((metadata.ContentHashListWithDeterminism, replacementToken));
        }

        /// <inheritdoc />
        protected override async Task<Result<LevelSelectors>> GetLevelSelectorsCoreAsync(OperationContext context, Fingerprint weakFingerprint, int level)
        {
            var weakFingerprintKey = GetKey(weakFingerprint);

            var redisValues = await _redis.GetHashKeysAsync(context, weakFingerprintKey, context.Token);

            var result = new List<Selector>(redisValues.Length / 2);
            for (var i = 0; i < redisValues.Length; i++)
            {
                byte[] selectorBytes = redisValues[i];
                var (selector, isReplacementToken) = DeserializeSelector(selectorBytes);

                if (!isReplacementToken)
                {
                    result.Add(selector);
                }
            }

            return LevelSelectors.Single<List<Selector>>(result);
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
