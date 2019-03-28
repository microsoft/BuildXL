// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// VSTS chunk-level deduplication file node
    /// </summary>
    public class DedupNodeOrChunkHashAlgorithm : HashAlgorithm
    {
        private readonly List<ChunkInfo> _chunks = new List<ChunkInfo>();
        private readonly DedupNodeTree.Algorithm _treeAlgorithm;
        private readonly IChunker _chunker;
        private IChunkerSession _session;
        private DedupNode? _lastNode;

        /// <inheritdoc />
        public override int HashSize => 8 * DedupNodeOrChunkHashInfo.Length;

        /// <summary>
        /// Initializes a new instance of the <see cref="DedupNodeOrChunkHashAlgorithm"/> class.
        /// </summary>
        public DedupNodeOrChunkHashAlgorithm()
            : this(DedupNodeTree.Algorithm.MaximallyPacked)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DedupNodeOrChunkHashAlgorithm"/> class.
        /// </summary>
        public DedupNodeOrChunkHashAlgorithm(DedupNodeTree.Algorithm treeAlgorithm)
        {
            _treeAlgorithm = treeAlgorithm;
            _chunker = DedupNodeHashAlgorithm.CreateChunker();
            Initialize();
        }

        /// <summary>
        /// Creates a copy of the chunk list.
        /// </summary>
        // ReSharper disable once PossibleInvalidOperationException
        public DedupNode GetNode() => _lastNode.Value;

        /// <inheritdoc />
        public override void Initialize()
        {
            _chunks.Clear();
            _session = _chunker.BeginChunking(SaveChunks);
        }

        /// <inheritdoc />
        protected override void HashCore(byte[] array, int ibStart, int cbSize)
        {
            if (_chunker.TotalBytes == 0)
            {
                _lastNode = null;
            }

            _session.PushBuffer(array, ibStart, cbSize);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Extends DedupNode algorithm to tag hash as DedupChunk or DedupNode.
        /// </remarks>
        protected override byte[] HashFinal()
        {
            _session.Dispose();

            if (_chunker.TotalBytes == 0)
            {
                _chunks.Add(new ChunkInfo(0, 0, DedupChunkHashInfo.Instance.EmptyHash.ToHashByteArray()));
            }

            _lastNode = DedupNodeTree.Create(_chunks, _treeAlgorithm);

            if (_lastNode.Value.ChildNodes.Count == 1)
            {
                // Content is small enough to track as a chunk.
                _lastNode = _lastNode.Value.ChildNodes.Single();
            }

            return SerializeHashAndId();
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
            var bytes = _lastNode.Value.Hash.ToArray();
            byte[] result = new byte[bytes.Length + 1];
            bytes.CopyTo(result, 0);

            if (_lastNode.Value.Type == DedupNode.NodeType.ChunkLeaf)
            {
                result[bytes.Length] = ChunkDedupIdentifier.ChunkAlgorithmId;
            }
            else
            {
                result[bytes.Length] = NodeDedupIdentifier.NodeAlgorithmId;
            }

            return result;
        }
    }
}
