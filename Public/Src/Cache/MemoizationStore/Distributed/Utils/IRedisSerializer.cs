// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
#if MICROSOFT_INTERNAL
using Microsoft.Caching.Redis;
#else
using StackExchange.Redis;
#endif

namespace BuildXL.Cache.MemoizationStore.Distributed.Utils
{
    /// <summary>
    /// Interface for serializing and deserializing types to store in Redis
    /// </summary>
    public interface IRedisSerializer
    {
        /// <summary>
        /// Converts <see cref="Fingerprint"/> to <see cref="RedisKey"/>
        /// </summary>
        RedisKey ToRedisKey(Fingerprint fingerprint);

        /// <summary>
        /// Converts <see cref="StrongFingerprint"/> to <see cref="RedisKey"/>
        /// </summary>
        RedisKey ToRedisKey(StrongFingerprint strongFingerprint);

        /// <summary>
        /// Converts <see cref="IList{Selector}"/> to <see cref="RedisValue"/> array
        /// </summary>
        RedisValue[] ToRedisValues(IList<Selector> selectors);

        /// <summary>
        /// Converts <see cref="ContentHashListWithDeterminism"/> to <see cref="RedisValue"/>
        /// </summary>
        RedisValue ToRedisValue(ContentHashListWithDeterminism hashList);

        /// <summary>
        /// Converts array of <see cref="RedisValue"/> to <see cref="IEnumerable{Selector}"/>
        /// </summary>
        IEnumerable<Selector> AsSelectors(RedisValue[] payload);

        /// <summary>
        /// Converts <see cref="RedisValue"/> to <see cref="ContentHashListWithDeterminism"/>
        /// </summary>
        ContentHashListWithDeterminism AsContentHashList(RedisValue value);
    }
}
