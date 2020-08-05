// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.ComponentModel;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <nodoc />
    public static class DedupNodeExtensions
    {
        /// <nodoc />
        public static ContentHash ToContentHash(this DedupNode node, HashType hashType)
        {
            byte[] hash;
            switch (hashType)
            {
                case HashType.DedupChunk:
                case HashType.DedupNode:
                    hash = node.Hash;
                    break;
                case HashType.DedupNodeOrChunk:
                case HashType.Dedup1024K:
                    hash = node.GetDedupIdentifier(hashType).ToBlobIdentifier().Bytes;
                    break;
                default:
                    throw new ArgumentException($"Unexpected HashType '{hashType}' for DedupNode.");
            }

            return new ContentHash(hashType, hash);
        }

        /// <nodoc />
        public static DedupIdentifier GetDedupIdentifier(this DedupNode node, HashType hashType)
        {
            if (node.Type == DedupNode.NodeType.InnerNode)
            {
                return node.GetNodeIdentifier(hashType);
            }
            else
            {
                return node.GetChunkIdentifier();
            }
        }

        /// <nodoc />
        public static NodeDedupIdentifier GetNodeIdentifier(this DedupNode node, HashType hashType)
        {
            if (node.Type != DedupNode.NodeType.InnerNode)
            {
                throw new ArgumentException($"The given hash does not represent a {nameof(NodeDedupIdentifier)}");
            }
            // TODO: Chunk size optimization - the hash-algo mapper will take care of this.
            // for now use default.
            return new NodeDedupIdentifier(node.Hash, (NodeAlgorithmId)AlgorithmIdLookup.Find(hashType));
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
    }
}
