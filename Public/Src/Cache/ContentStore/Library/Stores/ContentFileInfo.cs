// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Time;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    ///     Metadata for a piece of content in the CAS.
    /// </summary>
    public class ContentFileInfo : IEquatable<ContentFileInfo>
    {
        /// <summary>
        ///     Gets its size in bytes.
        /// </summary>
        public long FileSize { get; }

        /// <summary>
        ///     Gets last time it was accessed.
        /// </summary>
        public long LastAccessedFileTimeUtc { get; private set; }

        /// <summary>
        ///     Gets or sets number of known replicas.
        /// </summary>
        public int ReplicaCount { get; set; }

        /// <summary>
        /// Returns a total size of the content on disk.
        /// </summary>
        public long TotalSize => FileSize * ReplicaCount;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ContentFileInfo" /> class.
        /// </summary>
        public ContentFileInfo(long fileSize, long lastAccessedFileTimeUtc, int replicaCount)
        {
            FileSize = fileSize;
            LastAccessedFileTimeUtc = lastAccessedFileTimeUtc;
            ReplicaCount = replicaCount;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ContentFileInfo" /> class for newly inserted content
        /// </summary>
        /// <param name="clock">Clock to use for the current time.</param>
        /// <param name="fileSize">Size of the content.</param>
        /// <param name="replicaCount">Number of replicas.</param>
        public ContentFileInfo(IClock clock, long fileSize, int replicaCount = 1)
        {
            FileSize = fileSize;
            UpdateLastAccessed(clock);
            ReplicaCount = replicaCount;
        }

        /// <inheritdoc />
        public bool Equals(ContentFileInfo other)
        {
            return other != null &&
                FileSize == other.FileSize &&
                ReplicaCount == other.ReplicaCount &&
                LastAccessedFileTimeUtc == other.LastAccessedFileTimeUtc;
        }

        /// <summary>
        ///     Updates the last accessed time to now and increments the access count.
        /// </summary>
        /// <param name="clock">Clock to use for the current time.</param>
        public void UpdateLastAccessed(IClock clock)
        {
            LastAccessedFileTimeUtc = clock.UtcNow.ToFileTimeUtc();
        }

        /// <summary>
        ///     Updates the last accessed time to provided time.
        /// </summary>
        public void UpdateLastAccessed(DateTime dateTime)
        {
            var updatedFileTimeUtc = dateTime.ToFileTimeUtc();

            // Don't update LastAccessFileTimeUtc if dateTime is outdated
            if (updatedFileTimeUtc > LastAccessedFileTimeUtc)
            {
                LastAccessedFileTimeUtc = updatedFileTimeUtc;
            }
        }
    }
}
