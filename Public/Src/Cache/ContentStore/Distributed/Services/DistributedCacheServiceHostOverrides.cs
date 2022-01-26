// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;

namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    /// Allows overriding behavior/settings when constructing a <see cref="DistributedContentStore{T}"/> via <see cref="DistributedContentStoreFactory"/>
    /// </summary>
    public class DistributedCacheServiceHostOverrides
    {
        public static DistributedCacheServiceHostOverrides Default { get; } = new DistributedCacheServiceHostOverrides();

        public virtual IClock Clock { get; } = SystemClock.Instance;

        public virtual IWriteAheadEventStorage Override(IWriteAheadEventStorage storage)
        {
            return storage;
        }

        public virtual IWriteBehindEventStorage Override(IWriteBehindEventStorage storage)
        {
            return storage;
        }

        public virtual void Override(RedisContentLocationStoreConfiguration configuration) { }

        public virtual void Override(DistributedContentStoreSettings settings) { }
    }
}
