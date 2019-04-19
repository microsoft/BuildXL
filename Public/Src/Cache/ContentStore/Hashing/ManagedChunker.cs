// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
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
        private readonly SHA512Managed _shaHasher = new SHA512Managed();

        /// <summary>
        /// To get deterministic chunks out of the chunker, only give it buffers of at least 256KB, unless EOF.
        /// Cosmin Rusu recommends larger buffers for performance, so going with 1MB.
        /// </summary>
        public const uint MinPushBufferSize = 1024 * 1024;

        /// <summary>
        /// Gets total number of bytes chunked.
        /// </summary>
        public long TotalBytes { get; private set; }

        /// <summary>
        /// Creates a session for chunking a stream from a series of buffers.
        /// </summary>
        public IChunkerSession BeginChunking(Action<ChunkInfo> chunkCallback)
        {
            TotalBytes = 0;

            return new Session(this, chunkInfo =>
            {
                chunkCallback(chunkInfo);
                TotalBytes += chunkInfo.Size;
            });
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
            private static readonly ByteArrayPool pool = new ByteArrayPool((int)MinPushBufferSize);
            private readonly ManagedChunker _parent;
            private readonly RegressionChunking _regressionChunker;
            private readonly Action<ChunkInfo> _chunkCallback;
            private Pool<byte[]>.PoolHandle? _lastPushBuffer = null;
            private Pool<byte[]>.PoolHandle _pushBuffer = pool.Get();
            private int _bytesInPushBuffer = 0;
            private ulong _lastBufferFileOffset = 0;
            private ulong _bufferFileOffset = 0;
            private bool anyChunksFound = false;

            /// <summary>
            /// Initializes a new instance of the <see cref="Session"/> class.
            /// </summary>
            public Session(ManagedChunker chunker, Action<ChunkInfo> chunkCallback)
            {
                _parent = chunker;
                _regressionChunker = new RegressionChunking(ChunkTranslate);
                _chunkCallback = chunkCallback;
            }

            private void ChunkTranslate(DedupBasicChunkInfo chunk)
            {
                if (chunk.m_nStartChunk == 0 && chunk.m_nChunkLength == 0)
                {
                    return;
                }

                byte[] hash;
                if (chunk.m_nStartChunk < _bufferFileOffset)
                {
                    ulong chunkEnd = chunk.m_nStartChunk + chunk.m_nChunkLength;

                    if (chunkEnd < _bufferFileOffset)
                    {
                        hash = _parent._shaHasher.ComputeHash(
                            _lastPushBuffer.Value.Value,
                            (int)(chunk.m_nStartChunk - _lastBufferFileOffset),
                            (int)chunk.m_nChunkLength);
                    }
                    else
                    {
                        _parent._shaHasher.TransformBlock(
                            _lastPushBuffer.Value.Value,
                            (int)(chunk.m_nStartChunk - _lastBufferFileOffset),
                            (int)(_bufferFileOffset - chunk.m_nStartChunk),
                            null,
                            0);

                        int remainingChunkBytes = (int)(chunkEnd - _bufferFileOffset);
                        _parent._shaHasher.TransformFinalBlock(
                            _pushBuffer.Value,
                            0,
                            remainingChunkBytes);
                        hash = _parent._shaHasher.Hash;
                        _parent._shaHasher.Initialize();
                    }
                }
                else
                {
                    int startOffset = (int)(chunk.m_nStartChunk - _bufferFileOffset);
                    hash = _parent._shaHasher.ComputeHash(_pushBuffer.Value, startOffset, (int)chunk.m_nChunkLength);
                }

                if (anyChunksFound && chunk.m_nChunkLength == 0)
                {
                    return;
                }

                anyChunksFound = true;
                _chunkCallback(new ChunkInfo(chunk.m_nStartChunk, (uint)chunk.m_nChunkLength, hash.Take(32).ToArray()));
            }

            /// <summary>
            /// Chunks the buffer, calling back when chunks complete.
            /// </summary>
            public void PushBuffer(byte[] buffer, int startOffset, int count)
            {
                checked
                {
                    while (count > 0)
                    {
                        while (count > 0 && _bytesInPushBuffer < MinPushBufferSize)
                        {
                            _pushBuffer.Value[_bytesInPushBuffer] = buffer[startOffset];
                            startOffset++;
                            _bytesInPushBuffer++;
                            count--;
                        }

                        if (_bytesInPushBuffer == MinPushBufferSize)
                        {
                            _regressionChunker.PushBuffer(new ArraySegment<byte>(_pushBuffer.Value));

                            _lastBufferFileOffset = _bufferFileOffset;
                            _lastPushBuffer?.Dispose();
                            _lastPushBuffer = _pushBuffer;

                            _bufferFileOffset += MinPushBufferSize;
                            _pushBuffer = pool.Get();
                            _bytesInPushBuffer = 0;
                        }
                    }
                }
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                if (_bytesInPushBuffer > 0)
                {
                    _regressionChunker.PushBuffer(new ArraySegment<byte>(_pushBuffer.Value, 0, _bytesInPushBuffer));
                }


                _regressionChunker.Complete();

                _lastPushBuffer?.Dispose();
                _pushBuffer.Dispose();
            }

            /// <inheritdoc/>
            public override bool Equals(object obj)
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
