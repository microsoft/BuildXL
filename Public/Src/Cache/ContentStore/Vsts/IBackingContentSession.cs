// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Vsts
{
    /// <nodoc />
    public interface IReadOnlyBackingContentSession : IReadOnlyContentSession
    {
        /// <summary>
        /// Bulk operations for pins with a specific TTL
        /// </summary>
        Task<IEnumerable<Task<PinResult>>> PinAsync(OperationContext context, IReadOnlyList<ContentHash> contentHashes, DateTime keepUntil);

        /// <summary>
        /// Expiry Cache
        /// </summary>
        BackingContentStoreExpiryCache ExpiryCache { get; }

        /// <summary>
        /// Uri Cache
        /// </summary>
        DownloadUriCache UriCache { get; }
    }

    /// <nodoc />
    public interface IBackingContentSession : IReadOnlyBackingContentSession, IContentSession
    {
    }
}
