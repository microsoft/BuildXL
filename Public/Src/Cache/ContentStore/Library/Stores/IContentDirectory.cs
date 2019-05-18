// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.UtilitiesCore;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    ///     Callback delegate for analyzing/updating a CacheFileInfo.
    /// </summary>
    /// <param name="info">The pre-existing info. Null if none.</param>
    /// <returns>The new info to be saved. Null if no save necessary.</returns>
    public delegate Task<ContentFileInfo> UpdateFileInfo(ContentFileInfo info);

    /// <summary>
    /// Callbacks for host of <see cref="IContentDirectory"/>.
    /// </summary>
    public interface IContentDirectoryHost
    {
        /// <summary>
        /// Reads the content from content directory.
        /// </summary>
        ContentDirectorySnapshot<ContentFileInfo> Reconstruct(Context context);
    }

    /// <summary>
    ///     Access to CAS metadata.
    /// </summary>
    public interface IContentDirectory : IStartupShutdown
    {
        /// <summary>
        ///     Gets path to the file used to store info.
        /// </summary>
        AbsolutePath FilePath { get; }

        /// <summary>
        /// Returns counters associated with a current instance.
        /// </summary>
        CounterSet GetCounters();

        /// <summary>
        ///     Gets the total size in bytes of the content stored.
        /// </summary>
        /// <returns>Total size in bytes of stored content.</returns>
        /// <remarks>This must iterate over the entire content directory and is correspondingly expensive.</remarks>
        Task<long> GetSizeAsync();

        /// <summary>
        ///     Sums the replica counts of all content.
        /// </summary>
        /// <returns>The total number of replicas stored.</returns>
        /// <remarks>This must iterate over the entire content directory and is correspondingly expensive.</remarks>
        Task<long> GetTotalReplicaCountAsync();

        /// <summary>
        ///     The number of content entries stored.
        /// </summary>
        Task<long> GetCountAsync();

        /// <summary>
        ///     Enumerate the hashes.
        /// </summary>
        /// <returns>List of stored hashes.</returns>
        Task<IEnumerable<ContentHash>> EnumerateContentHashesAsync();

        /// <summary>
        ///     Enumerate the content hashes with file sizes.
        /// </summary>
        /// <returns>An enumeration of the content hashes with file sizes.</returns>
        Task<IReadOnlyList<ContentInfo>> EnumerateContentInfoAsync();

        /// <summary>
        ///     Attempts to delete the content entry associated with the given hash.
        /// </summary>
        /// <param name="contentHash">The hash for which info should be removed.</param>
        /// <returns>The removed info.  Null if none removed.</returns>
        Task<ContentFileInfo> RemoveAsync(ContentHash contentHash);

        /// <summary>
        ///     Accesses the info (if any) corresponding to a hash and calls the given action upon it.
        /// </summary>
        /// <param name="contentHash">The hash to lookup.</param>
        /// <param name="touch">If true, poke the content's LRU.</param>
        /// <param name="clock">Clock to use for the current time.</param>
        /// <param name="updateFileInfo">
        ///     A callback to perform on the info related to the given hash.
        ///     If there exists no info, null will be given.
        ///     If an info is returned and the content is new or different (excluding LRU differences),
        ///     then the ContentDirectory will save it as the hash's new info.
        /// </param>
        Task UpdateAsync(ContentHash contentHash, bool touch, IClock clock, UpdateFileInfo updateFileInfo);

        /// <summary>
        /// Tries to get <see cref="ContentFileInfo"/> information for a given <paramref name="contentHash"/>.
        /// </summary>
        /// <returns>Task with null value if content hash is not registered in content directory.</returns>
        bool TryGetFileInfo(ContentHash contentHash, out ContentFileInfo fileInfo);

        /// <summary>
        ///     Returns the list of content hashes in the content directory in the order by which they should be LRU-ed.
        /// </summary>
        /// <returns>A full list of contents in LRU order.</returns>
        /// <remarks>
        ///     As-is, this defeats the purpose of having an non-in-memory implementations.
        ///     We have a new pattern in mind for this interface, which will go in with the SQLite implementation.
        /// </remarks>
        Task<IReadOnlyList<ContentHash>> GetLruOrderedCacheContentAsync();

        /// <summary>
        ///     Returns the list of content hashes in the content directory in the order by which they should be LRU-ed.
        /// </summary>
        /// <returns>A full list of contents in LRU order with its last access time.</returns>
        Task<IReadOnlyList<ContentHashWithLastAccessTimeAndReplicaCount>> GetLruOrderedCacheContentWithTimeAsync();

        /// <summary>
        ///     Complete all pending/background operations.
        /// </summary>
        Task SyncAsync();

        /// <summary>
        /// Update with LRU from content tracker.
        /// </summary>
        void UpdateContentWithLastAccessTime(ContentHash contentHash, DateTime lastAccess);
    }
}
