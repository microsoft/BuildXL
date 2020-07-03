// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
    public interface IChunker : IHashAlgorithmBufferPool, IDisposable
    {
        /// <summary>
        /// Creates a session for chunking a stream from a series of buffers.
        /// </summary>
        IChunkerSession BeginChunking(Action<ChunkInfo> chunkCallback);

        /// <summary>
        /// Configuration that will be used for chunking
        /// </summary>
        ChunkerConfiguration Configuration { get; }
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
}
