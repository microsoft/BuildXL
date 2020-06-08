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
            _session = InitializeSession();
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
            _chunks.Clear();
            _session = _chunker.BeginChunking(SaveChunks);
        }

        private IChunkerSession InitializeSession()
        {
            Initialize();
            return _session;
        }

        /// <inheritdoc />
        protected override void HashCore(byte[] array, int ibStart, int cbSize)
        {
            _lastNode = null;
            _session.PushBuffer(array, ibStart, cbSize);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Extends DedupNode algorithm to tag hash as DedupChunk or DedupNode.
        /// </remarks>
        protected override byte[] HashFinal()
        {
            _session.Dispose();

            if (_chunks.Count == 0)
            {
                _chunks.Add(new ChunkInfo(0, 0, DedupChunkHashInfo.Instance.EmptyHash.ToHashByteArray()));
            }

            if (_chunks.Count == 1)
            {
                // Content is small enough to track as a chunk.
                _lastNode = new DedupNode(_chunks.Single());
                Contract.Assert(_lastNode.Value.Type == DedupNode.NodeType.ChunkLeaf);
            }
            else
            {
                _lastNode = DedupNodeTree.Create(_chunks, _treeAlgorithm);
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
                result[bytes.Length] = NodeDedupIdentifier.NodeAlgorithmId;
            }

            return result;
        }
    }
}
