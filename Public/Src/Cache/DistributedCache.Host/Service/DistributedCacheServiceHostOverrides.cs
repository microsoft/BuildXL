// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.Host.Service.Internal;

namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    /// Allows overriding behavior/settings when constructing a <see cref="DistributedContentStore{T}"/> via <see cref="DistributedContentStoreFactory"/>
    /// </summary>
    public class DistributedCacheServiceHostOverrides
    {
        public static DistributedCacheServiceHostOverrides Default { get; } = new DistributedCacheServiceHostOverrides();

        public virtual IClock Clock { get; } = SystemClock.Instance;

        public virtual void Override(RedisContentLocationStoreConfiguration configuration) { }

        public virtual void Override(DistributedContentStoreSettings settings) { }
    }
}
