// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.UtilitiesCore;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    ///     A content hashing object
    /// </summary>
    public interface IContentHasher : IDisposable
    {
        /// <summary>
        ///     Gets hash algorithm information.
        /// </summary>
        HashInfo Info { get; }

        /// <summary>
        ///     Gets a token for an unused hash algorithm object,
        ///     or creates a new HashAlgorithm are available.
        /// </summary>
        HasherToken CreateToken();

        /// <summary>
        ///     Computes a hash from the given stream.
        /// </summary>
        /// <param name="content">Stream of content</param>
        /// <returns>The content hash of the stream</returns>
        Task<ContentHash> GetContentHashAsync(Stream content);

        /// <summary>
        ///     Computes a hash from the given byte array
        /// </summary>
        /// <param name="content">Content as an array of bytes</param>
        /// <returns>The content hash of the byte array.</returns>
        ContentHash GetContentHash(byte[] content);

        /// <summary>
        ///     Computes a hash from the given byte array
        /// </summary>
        /// <param name="content">Content as an array of bytes</param>
        /// <param name="offset">Starting offset into the content array</param>
        /// <param name="count">Number of bytes to hash in the content array</param>
        /// <returns>The content hash of the byte array.</returns>
        ContentHash GetContentHash(byte[] content, int offset, int count);

        /// <summary>
        ///     Construct a stream wrapper that calculates the content hash as the stream is read.
        /// </summary>
        HashingStream CreateReadHashingStream(Stream stream, long parallelHashingFileSizeBoundary = -1);

        /// <summary>
        ///     Construct a stream wrapper that calculates the content hash as the stream is written.
        /// </summary>
        HashingStream CreateWriteHashingStream(Stream stream, long parallelHashingFileSizeBoundary = -1);

        /// <summary>
        ///     Get statistics
        /// </summary>
        CounterSet GetCounters();
    }
}
