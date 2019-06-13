// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace ContentStoreTest.Distributed.Sessions
{
    internal static class TransitioningContentLocationStoreExtensions
    {
        public static Task<GetBulkLocationsResult> GetBulkLocalAsync(
            this TransitioningContentLocationStore store,
            Context context,
            params ContentHash[] contentHashes)
        {
            return store.GetBulkAsync(context, contentHashes, CancellationToken.None, UrgencyHint.Nominal, GetBulkOrigin.Local);
        }
    }
}
