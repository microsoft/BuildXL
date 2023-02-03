// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <nodoc />
    public static class DedupNodeExtensions
    {
        private static readonly IContentHasher ChunkHasher = DedupSingleChunkHashInfo.Instance.CreateContentHasher();  // Regardless of underlying chunk size, always hash this way.

        /// <nodoc />
        public static ContentHash ToContentHash(this DedupNode node, HashType hashType)
        {
            byte[] hash;
            switch (hashType)
            {
                case HashType.DedupSingleChunk:
                case HashType.DedupNode:
                    hash = node.Hash;
                    break;
                case HashType.Dedup64K:
                case HashType.Dedup1024K:
                    // TODO: BJB: What is this doing and why?  Why do we need to do this?
                    hash = node.GetDedupIdentifier().ToBlobIdentifier().Bytes;
                    break;
                default:
                    throw new NotImplementedException($"Unexpected HashType '{hashType}' for DedupNode.");
            }

            return new ContentHash(hashType, hash);
        }

        /// <nodoc />
        public static NodeDedupIdentifier CalculateNodeDedupIdentifier(this DedupNode node)
        {
            return  NodeDedupIdentifier.CalculateIdentifierFromSerializedNode(node.Serialize());
        }

        // DEVNOTE: this method is just here for compatibility till we can remove it from ADO.
        /// <nodoc />
        public static DedupIdentifier GetDedupIdentifier(this DedupNode node, HashType hashType)
        {
            return GetDedupIdentifier(node);
        }

        /// <nodoc />
        public static DedupIdentifier GetDedupIdentifier(this DedupNode node)
        {
            if (node.Type == DedupNode.NodeType.InnerNode)
            {
                return node.GetNodeIdentifier();
            }
            else
            {
                return node.GetChunkIdentifier();
            }
        }

        /// <nodoc />
        public static NodeDedupIdentifier GetNodeIdentifier(this DedupNode node)
        {
            if (node.Type != DedupNode.NodeType.InnerNode)
            {
                throw new ArgumentException($"The given hash does not represent a {nameof(NodeDedupIdentifier)}");
            }
            return new NodeDedupIdentifier(node.Hash);
        }

        /// <nodoc />
        public static ChunkDedupIdentifier GetChunkIdentifier(this DedupNode node)
        {
            if (node.Type != DedupNode.NodeType.ChunkLeaf)
            {
                throw new ArgumentException($"The given hash does not represent a {nameof(ChunkDedupIdentifier)}");
            }

            return new ChunkDedupIdentifier(node.Hash);
        }

        /// <nodoc />
        [CLSCompliant(false)]
        public static void AssertFilled(this DedupNode node)
        {
            if (node.Type != DedupNode.NodeType.InnerNode)
            {
                throw new ArgumentException($"Expected a filled {nameof(DedupNode.NodeType.InnerNode)}, but this is a {node.Type}: {node.HashString}");
            }

            if (node.ChildNodes == null || node.ChildNodes.Count == 0)
            {
                throw new ArgumentException($"Expected a filled {nameof(DedupNode.NodeType.InnerNode)}, but ChildNodes is empty for: {node.HashString}");
            }
        }
    }
}
