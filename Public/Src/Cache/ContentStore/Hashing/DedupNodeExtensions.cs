// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <nodoc />
    public static class DedupNodeExtensions
    {
        /// <nodoc />
        public static ContentHash ToContentHash(this DedupNode node)
        {
            return new ContentHash(HashType.DedupNodeOrChunk, node.GetDedupIdentifier().ToBlobIdentifier().Bytes);
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
    }
}