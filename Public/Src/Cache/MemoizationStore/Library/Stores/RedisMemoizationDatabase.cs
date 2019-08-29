// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Utilities;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using System.Linq;
using StackExchange.Redis;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using System.Threading;

namespace BuildXL.Cache.MemoizationStore.Stores
{
    /// <summary>
    /// Implements a memoization database through Redis.
    /// </summary>
    public class RedisMemoizationDatabase : MemoizationDatabase
    {
        private readonly RedisDatabaseAdapter _redis;
        private readonly IClock _clock;

        private readonly ObjectPool<StreamBinaryWriter> _writerPool = new ObjectPool<StreamBinaryWriter>(() => new StreamBinaryWriter(), w => w.ResetPosition());
        private readonly ObjectPool<StreamBinaryReader> _readerPool = new ObjectPool<StreamBinaryReader>(() => new StreamBinaryReader(), r => { });

        /// <summary>
        /// Fine-grained locks that used for all operations that mutate Metadata records.
        /// </summary>
        private readonly SemaphoreSlim[] _metadataLocks = Enumerable.Range(0, byte.MaxValue + 1).Select(s => new SemaphoreSlim(1)).ToArray();

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
        public override async Task<Result<bool>> CompareExchange(OperationContext context, StrongFingerprint strongFingerprint, ContentHashListWithDeterminism expected, ContentHashListWithDeterminism replacement)
        {
            var key = GetKey(strongFingerprint.WeakFingerprint);
            byte[] selectorBytes = SerializeSelector(strongFingerprint.Selector);

            await _metadataLocks[strongFingerprint.WeakFingerprint.ToByteArray()[0]].WaitAsync();

            try
            {
                byte[] metadataBytes = await _redis.GetHashValueAsync(context, key, selectorBytes, context.Token);

                if (metadataBytes != null)
                {
                    var current = DeserializeMetadataEntry(metadataBytes);
                    if (!current.ContentHashListWithDeterminism.Equals(expected))
                    {
                        return false;
                    }
                }

                var replacementMetadata = new MetadataEntry(replacement, _clock.UtcNow.ToFileTimeUtc());
                return await _redis.SetHashValueAsync(context, key, selectorBytes, metadataBytes, When.Always, context.Token);
            }
            finally
            {
                _metadataLocks[selectorBytes[0]].Release();
            }
        }

        /// <inheritdoc />
        public override async Task<IEnumerable<StructResult<StrongFingerprint>>> EnumerateStrongFingerprintsAsync(OperationContext context)
        {
            var getFingerprintsTasks = _redis.GetServers()
                .Select(tuple => tuple.server)
                .SelectMany(server => server.Keys())
                .Select(key => DeserializeKey(key))
                .Where(key => key != default)
                .Select(async weakFingerprint =>
                {
                    var selectorsResult = await GetLevelSelectorsAsync(context, weakFingerprint, 1);
                    if (!selectorsResult)
                    {
                        return Array.Empty<StructResult<StrongFingerprint>>();
                    }
                    return selectorsResult.Value.Selectors
                        .Select(selector => new StructResult<StrongFingerprint>(new StrongFingerprint(weakFingerprint, selector)));
                })
                .ToArray();

            await Task.WhenAll(getFingerprintsTasks);

            return getFingerprintsTasks.SelectMany(task => task.Result);
        }

        /// <inheritdoc />
        public override async Task<GetContentHashListResult> GetContentHashListAsync(OperationContext context, StrongFingerprint strongFingerprint)
        {
            var key = GetKey(strongFingerprint.WeakFingerprint);
            byte[] selectorBytes = SerializeSelector(strongFingerprint.Selector);

            byte[] metadataBytes = await _redis.GetHashValueAsync(context, key, selectorBytes, context.Token);

            if (metadataBytes == null)
            {
                return new GetContentHashListResult("Strong fingerprint was not found.");
            }

            var metadata = DeserializeMetadataEntry(metadataBytes);

            // Update the time, only if no one else has changed it in the mean time. We don't
            // really care if this succeeds or not, because if it doesn't it only means someone
            // else changed the stored value before this operation but after it was read.
            CompareExchange(context, strongFingerprint, metadata.ContentHashListWithDeterminism, metadata.ContentHashListWithDeterminism).FireAndForget(context);

            return new GetContentHashListResult(metadata.ContentHashListWithDeterminism);
        }

        /// <inheritdoc />
        public override async Task<Result<LevelSelectors>> GetLevelSelectorsAsync(OperationContext context, Fingerprint weakFingerprint, int level)
        {
            var weakFingerprintKey = GetKey(weakFingerprint);

            var redisValues = await _redis.GetHashFieldsAsync(context, weakFingerprintKey, context.Token);

            var result = new Selector[redisValues.Length];
            for (var i = 0; i < redisValues.Length; i++)
            {
                byte[] selector = redisValues[i];
                using (var pooledReader = _readerPool.GetInstance())
                {
                    var reader = pooledReader.Instance;
                    result[i] = reader.Deserialize(new ArraySegment<byte>(selector), reader => Selector.Deserialize(reader));
                }
            }

            return LevelSelectors.Single<Selector[]>(result);
        }

        private Fingerprint DeserializeKey(string key)
        {
            if (Fingerprint.TryParse(key.Substring(3), out var weakFingerprint))
            {
                return weakFingerprint;
            }
            return default;
        }

        private byte[] SerializeSelector(Selector selector)
        {
            using (var pooledWriter = _writerPool.GetInstance())
            {
                var writer = pooledWriter.Instance.Writer;
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
