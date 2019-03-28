// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
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
        /// The algorithm to use to construct the tree from the ndoes.
        /// </summary>
        public enum Algorithm : byte
        {
            /// <summary>
            /// Packs all the chunks in as few nodes as possible. Each of the children of the root will be a full tree.
            /// </summary>
            MaximallyPacked = 0,

            /// <summary>
            /// Packs chunks into variably-sized nodes so that whole sub-file nodes can be deduplicated.
            /// </summary>
            RollingHash = 1,
        }

        private const int MinVariableChildCount = AverageVariableChildCount / 2;
        private const int AverageVariableChildCount = DedupNode.MaxDirectChildrenPerNode / 4;
        private const ulong VariableChildCountBitMask = AverageVariableChildCount - 1;

        /// <summary>
        /// Creates a tree from the given chunks using the given algorithm.
        /// </summary>
        /// <returns>
        /// The root of the tree.
        /// </returns>
        public static DedupNode Create(
            IEnumerable<ChunkInfo> chunks,
            Algorithm algorithm)
        {
            DedupNode root;
            switch (algorithm)
            {
                case Algorithm.MaximallyPacked:
                    root = PackedDedupNodeTree.EnumerateTree(chunks).Last();
                    break;
                case Algorithm.RollingHash:
                    root = CreateRollingHashTree(chunks.Select(c => new DedupNode(c)).ToList());
                    break;
                default:
                    throw new ArgumentException($"Unknown Algorithm: {algorithm}", nameof(algorithm));
            }

            return root;
        }

        /// <summary>
        /// Creates a tree from the given chunks. Children are grouped to increase the likelihood of node reuse.
        /// </summary>
        private static DedupNode CreateRollingHashTree(IReadOnlyList<DedupNode> chunks)
        {
            // If we do need to make a tree, then we'll want to use a rolling hash function to ensure
            // that we get consistent groupings of children nodes even with insertions/removals
            // of children (i.e. changes to the underlying file).
            var rolling = new RollingHash(
                windowLength: 4,
                bitMask: VariableChildCountBitMask,
                minCount: MinVariableChildCount);
            var thisLevel = new Queue<DedupNode>(chunks);
            while (thisLevel.Count > DedupNode.MaxDirectChildrenPerNode)
            {
                var nextLevel = new Queue<DedupNode>();
                while (thisLevel.Any())
                {
                    rolling.Reset();
                    var nodesForChild = new List<DedupNode>();
                    while (thisLevel.Any() && nodesForChild.Count < DedupNode.MaxDirectChildrenPerNode && !rolling.IsAtBoundary)
                    {
                        var node = thisLevel.Dequeue();

                        ulong nodeHash = 0;
                        nodeHash ^= BitConverter.ToUInt64(node.Hash, 0);
                        nodeHash ^= BitConverter.ToUInt64(node.Hash, 8);
                        nodeHash ^= BitConverter.ToUInt64(node.Hash, 16);
                        nodeHash ^= BitConverter.ToUInt64(node.Hash, 24);

                        rolling.Add(nodeHash);

                        nodesForChild.Add(node);
                    }

                    var newNode = new DedupNode(nodesForChild);
                    nextLevel.Enqueue(newNode);
                }

                thisLevel = nextLevel;
            }

            var root = new DedupNode(thisLevel.ToList());
            return root;
        }

        private struct RollingHash
        {
            private readonly ulong[] _windowValues;
            private readonly ulong _bitMask;
            private readonly int _minCount;
            private int _index;
            private ulong _rollingHash;

            public RollingHash(int windowLength, ulong bitMask, int minCount)
            {
                _windowValues = new ulong[windowLength];
                _bitMask = bitMask;
                _minCount = minCount;

                _index = 0;
                _rollingHash = 0;

                Reset();
            }

            public void Reset()
            {
                for (int i = 0; i < _windowValues.Length; i++)
                {
                    _windowValues[i] = 0;
                }

                _index = 0;
                _rollingHash = 0;
            }

            public void Add(ulong value)
            {
                if (IsAtBoundary)
                {
                    throw new InvalidOperationException();
                }

                _rollingHash ^= _windowValues[_index % _windowValues.Length];
                _rollingHash ^= value;
                _index++;
                _windowValues[_index % _windowValues.Length] = _rollingHash;
            }

            public bool IsAtBoundary =>
                ((_rollingHash & _bitMask) == _bitMask) &&
                (_index >= _minCount);
        }
    }
}
