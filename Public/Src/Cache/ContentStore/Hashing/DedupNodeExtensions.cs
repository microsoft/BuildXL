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
        public static ContentHash ToContentHash(this DedupNode node)
        {
            var nodeDedupIdentifier = node.GetDedupIdentifier();
            switch (nodeDedupIdentifier.AlgorithmId)
            {
                case (byte)NodeAlgorithmId.Node64K:
                    return new ContentHash(HashType.DedupNodeOrChunk, nodeDedupIdentifier.ToBlobIdentifier().Bytes);
                case (byte)NodeAlgorithmId.Node1024K:
                    return new ContentHash(HashType.Dedup1024K, nodeDedupIdentifier.ToBlobIdentifier().Bytes);
                default:
                    throw new InvalidEnumArgumentException($"Unknown algorithm id detected for blob {nodeDedupIdentifier.ToBlobIdentifier()} : {nodeDedupIdentifier.AlgorithmId}");
            }
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
            // TODO: Chunk size optimization - the hash-algo mapper will take care of this.
            // for now use default.
            return new NodeDedupIdentifier(node.Hash, (byte)NodeAlgorithmId.Node64K);
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
