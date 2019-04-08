// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Distributed.Redis;

namespace ContentStoreTest.Distributed.ContentLocation
{
    /// <summary>
    /// Equality comparer for <see cref="RedisCheckpointInfo"/> type.
    /// </summary>
    internal class RedisCheckpointInfoEqualityComparer : IEqualityComparer<RedisCheckpointInfo>
    {
        public bool Equals(RedisCheckpointInfo x, RedisCheckpointInfo y)
        {
            return x.CheckpointId == y.CheckpointId &&
                   x.SequenceNumber == y.SequenceNumber &&
                   x.CheckpointCreationTime == y.CheckpointCreationTime &&
                   x.MachineName == y.MachineName;
        }

        /// <inheritdoc />
        public int GetHashCode(RedisCheckpointInfo info) => 0; // The comparer is used in tests and not intented for using with hash maps.
    }
}
