// Copyright (c) Microsoft Corporation. All rights reserved.

using System;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// Chunk deduplication based on the Windows Server volume-level chunking algorithm.
    /// </summary>
    /// <remarks>
    /// Windows Server Deduplication: https://technet.microsoft.com/en-us/library/hh831602(v=ws.11).aspx
    /// More documentation: https://mseng.visualstudio.com/DefaultCollection/VSOnline/Artifact%20Services/_git/Content.VS?path=%2Fvscom%2Fintegrate%2Fapi%2Fdedup%2Fnode.md&amp;version=GBteams%2Fartifact%2Fversion2&amp;_a=contents
    /// </remarks>
    public interface IChunker : IDisposable
    {
        /// <summary>
        /// Gets total number of bytes chunked.
        /// </summary>
        long TotalBytes { get; }

        /// <summary>
        /// Creates a session for chunking a stream from a series of buffers.
        /// </summary>
        IChunkerSession BeginChunking(Action<ChunkInfo> chunkCallback);
    }

    /// <summary>
    /// A session for chunking a stream from a series of buffers
    /// </summary>
    public interface IChunkerSession : IDisposable
    {
        /// <summary>
        /// Chunks the buffer, calling back when chunks complete.
        /// </summary>
        void PushBuffer(byte[] buffer, int startOffset, int count);
    }

    /// <summary>
    /// Chunk deduplication based on the Windows Server volume-level chunking algorithm.
    /// </summary>
    /// <remarks>
    /// Windows Server Deduplication: https://technet.microsoft.com/en-us/library/hh831602(v=ws.11).aspx
    /// More documentation: https://mseng.visualstudio.com/DefaultCollection/VSOnline/Artifact%20Services/_git/Content.VS?path=%2Fvscom%2Fintegrate%2Fapi%2Fdedup%2Fnode.md&amp;version=GBteams%2Fartifact%2Fversion2&amp;_a=contents
    /// </remarks>
    public static class Chunker
    {
        /// <summary>
        /// To get deterministic chunks out of the chunker, only give it buffers of at least 256KB, unless EOF.
        /// Cosmin Rusu recommends larger buffers for performance, so going with 1MB.
        /// </summary>
        public const uint MinPushBufferSize = 1024 * 1024;

        /// <summary>
        /// Chunks the buffer, calling back when chunks complete.
        /// </summary>
        public static void PushBuffer<T>(this T session, ArraySegment<byte> bytes)
            where T : IChunkerSession
        {
            session.PushBuffer(bytes.Array, bytes.Offset, bytes.Count);
        }
    }
}
