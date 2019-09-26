// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    ///     Pairing of content hash and last access time.
    /// </summary>
    public readonly struct ContentHashWithLastAccessTime
    {
        /// <nodoc />
        public ContentHashWithLastAccessTime(ContentHash contentHash, DateTime lastAccessTime)
        {
            ContentHash = contentHash;
            LastAccessTime = lastAccessTime;
        }

        /// <summary>
        ///     Gets the content hash member.
        /// </summary>
        public ContentHash ContentHash { get; }

        /// <summary>
        ///     Gets the last time the content was accessed.
        /// </summary>
        public DateTime LastAccessTime { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"[ContentHash={ContentHash} LastAccessTime={LastAccessTime}]";
        }
    }
}
