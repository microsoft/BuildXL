// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
        public IEnumerable<IReadOnlyList<ContentHashWithLastAccessTimeAndReplicaCount>> GetLruPages(Context context, IReadOnlyList<ContentHashWithLastAccessTimeAndReplicaCount> contentHashesWithInfo)
        {
            yield break;
        }
    }
}
