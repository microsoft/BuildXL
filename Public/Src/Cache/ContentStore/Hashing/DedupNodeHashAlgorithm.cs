// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// VSTS chunk-level deduplication file node
    /// </summary>
    public sealed class DedupNodeHashAlgorithm : DedupNodeOrChunkHashAlgorithm
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DedupNodeHashAlgorithm"/> class.
        /// </summary>
        public DedupNodeHashAlgorithm()
            : this(DedupNodeTree.Algorithm.MaximallyPacked, Chunker.Create(ChunkerConfiguration.Default))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DedupNodeHashAlgorithm"/> class.
        /// </summary>
        public DedupNodeHashAlgorithm(ChunkerConfiguration configuration, DedupNodeTree.Algorithm treeAlgorithm)
            : this(treeAlgorithm, Chunker.Create(configuration))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DedupNodeHashAlgorithm"/> class.
        /// </summary>
        public DedupNodeHashAlgorithm(IChunker chunker)
            : this(DedupNodeTree.Algorithm.MaximallyPacked, chunker)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DedupNodeHashAlgorithm"/> class.
        /// </summary>
        public DedupNodeHashAlgorithm(DedupNodeTree.Algorithm treeAlgorithm, IChunker chunker)
            : base(treeAlgorithm, chunker)
        {
        }
      
        /// <inheritdoc />
        protected internal override DedupNode CreateNode()
        {
            var node = base.CreateNode();

            if (node.Type == DedupNode.NodeType.ChunkLeaf)
            {
                node = new DedupNode(new[] { node });
            }

            return node;
        }
    }
}
