// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    ///     Pairing of content hash, size, and last access time.
    /// </summary>
    public readonly struct ContentHashWithSizeAndLastAccessTime
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="ContentHashWithSize"/> struct.
        /// </summary>
        public ContentHashWithSizeAndLastAccessTime(ContentHash contentHash, long size, DateTime lastAccessTime)
        {
            Hash = contentHash;
            Size = size;
            LastAccessTime = lastAccessTime;
        }

        /// <summary>
        ///     Gets the content hash member.
        /// </summary>
        public ContentHash Hash { get; }

        /// <summary>
        ///     Gets the content size member.
        /// </summary>
        public long Size { get; }

        /// <summary>
        ///     Gets the last time the content was accessed.
        /// </summary>
        public DateTime LastAccessTime { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"[ContentHash={Hash} Size={Size} LastAccessTime={LastAccessTime}]";
        }
    }
}
