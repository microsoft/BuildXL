﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;
using BuildXL.Utilities;
using StackExchange.Redis;
using BuildXL.Cache.ContentStore.UtilitiesCore.Internal;
using System.Security.Cryptography;

namespace BuildXL.Cache.MemoizationStore.Distributed.Stores
{
    /// <summary>
    /// Implements a memoization database through Redis.
    /// </summary>
    internal class RedisMemoizationDatabase : MemoizationDatabase
    {
        private readonly RedisDatabaseAdapter _redis;
        private readonly IClock _clock;

        private readonly ObjectPool<StreamBinaryWriter> _writerPool = new ObjectPool<StreamBinaryWriter>(() => new StreamBinaryWriter(), w => w.ResetPosition());
        private readonly ObjectPool<StreamBinaryReader> _readerPool = new ObjectPool<StreamBinaryReader>(() => new StreamBinaryReader(), r => { });

        /// <inheritdoc />
        protected override Tracer Tracer => new Tracer(nameof(RedisMemoizationDatabase));

        private string GetKey(Fingerprint weakFingerprint) => $"WF_{weakFingerprint.Serialize()}";

        /// <nodoc />
        public RedisMemoizationDatabase(
            RedisDatabaseAdapter redis,
            IClock clock
            )
        {
            _redis = redis;
            _clock = clock;
        }

        /// <inheritdoc />
        public override Task<Result<bool>> CompareExchange(OperationContext context, StrongFingerprint strongFingerprint, string expectedReplacementToken, ContentHashListWithDeterminism expected, ContentHashListWithDeterminism replacement)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var key = GetKey(strongFingerprint.WeakFingerprint);
                    var replacementMetadata = new MetadataEntry(replacement, _clock.UtcNow.ToFileTimeUtc());
                    var replacementBytes = SerializeMetadataEntry(replacementMetadata);

                    byte[] selectorBytes = SerializeSelector(strongFingerprint.Selector, isHash: false);
                    byte[] tokenFieldNameBytes = SerializeSelector(strongFingerprint.Selector, isHash: true);

                    var newReplacementToken = Guid.NewGuid().ToString();

                    var result = await _redis.ExecuteBatchAsync(context, batch => batch.CompareExchangeAsync(key, selectorBytes, tokenFieldNameBytes, expectedReplacementToken, replacementBytes, newReplacementToken), RedisOperation.CompareExchange);
                    return new Result<bool>(result);
                });
        }

        /// <inheritdoc />
        public override Task<IEnumerable<StructResult<StrongFingerprint>>> EnumerateStrongFingerprintsAsync(OperationContext context)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override async Task<Result<(ContentHashListWithDeterminism contentHashListInfo, string replacementToken)>> GetContentHashListAsync(OperationContext context, StrongFingerprint strongFingerprint)
        {
            var key = GetKey(strongFingerprint.WeakFingerprint);

            (byte[] metadataBytes, string replacementToken) = await _redis.ExecuteBatchAsync(context,
                async batch =>
                {
                    byte[] selectorBytes = SerializeSelector(strongFingerprint.Selector, isHash: false);
                    var metadataBytesTask = batch.AddOperation(key, b => b.HashGetAsync(key, selectorBytes));

                    byte[] replacementTokenFieldName = SerializeSelector(strongFingerprint.Selector, isHash: true);
                    var replacementTokenTask = batch.AddOperation(key, b => b.HashGetAsync(key, replacementTokenFieldName));

                    return ((byte[])await metadataBytesTask, (string)await replacementTokenTask);
                },
                RedisOperation.GetContentHashList);

            if (metadataBytes == null)
            {
                return new Result<(ContentHashListWithDeterminism contentHashListInfo, string replacementToken)>("Strong fingerprint was not found.");
            }

            var metadata = DeserializeMetadataEntry(metadataBytes);

            // Update the time, only if no one else has changed it in the mean time. We don't
            // really care if this succeeds or not, because if it doesn't it only means someone
            // else changed the stored value before this operation but after it was read.
            CompareExchange(context, strongFingerprint, replacementToken, metadata.ContentHashListWithDeterminism, metadata.ContentHashListWithDeterminism).FireAndForget(context);

            return new Result<(ContentHashListWithDeterminism, string)>((metadata.ContentHashListWithDeterminism, replacementToken));
        }

        /// <inheritdoc />
        public override async Task<Result<LevelSelectors>> GetLevelSelectorsAsync(OperationContext context, Fingerprint weakFingerprint, int level)
        {
            var weakFingerprintKey = GetKey(weakFingerprint);

            var redisValues = await _redis.GetHashKeysAsync(context, weakFingerprintKey, context.Token);

            var result = new List<Selector>(redisValues.Length / 2);
            for (var i = 0; i < redisValues.Length; i++)
            {
                byte[] selectorBytes = redisValues[i];
                var (selector, isHash) = DeserializeSelector(selectorBytes);

                if (!isHash)
                {
                    result.Add(selector);
                }
            }

            return LevelSelectors.Single<List<Selector>>(result);
        }

        private Fingerprint DeserializeKey(string key)
        {
            if (Fingerprint.TryParse(key.Substring(3), out var weakFingerprint))
            {
                return weakFingerprint;
            }
            return default;
        }

        private (Selector, bool isHash) DeserializeSelector(byte[] selectorBytes)
        {
            using (var pooledReader = _readerPool.GetInstance())
            {
                var reader = pooledReader.Instance;
                return reader.Deserialize(new ArraySegment<byte>(selectorBytes), reader =>
                {
                    var isHash = reader.ReadBoolean();
                    var selector = Selector.Deserialize(reader);
                    return (selector, isHash);
                });
            }
        }

        private byte[] SerializeSelector(Selector selector, bool isHash)
        {
            using (var pooledWriter = _writerPool.GetInstance())
            {
                var writer = pooledWriter.Instance.Writer;
                writer.Write(isHash);
                selector.Serialize(writer);
                return pooledWriter.Instance.Buffer.ToArray();
            }
        }

        private byte[] SerializeMetadataEntry(MetadataEntry metadata)
        {
            using (var pooledWriter = _writerPool.GetInstance())
            {
                var writer = pooledWriter.Instance.Writer;
                metadata.Serialize(writer);
                return pooledWriter.Instance.Buffer.ToArray();
            }
        }

        private MetadataEntry DeserializeMetadataEntry(byte[] bytes)
        {
            using (var pooledReader = _readerPool.GetInstance())
            {
                var reader = pooledReader.Instance;
                return reader.Deserialize(new ArraySegment<byte>(bytes), reader => MetadataEntry.Deserialize(reader));
            }
        }
    }
}
