// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// Algorithms for building long streams of chunks into Merkle trees of DedupNodes
    /// </summary>
    public static class PackedDedupNodeTree
    {
        /// <summary>
        /// Non-blocking enumerable of the whole tree given an emuerable of chunk leaves.
        /// </summary>
        public static IEnumerable<DedupNode> EnumerateTree(IEnumerable<ChunkInfo> chunks)
        {
            if (chunks is IReadOnlyCollection<ChunkInfo> collection)
            {
                return EnumerateTree(collection);
            }

            return EnumerateTree(chunks.Select(c => new DedupNode(c)));
        }

        /// <summary>
        /// Non-blocking enumerable of the whole tree given an collection of chunk leaves.
        /// </summary>
        public static IEnumerable<DedupNode> EnumerateTree(IReadOnlyCollection<ChunkInfo> chunks)
        {
            var chunkNodes = chunks.Select(c => new DedupNode(c));

            // Short-circuit for most nodes and avoid the recursion/yield-return slowness
            if (chunks.Count <= DedupNode.MaxDirectChildrenPerNode)
            {
                return new[] { new DedupNode(chunkNodes) };
            }

            return EnumerateTree(chunkNodes);
        }

        /// <summary>
        /// Non-blocking enumerable of the whole tree given an collection of nodes.
        /// </summary>
        public static IEnumerable<DedupNode> EnumerateTree(IEnumerable<DedupNode> nodes)
        {
            var nextLevel = new List<DedupNode>();
            int nextLevelCount;
            do
            {
                var thisLevel = new List<DedupNode>();
                foreach (var node in nodes)
                {
                    thisLevel.Add(node);
                    if (thisLevel.Count == DedupNode.MaxDirectChildrenPerNode)
                    {
                        var newNode = new DedupNode(thisLevel);
                        yield return newNode;
                        nextLevel.Add(newNode);
                        thisLevel.Clear();
                    }
                }

                nextLevel.AddRange(thisLevel);
                foreach (var node in thisLevel)
                {
                    yield return node;
                }

                nodes = nextLevel;
                nextLevelCount = nextLevel.Count;
                nextLevel = new List<DedupNode>();
            }
            while (nextLevelCount > DedupNode.MaxDirectChildrenPerNode);

            if (nextLevelCount == 1)
            {
                yield return nodes.Single();
            }
            else
            {
                yield return new DedupNode(nodes);
            }
        }
    }
}
