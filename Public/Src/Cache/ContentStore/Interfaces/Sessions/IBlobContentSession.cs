// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Utilities.Collections;

namespace BuildXL.Cache.ContentStore.Interfaces.Sessions
{
    /// <summary>
    /// Interface for a session that interacts with blob storage and can provide URIs for content based on their hashes.
    /// </summary>
    public interface IBlobContentSession
    {
        /// <summary>
        /// Returns a location where the content with the specified hash should be located.
        /// This does not guarantee that the content is actually present at that location.
        /// </summary>
        ValueTask<Result<Uri>> TryGetContentUriAsync(Context context, ContentHash contentHash);
    }

    /// <summary>
    /// Accessor for the global blob cache session.
    /// </summary>
    public static class BlobCacheAccessor
    {
        /// <summary>
        /// Current blob cache session. If there is no cache that is utilizing azure blob storage, the value will be null.
        /// </summary>
        public static AsyncLocal<BoxRef<IBlobContentSession>?>? GlobalBlobCacheSession = new AsyncLocal<BoxRef<IBlobContentSession>?>();

        /// <summary>
        /// Logger used by the cache.
        /// </summary>
        public static AsyncLocal<BoxRef<ILogger>?>? CacheLogger = new AsyncLocal<BoxRef<ILogger>?>();
    }
}
