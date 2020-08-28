// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Security.Cryptography;
using BuildXL.Cache.ContentStore.Hashing.Chunking;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// Chunk deduplication based on the Windows Server volume-level chunking algorithm.
    /// </summary>
    /// <remarks>
    /// Windows Server Deduplication: https://technet.microsoft.com/en-us/library/hh831602(v=ws.11).aspx
    /// More documentation: https://mseng.visualstudio.com/DefaultCollection/VSOnline/Artifact%20Services/_git/Content.VS?path=%2Fvscom%2Fintegrate%2Fapi%2Fdedup%2Fnode.md&amp;version=GBteams%2Fartifact%2Fversion2&amp;_a=contents
    /// </remarks>
    public sealed class ManagedChunker : IChunker
    {
        private readonly DeterministicChunker _inner;

        /// <inheritdoc/>
        public ChunkerConfiguration Configuration => _inner.Configuration;

        /// <summary>
        /// 
        /// </summary>
        public ManagedChunker(ChunkerConfiguration configuration)
        {
            _inner = new DeterministicChunker(configuration, new ManagedChunkerNonDeterministic(configuration));
        }

        /// <inheritdoc/>
        public IChunkerSession BeginChunking(Action<ChunkInfo> chunkCallback)
        {
            return _inner.BeginChunking(chunkCallback);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _inner.Dispose();
        }

        /// <inheritdoc/>
        public Pool<byte[]>.PoolHandle GetBufferFromPool()
        {
            return _inner.GetBufferFromPool();
        }
    }

    internal sealed class ManagedChunkerNonDeterministic : INonDeterministicChunker
    {
        public ChunkerConfiguration Configuration { get; }
        private readonly SHA512 _shaHasher = new SHA512CryptoServiceProvider();

        public ManagedChunkerNonDeterministic(ChunkerConfiguration configuration)
        {
            Configuration = configuration;
        }

        /// <summary>
        /// Creates a session for chunking a stream from a series of buffers.
        /// </summary>
        public IChunkerSession BeginChunking(Action<ChunkInfo> chunkCallback)
        {
            return new Session(this, chunkCallback);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _shaHasher.Dispose();
        }

        /// <summary>
        /// A session for chunking a stream from a series of buffers
        /// </summary>
        private sealed class Session : IChunkerSession
        {
            private readonly ManagedChunkerNonDeterministic _parent;
            private readonly RegressionChunking _regressionChunker;
            private readonly Action<ChunkInfo> _chunkCallback;
            private ArraySegment<byte>? _currentBuffer = null;

            /// <summary>
            /// Initializes a new instance of the <see cref="Session"/> class.
            /// </summary>
            public Session(ManagedChunkerNonDeterministic chunker, Action<ChunkInfo> chunkCallback)
            {
                _parent = chunker;
                _regressionChunker = new RegressionChunking(_parent.Configuration, ChunkTranslate);
                _chunkCallback = chunkCallback;
            }

            private void ChunkTranslate(DedupBasicChunkInfo chunk)
            {
                if (chunk.m_nStartChunk == 0 && chunk.m_nChunkLength == 0)
                {
                    return;
                }

                Contract.Assert(_currentBuffer != null);
                Contract.Assert(_currentBuffer.Value.Array != null);

               byte[] hash = _parent._shaHasher.ComputeHash(
                    _currentBuffer.Value.Array,
                    _currentBuffer.Value.Offset + (int)chunk.m_nStartChunk,
                    (int)chunk.m_nChunkLength);

                _chunkCallback(new ChunkInfo(chunk.m_nStartChunk, (uint)chunk.m_nChunkLength, hash.Take(32).ToArray()));
            }

            /// <summary>
            /// Chunks the buffer, calling back when chunks complete.
            /// </summary>
            public void PushBuffer(byte[] buffer, int startOffset, int count)
            {
                _currentBuffer = new ArraySegment<byte>(buffer, startOffset, count);
                _regressionChunker.PushBuffer(_currentBuffer.Value);
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                _regressionChunker.Complete();
            }

            /// <inheritdoc/>
            public override bool Equals(object? obj)
            {
                throw new InvalidOperationException();
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                throw new InvalidOperationException();
            }

            /// <nodoc />
            [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "right")]
            [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "left")]
            [SuppressMessage("ReSharper", "UnusedParameter.Global")]
            public static bool operator ==(Session left, Session right)
            {
                throw new InvalidOperationException();
            }

            /// <nodoc />
            [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "right")]
            [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "left")]
            [SuppressMessage("ReSharper", "UnusedParameter.Global")]
            public static bool operator !=(Session left, Session right)
            {
                throw new InvalidOperationException();
            }
        }
    }
}
