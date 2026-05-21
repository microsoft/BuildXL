// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace BuildXL.Cache.ContentStore.Interfaces.Sessions
{
    /// <summary>
    /// Notifies the memoization layer that content has been deleted from the remote cache,
    /// so that associated fingerprint entries can be invalidated.
    /// </summary>
    public interface IContentDeletionNotifier
    {
        /// <summary>
        /// Notifies that the specified content was deleted from the remote cache.
        /// Implementations should invalidate any fingerprint entries that reference this content.
        /// </summary>
        Task NotifyContentDeletedAsync(Context context, ContentHash contentHash);
    }
}
