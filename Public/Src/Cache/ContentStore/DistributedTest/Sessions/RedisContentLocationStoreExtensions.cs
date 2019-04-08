// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace ContentStoreTest.Distributed.Sessions
{
    internal static class RedisContentLocationStoreExtensions
    {
        public static Task<GetBulkLocationsResult> GetBulkAsync(
            this IContentLocationStore store,
            Context context,
            ContentHash contentHash,
            GetBulkOrigin origin)
        {
            return store.GetBulkAsync(context, new ContentHash[] {contentHash}, CancellationToken.None, UrgencyHint.Nominal, origin);
        }
    }
}
