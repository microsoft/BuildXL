// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.UtilitiesCore;

#pragma warning disable CS3001 // CLS
#pragma warning disable CS3002
#pragma warning disable CS3003

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// Certain hashers can make significant optimizations if they know the total input size up-front.
    /// </summary>
    public interface IHashAlgorithmInputLength
    {
        /// <summary>
        /// Provides the hasher with the total length of the input.
        /// </summary>
        void SetInputLength(long inputLength);
    }

    /// <summary>
    /// Certain hashers can save on lots of copies by providing a buffer of a particular size.
    /// </summary>
    public interface IHashAlgorithmBufferPool
    {
        /// <summary>
        /// The buffer to fill as much as possible before calling TransformBlock or ComputeHash on it.
        /// </summary>
        Pool<byte[]>.PoolHandle GetBufferFromPool();
    }

    /// <summary>
    /// All <see cref="HashAlgorithm"/> have <see cref="HashAlgorithm.Initialize"/> method, 
    /// but some hashers may decide to do something not only before computing the hash, but also after doing so.
    /// For instance, if the hasher uses an object pool the object will be obtained in 'Initialize' and
    /// released to pool in 'Cleanup' method.
    /// </summary>
    public interface IHashAlgorithmWithCleanup
    {
        /// <summary>
        /// Cleans up a transient state.
        /// </summary>
        void Cleanup();
    }

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
        Task<ContentHash> GetContentHashAsync(StreamWithLength content);

        /// <summary>
        ///     Computes a hash from the given byte array
        /// </summary>
        /// <param name="content">Content as an array of bytes</param>
        /// <returns>The content hash of the byte array.</returns>
        ContentHash GetContentHash(byte[] content);

        /// <summary>
        ///     Computes a hash from the given byte readonly span of bytes.
        /// </summary>
        /// <remarks>
        /// The underlying API is only available in .net core, that's why this API is also available only for .net core.
        /// </remarks>
        ContentHash GetContentHash(ReadOnlySpan<byte> content);

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
        HashingStream CreateReadHashingStream(long streamLength, Stream stream, long parallelHashingFileSizeBoundary = -1);

        /// <summary>
        ///     Construct a stream wrapper that calculates the content hash as the stream is read.
        /// </summary>
        HashingStream CreateReadHashingStream(StreamWithLength stream, long parallelHashingFileSizeBoundary = -1);

        /// <summary>
        ///     Construct a stream wrapper that calculates the content hash as the stream is written.
        /// </summary>
        HashingStream CreateWriteHashingStream(long streamLength, Stream stream, long parallelHashingFileSizeBoundary = -1);

        /// <summary>
        ///     Construct a stream wrapper that calculates the content hash as the stream is written.
        /// </summary>
        HashingStream CreateWriteHashingStream(StreamWithLength stream, long parallelHashingFileSizeBoundary = -1);

        /// <summary>
        ///     Get statistics
        /// </summary>
        CounterSet GetCounters();
    }

    /// <summary>
    /// A DedupContent hashing object.
    /// </summary>
    public interface IDedupContentHasher: IContentHasher
    {
        /// <summary>
        /// Computes a Hash from the given stream and returns a DedupNode.
        /// </summary>
        /// <param name="content"></param>
        /// <returns></returns>
        Task<DedupNode> HashContentAndGetDedupNodeAsync(StreamWithLength content);
    }
}
