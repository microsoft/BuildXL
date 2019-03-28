// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    ///     Supported hash types
    /// </summary>
    public enum HashType
    {
        /// <summary>
        ///     Uninitialized
        /// </summary>
        Unknown = 0,

        /// <summary>
        ///     SHA1 hash (20 bytes)
        /// </summary>
        SHA1 = 1,

        /// <summary>
        ///     SHA256 hash (32 bytes)
        /// </summary>
        SHA256 = 2,

        /// <summary>
        ///     MD5 hash (16 bytes)
        /// </summary>
        MD5 = 3,

        /// <summary>
        ///     VSO hash algorithm 0 (33 bytes)
        /// </summary>
        Vso0 = 4,

        /// <summary>
        ///     NTFS Deduplication chunk hash: SHA512 truncated to 256 (32 bytes)
        /// </summary>
        DedupChunk = 5,

        /// <summary>
        ///     VSTS chunk-level deduplication file node (32 bytes)
        /// </summary>
        DedupNode = 6,

        /// <summary>
        ///     DedupChunk or DedupNode with respective algorithm ID appended (33 bytes)
        /// </summary>
        DedupNodeOrChunk = 7,

        /// <summary>
        ///     Legacy VSO hash (33 bytes)
        /// </summary>
        DeprecatedVso0 = 'V' | ('S' << 8) | ('O' << 16) | ('0' << 24)
    }
}
