// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Security.Cryptography;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// VSTS chunk-level deduplication file node
    /// </summary>
    public class DedupNodeOrChunkHashAlgorithm : HashAlgorithm, IHashAlgorithmInputLength, IHashAlgorithmBufferPool
    {
        private readonly List<ChunkInfo> _chunks = new List<ChunkInfo>();
        private readonly DedupNodeTree.Algorithm _treeAlgorithm;
        private readonly IChunker _chunker;
        private readonly DedupChunkHashAlgorithm _chunkHasher = new DedupChunkHashAlgorithm();
        private IChunkerSession? _session;
        private long _sizeHint;
        private bool _chunkingStarted;
        private long _bytesChunked;
        private DedupNode? _lastNode;

        /// <inheritdoc />
        public override int HashSize => 8 * DedupNode64KHashInfo.Length;

        /// <summary>
        /// Initializes a new instance of the <see cref="DedupNodeOrChunkHashAlgorithm"/> class.
        /// </summary>
        public DedupNodeOrChunkHashAlgorithm()
            : this(DedupNodeTree.Algorithm.MaximallyPacked, Chunker.Create(ChunkerConfiguration.Default))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DedupNodeOrChunkHashAlgorithm"/> class.
        /// </summary>
        public DedupNodeOrChunkHashAlgorithm(DedupNodeTree.Algorithm treeAlgorithm, IChunker chunker)
        {
            _treeAlgorithm = treeAlgorithm;
            _chunker = chunker;
            Initialize();
        }

        /// <inheritdoc/>
        public void SetInputLength(long expectedSize)
        {
            Contract.Assert(!_chunkingStarted);
            Contract.Assert(expectedSize >= 0);
            _sizeHint = expectedSize;
        }

        /// <inheritdoc/>
        public Pool<byte[]>.PoolHandle GetBufferFromPool()
        {
            return _chunker.GetBufferFromPool();
        }

        /// <summary>
        /// Creates a copy of the chunk list.
        /// </summary>
        // ReSharper disable once PossibleInvalidOperationException
        public DedupNode GetNode()
        {
            Contract.Assert(_lastNode != null, "Can't get a DedupNode because _lastNode is null.");
            return _lastNode.Value;
        }

        /// <inheritdoc />
        public override void Initialize()
        {
            _chunkHasher.Initialize();
            _chunks.Clear();
            _session?.Dispose();
            _session = null;
            _sizeHint = -1;
            _chunkingStarted = false;
            _bytesChunked = 0;
        }

        /// <inheritdoc />
        protected override void HashCore(byte[] array, int ibStart, int cbSize)
        {
            _chunkingStarted = true;

            if (SingleChunkHotPath)
            {
                _chunkHasher.HashCoreInternal(array, ibStart, cbSize);
            }
            else
            {
                if (_session == null)
                {
                    _session = _chunker.BeginChunking(SaveChunks);
                }
                _session.PushBuffer(array, ibStart, cbSize);
            }

            _bytesChunked += cbSize;
        }

        private bool SingleChunkHotPath => _sizeHint >= 0 && _sizeHint <= _chunker.Configuration.MinChunkSize;

        /// <inheritdoc />
        /// <remarks>
        /// Extends DedupNode algorithm to tag hash as DedupChunk or DedupNode.
        /// </remarks>
        protected override byte[] HashFinal()
        {
            _lastNode = CreateNode();
            
            return SerializeHashAndId();
        }

        /// <summary>
        /// Create a node out of the list of chunks.
        /// </summary> 
        protected internal virtual DedupNode CreateNode()
        {
            if (SingleChunkHotPath)
            {
                Contract.Assert(_chunks.Count == 0, $"Chunk count: {_chunks.Count} sizehint: {_sizeHint} chunker min chunk size: {_chunker.Configuration.MinChunkSize}");
                Contract.Check(_bytesChunked == _sizeHint)?.Assert($"_bytesChunked != _sizeHint. _bytesChunked={_bytesChunked} _sizeHint={_sizeHint}");
                Contract.Assert(_session == null);
                byte[] chunkHash = _chunkHasher.HashFinalInternal();
                return new DedupNode(DedupNode.NodeType.ChunkLeaf, (ulong)_sizeHint, chunkHash, 0);
            }
            else
            {
                _session?.Dispose();
                _session = null;

                if (_chunks.Count == 0)
                {
                    return new DedupNode(new ChunkInfo(0, 0, DedupSingleChunkHashInfo.Instance.EmptyHash.ToHashByteArray()));
                }
                else if (_chunks.Count == 1)
                {
                    // Content is small enough to track as a chunk.
                    var node = new DedupNode(_chunks.Single());
                    Contract.Assert(node.Type == DedupNode.NodeType.ChunkLeaf);
                    return node;
                }
                else
                {
                    return DedupNodeTree.Create(_chunks, _treeAlgorithm);
                }
            }
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _chunker.Dispose();
            }

            base.Dispose(disposing);
        }

        private void SaveChunks(ChunkInfo chunk)
        {
            _chunks.Add(chunk);
        }

        /// <summary>
        /// Appends algorithm id to hash result.
        /// </summary>
        private byte[] SerializeHashAndId()
        {
            Contract.Assert(_lastNode != null);

            var bytes = _lastNode.Value.Hash.ToArray();
            byte[] result = new byte[bytes.Length + 1];
            bytes.CopyTo(result, 0);

            if (_lastNode.Value.Type == DedupNode.NodeType.ChunkLeaf)
            {
                result[bytes.Length] = ChunkDedupIdentifier.ChunkAlgorithmId;
            }
            else
            {
                // TODO: Chunk size optimization: This gets replaced with a nicer Chunk config <--> hash type mapper to take care of this in the subsequent PRs.
                if (_chunker.Configuration.AvgChunkSize == 1024 * 1024) // 1MB
                {
                    result[bytes.Length] = (byte)NodeAlgorithmId.Node1024K;
                }
                else if (_chunker.Configuration.AvgChunkSize == 64 * 1024) // 64K (default)
                {
                    result[bytes.Length] = (byte)NodeAlgorithmId.Node64K;
                }
                else
                {
                    throw new NotImplementedException($"Unsupported average chunk size specified (in bytes): {_chunker.Configuration.AvgChunkSize}.");
                }
            }

            return result;
        }
    }
}
