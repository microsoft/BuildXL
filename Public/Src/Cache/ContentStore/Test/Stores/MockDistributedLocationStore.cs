// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;

namespace ContentStoreTest.Stores
{
    public sealed class MockDistributedLocationStore : IDistributedLocationStore
    {
        public IReadOnlyList<ContentHash> UnregisteredHashes;

        /// <inheritdoc />
        public bool CanComputeLru => false;

        /// <inheritdoc />
        public Task<BoolResult> UnregisterAsync(Context context, IReadOnlyList<ContentHash> contentHashes, CancellationToken token)
        {
            UnregisteredHashes = contentHashes;
            return BoolResult.SuccessTask;
        }

        /// <inheritdoc />
        public IEnumerable<ContentEvictionInfo> GetHashesInEvictionOrder(Context context, IReadOnlyList<ContentHashWithLastAccessTimeAndReplicaCount> contentHashesWithInfo)
        {
            yield break;
        }
    }
}
