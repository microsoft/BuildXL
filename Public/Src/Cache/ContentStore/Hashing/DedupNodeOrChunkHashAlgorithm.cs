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
        private readonly IChunker _chunker;
        private readonly DedupChunkHashAlgorithm _chunkHasher = new DedupChunkHashAlgorithm();
        private IChunkerSession? _session;
        private long _sizeHint;
        private bool _chunkingStarted;
        private long _bytesChunked;
        private DedupNode? _lastNode;
        private const HashType NodeOrChunkTargetHashType = HashType.Dedup64K;

        /// <inheritdoc />
        public override int HashSize => 8 * DedupNode64KHashInfo.Length;

        /// <summary>
        /// GetHashType - retrieves the hash type configuration to use.
        /// </summary>
        public virtual HashType DedupHashType => NodeOrChunkTargetHashType;

        /// <nodoc />
        public DedupNodeOrChunkHashAlgorithm() :
            this(Chunker.Create(NodeOrChunkTargetHashType.GetChunkerConfiguration()))
        {
        }

        /// <nodoc />
        public DedupNodeOrChunkHashAlgorithm(IChunker chunker)
        {
            if (!ChunkerConfiguration.IsValidChunkSize(chunker.Configuration)) {throw new NotImplementedException($"Unsupported chunk size specified: {chunker.Configuration.AvgChunkSize} in bytes.");}
            _chunker = chunker;
            Initialize();
        }

        /// <inheritdoc/>
        public void SetInputLength(long expectedSize)
        {
            Contract.Check(!_chunkingStarted)?.Assert($"{nameof(SetInputLength)}: chunking cannot start before input length is set {nameof(_chunkingStarted)}: {_chunkingStarted}");
            Contract.Check(expectedSize >= 0)?.Assert($"{nameof(SetInputLength)}: expected size cannot be negative {nameof(expectedSize)}: {expectedSize}");
            _sizeHint = expectedSize;
        }

        /// <inheritdoc/>
        public Pool<byte[]>.PoolHandle GetBufferFromPool() => _chunker.GetBufferFromPool();

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
                Contract.Check(_chunks.Count == 0)?.Assert($"Chunk count: {_chunks.Count} sizehint: {_sizeHint} chunker min chunk size: {_chunker.Configuration.MinChunkSize}");
                Contract.Check(_bytesChunked == _sizeHint)?.Assert($"_bytesChunked != _sizeHint. _bytesChunked={_bytesChunked} _sizeHint={_sizeHint}");
                Contract.Assert(_session == null, "Dedup session cannot be null.");
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
                    Contract.Check(node.Type == DedupNode.NodeType.ChunkLeaf)?.Assert($"{nameof(CreateNode)}: expected chunk leaf: {DedupNode.NodeType.ChunkLeaf} got {node.Type} instead.");
                    return node;
                }
                else
                {
                    return DedupNodeTree.Create(_chunks);
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
            Contract.Check(chunk.Size != 0)?.Assert($"{nameof(SaveChunks)}: chunk size cannot be zero. Size: {chunk.Size}");
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
                result[bytes.Length] = (byte)ChunkerConfiguration.GetNodeAlgorithmId(_chunker.Configuration);
            }

            return result;
        }
    }
}
