// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("BuildXL.Cache.ContentStore.Interfaces.Test, PublicKey=0024000004800000940000000602000000240000525341310004000001000100bdd83cf6a918814f5b0395f20b6aa573b872fcddb8b121f162bdd7d5eb302146b2ea6d7e6551279ff9d62e7bea417acae39badc6e6decfe45ba7b3ad70af432a1aa587343aa67647a4d402a0e2d011a9758aab9f0f8d1c911d554331e8176be34592badc08bc94bbd892af7bcb72ac613f37e4b57a6e18599535211fef8a7eba")]

namespace BuildXL.Cache.ContentStore.Hashing
{
    internal class DeterministicChunker : IChunker
    {
        private readonly IChunker _chunker;

        public DeterministicChunker(IChunker chunker)
        {
            _chunker = chunker;
        }

        public IChunkerSession BeginChunking(Action<ChunkInfo> chunkCallback)
        {
            return new Session(_chunker, chunkCallback);
        }

        public void Dispose()
        {
            _chunker.Dispose();
        }

        private sealed class Session : IChunkerSession
        {
            private static readonly ByteArrayPool PushBufferPool = new ByteArrayPool((int)Chunker.MinPushBufferSize);
            private static readonly Pool<List<ChunkInfo>> ChunksSeenPool = new Pool<List<ChunkInfo>>(() => new List<ChunkInfo>(), list => list.Clear());

            private readonly IChunker _chunker;
            private readonly Action<ChunkInfo> _callback;
            private readonly IPoolHandle<byte[]> _pushBufferHandle;
            private byte[] _pushBuffer;
            private readonly IPoolHandle<List<ChunkInfo>> _chunksSeenHandle;
            private List<ChunkInfo> _chunksSeen;
            private int _bytesInPushBuffer = 0;
            private ulong _lastPushBaseline = 0;

            public Session(IChunker chunker, Action<ChunkInfo> callback)
            {
                _pushBufferHandle = PushBufferPool.Get();
                _pushBuffer = _pushBufferHandle.Value;

                _chunksSeenHandle = ChunksSeenPool.Get();
                _chunksSeen = _chunksSeenHandle.Value;

                _chunker = chunker;
                _callback = callback;
            }

            private void FoundChunk(ChunkInfo chunk)
            {
                _chunksSeen.Add(chunk);
            }

            public void Dispose()
            {
                if (_bytesInPushBuffer != 0)
                {
                    using (var inner = _chunker.BeginChunking(FoundChunk))
                    {
                        inner.PushBuffer(_pushBuffer, 0, _bytesInPushBuffer);
                    }

                    ReportChunks();
                }

                if (_pushBuffer != null)
                {
                    _pushBufferHandle.Dispose();
                    _pushBuffer = null;
                }

                if (_chunksSeen != null)
                {
                    _chunksSeenHandle.Dispose();
                    _chunksSeen = null;
                }
            }

            private void ReportChunks()
            {
                checked
                {
                    foreach (ChunkInfo chunk in _chunksSeen)
                    {
                        var fixedChunk = new ChunkInfo(chunk.Offset + _lastPushBaseline, chunk.Size, chunk.Hash);
                        _callback(fixedChunk);
                    }
                }
            }

            private int PushBufferInner(byte[] buffer, int startOffset, int count)
            {
                checked
                {
                    using (IChunkerSession inner = _chunker.BeginChunking(FoundChunk))
                    {
                        inner.PushBuffer(buffer, startOffset, count);
                    }

                    //don't trust the last one ...
                    _chunksSeen.RemoveAt(_chunksSeen.Count - 1);

                    ReportChunks();

                    ChunkInfo secondToLastChunk = _chunksSeen.Last();
                    int bytesChunked = (int)(secondToLastChunk.Offset + secondToLastChunk.Size);

                    _chunksSeen.Clear();
                    _lastPushBaseline += (uint)bytesChunked;

                    return bytesChunked;
                }
            }

            public void PushBuffer(byte[] buffer, int startOffset, int count)
            {
                checked
                {
                    while (count > 0)
                    {
                        int bytesConsumed;

                        // if we already have a contiguous array
                        if (_bytesInPushBuffer == 0 && count >= _pushBuffer.Length)
                        {
                            bytesConsumed = PushBufferInner(buffer, startOffset, _pushBuffer.Length);
                        }
                        else
                        {
                            Contract.Assert(_pushBuffer.Length > _bytesInPushBuffer);
                            int roomInPushBuffer = _pushBuffer.Length - _bytesInPushBuffer;
                            bytesConsumed = Math.Min(roomInPushBuffer, count);
                            Buffer.BlockCopy(buffer, startOffset, _pushBuffer, _bytesInPushBuffer, bytesConsumed);
                            _bytesInPushBuffer += bytesConsumed;
                            if (_bytesInPushBuffer == _pushBuffer.Length)
                            {
                                int bytesChunked = PushBufferInner(_pushBuffer, 0, _pushBuffer.Length);
                                int bytesToReChunk = _pushBuffer.Length - bytesChunked;
                                Contract.Assert(bytesToReChunk >= 0);
                                Buffer.BlockCopy(_pushBuffer, (int)bytesChunked, _pushBuffer, 0, (int)bytesToReChunk);
                                _bytesInPushBuffer = bytesToReChunk;
                            }
                        }

                        Contract.Assert(bytesConsumed > 0);

                        count -= bytesConsumed;
                        startOffset += bytesConsumed;
                    }
                }
            }
        }
    }
}
