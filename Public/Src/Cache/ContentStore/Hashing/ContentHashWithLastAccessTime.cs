// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using StructGenerators;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    ///     Pairing of content hash and last access time.
    /// </summary>
    [StructGenerators.StructRecord]
    public readonly partial struct ContentHashWithLastAccessTime
    {
        /// <nodoc />
        public ContentHashWithLastAccessTime(ContentHash contentHash, DateTime lastAccessTime)
        {
            Hash = contentHash;
            LastAccessTime = lastAccessTime;
        }

        /// <summary>
        ///     Gets the content hash member.
        /// </summary>
        public ContentHash Hash { get; }

        /// <summary>
        ///     Gets the last time the content was accessed.
        /// </summary>
        public DateTime LastAccessTime { get; }

    }
}
