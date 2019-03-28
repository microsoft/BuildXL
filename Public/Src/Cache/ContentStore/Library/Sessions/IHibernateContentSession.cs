// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace BuildXL.Cache.ContentStore.Sessions
{
    /// <summary>
    ///     Extended features for sessions that can hibernate/restore.
    /// </summary>
    public interface IHibernateContentSession
    {
        /// <summary>
        ///     Retrieve collection of content hashes currently pinned in the session.
        /// </summary>
        /// <returns>
        ///     Collection of content hashes for content that is currently pinned.
        /// </returns>
        IEnumerable<ContentHash> EnumeratePinnedContentHashes();

        /// <summary>
        ///     Restore pinning of a collection of content hashes in the current session.
        /// </summary>
        /// <param name="context">
        ///     Tracing context.
        /// </param>
        /// <param name="contentHashes">
        ///     Collection of content hashes to be pinned.
        /// </param>
        Task PinBulkAsync(Context context, IEnumerable<ContentHash> contentHashes);
    }
}
