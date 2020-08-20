// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// Algorithms for building long streams of chunks into Merkle trees of DedupNodes
    /// </summary>
    public static class DedupNodeTree
    {
        /// <summary>
        /// Creates a tree from the given chunks using the 'MaximallyPacked' algorithm.
        /// </summary>
        /// <returns>
        /// The root of the tree.
        /// </returns>
        public static DedupNode Create(
            IEnumerable<ChunkInfo> chunks)
        {
            DedupNode root;
            root = PackedDedupNodeTree.EnumerateTree(chunks).Last();
            return root;
        }
    }
}
