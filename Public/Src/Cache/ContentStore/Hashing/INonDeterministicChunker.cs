// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Cache.ContentStore.Hashing
{
    internal interface INonDeterministicChunker : IDisposable
    {
        /// <summary>
        /// Creates a session for chunking a stream from a series of buffers.
        /// </summary>
        IChunkerSession BeginChunking(Action<ChunkInfo> chunkCallback);
    }
}
