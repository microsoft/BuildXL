// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using StackExchange.Redis;

namespace BuildXL.Cache.MemoizationStore.Distributed.Utils
{
    /// <summary>
    /// Implementation of <see cref="IRedisSerializer"/>
    /// </summary>
    public class RedisSerializer : IRedisSerializer
    {
        /// <summary>
        /// Separator between different values
        /// </summary>
        internal const string RedisValueSeparator = "++";

        /// <summary>
        /// Indicator that the following value is not null
        /// </summary>
        internal const string RedisValueExistsSemaphore = "1";

        /// <inheritdoc />
        public RedisKey ToRedisKey(Fingerprint fingerprint)
        {
            return fingerprint.Serialize();
        }

        /// <inheritdoc />
        public RedisKey ToRedisKey(StrongFingerprint strongFingerprint)
        {
            return strongFingerprint.WeakFingerprint.Serialize() +
                   RedisValueSeparator +
                   Serialize(strongFingerprint.Selector);
        }

        /// <inheritdoc />
        public RedisValue[] ToRedisValues(IList<Selector> selectors)
        {
            return selectors
                        .Select(selector => (RedisValue)Serialize(selector))
                        .ToArray();
        }

        /// <inheritdoc />
        public RedisValue ToRedisValue(ContentHashListWithDeterminism hashList)
        {
            var determinismBytes = HexUtilities.BytesToHex(hashList.Determinism.Serialize());
            if (hashList.ContentHashList == null)
            {
                return determinismBytes + RedisValueSeparator + RedisValueSeparator + RedisValueSeparator + RedisValueSeparator;
            }

            var hashListValue = RedisValueExistsSemaphore + RedisValueSeparator + hashList.ContentHashList.Serialize();
            var payloadValue = hashList.ContentHashList.HasPayload
                ? RedisValueExistsSemaphore + RedisValueSeparator + HexUtilities.BytesToHex(hashList.ContentHashList.Payload.ToList())
                : RedisValueSeparator;

            return determinismBytes + RedisValueSeparator + hashListValue + RedisValueSeparator + payloadValue;
        }

        /// <inheritdoc />
        public IEnumerable<Selector> AsSelectors(RedisValue[] payload)
        {
            return payload.Select(ToSelector);
        }

        /// <inheritdoc />
        public ContentHashListWithDeterminism AsContentHashList(RedisValue value)
        {
            const int partCount = 5;
            var parts = value.ToString().Split(new[] { RedisValueSeparator }, partCount, StringSplitOptions.None);
            Contract.Assert(parts.Length == partCount);
            var determinism = CacheDeterminism.Deserialize(HexUtilities.HexToBytes(parts[0]));
            var payload = parts[3] == RedisValueExistsSemaphore ? HexUtilities.HexToBytes(parts[4]) : null;
            var hashList = parts[1] == RedisValueExistsSemaphore ? ContentHashList.Deserialize(parts[2], payload) : null;
            return new ContentHashListWithDeterminism(hashList, determinism);
        }

        /// <summary>
        ///     Serialize
        /// </summary>
        private static string Serialize(Selector selector)
        {
            return selector.ContentHash.Serialize() + RedisValueSeparator + HexUtilities.BytesToHex(selector.Output);
        }

        private static Selector ToSelector(RedisValue value)
        {
            var parts = value.ToString().Split(new[] { RedisValueSeparator }, 2, StringSplitOptions.RemoveEmptyEntries);
            ContentHash contentHash;
            if (!ContentHash.TryParse(parts[0], out contentHash))
            {
                contentHash = default(ContentHash);
            }

            byte[] output = parts.Length > 1 ? HexUtilities.HexToBytes(parts[1]) : null;
            return new Selector(contentHash, output);
        }
    }
}
