// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Hashing;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    ///     Public information about some content.
    /// </summary>
    public readonly struct ContentInfo
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="ContentInfo" /> class.
        /// </summary>
        public ContentInfo(ContentHash contentHash, long size, DateTime lastAccessTimeUtc)
        {
            ContentHash = contentHash;
            Size = size;
            LastAccessTimeUtc = lastAccessTimeUtc;
        }

        /// <summary>
        ///     Gets hash of the content.
        /// </summary>
        public ContentHash ContentHash { get; }

        /// <summary>
        ///     Gets size, in bytes, of the content.
        /// </summary>
        public long Size { get; }

        /// <summary>
        /// Gets the last access time.
        /// </summary>
        public DateTime LastAccessTimeUtc { get; }
    }
}
