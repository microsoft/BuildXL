// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Hashing
{
    public class DedupNodeContentHasherTests : ContentHasherTests<DedupNodeHashAlgorithm>
    {
        [Fact]
        public void PublicConstructor()
        {
            using (var hasher = new ContentHasher<DedupNodeHashAlgorithm>(DedupNodeHashInfo.Instance))
            {
                Assert.NotNull(hasher);
            }
        }

        [Fact]
        public void HashOfChunksInNodeMatchesChunkHashAlgorithm()
        {
            using (var nodeHasher = new DedupNodeHashAlgorithm())
            using (var chunkHasher = new DedupChunkHashAlgorithm())
            {
                byte[] bytes = new byte[2 * DedupNode.MaxDirectChildrenPerNode * (64 * 1024 /* avg chunk size */)];

                var r = new Random(Seed: 0);
                r.NextBytes(bytes);

                nodeHasher.ComputeHash(bytes, 0, bytes.Length);
                var node = nodeHasher.GetNode();
                Assert.NotNull(node.Height);
                Assert.Equal((uint)2, node.Height.Value);
                ulong offset = 0;
                foreach (var chunkInNode in node.EnumerateChunkLeafsInOrder())
                {
                    byte[] chunkHash = chunkHasher.ComputeHash(bytes, (int)offset, (int)chunkInNode.TransitiveContentBytes);
                    Assert.Equal(chunkHash, chunkInNode.Hash);
                    offset += chunkInNode.TransitiveContentBytes;
                }
            }
        }

        [Fact]
        public void CanChunkLargeFiles()
        {
            using (var hasher = new DedupNodeHashAlgorithm())
            {
                byte[] bytes = new byte[2 * DedupNode.MaxDirectChildrenPerNode * (64 * 1024 /* avg chunk size */)];

                var r = new Random(Seed: 0);
                r.NextBytes(bytes);

                hasher.Initialize();
                hasher.ComputeHash(bytes, 0, bytes.Length);
                var node = hasher.GetNode();
                Assert.Equal<string>("AED439355682D588140FAFC3C8B89CE69DD88AE69EF8E0BADD2CFE083B3165C1", node.Hash.ToHex());
                var chunks = new HashSet<string>(node.EnumerateChunkLeafsInOrder().Select(n => n.Hash.ToHex()));
                Assert.True(chunks.Count > DedupNode.MaxDirectChildrenPerNode);
            }
        }

        [Fact]
        public void CanCreateMultipleLayersOfNodes()
        {
            const int fullLayer = DedupNode.MaxDirectChildrenPerNode;
            const int twoFullLayers = fullLayer * fullLayer;
            NodeTreeChecker(chunkCount: fullLayer, expectedNodeCount: 1, expectedHeight: 1, expectedHash: "3CB2CC48422740075AE74987134D5A4577AC0010710ED67AE5235C8C5CEAF440");
            NodeTreeChecker(chunkCount: fullLayer + 1, expectedNodeCount: 2, expectedHeight: 2, expectedHash: "F8F69BFFE63DF35931C9F7146C2BB496266E2DB3F45CADE924B3A6C02B665F2F");
            NodeTreeChecker(chunkCount: fullLayer + fullLayer - 1, expectedNodeCount: 2, expectedHeight: 2, expectedHash: "1C4A9F6B398194236EA0495BD7DF45804F9A031C4A6AFEC62A9F91CBDC2B8B88");
            NodeTreeChecker(chunkCount: fullLayer + fullLayer, expectedNodeCount: 3, expectedHeight: 2, expectedHash: "632F232D5111165FAA7B381DE72637D4B205578658F65D26F26004643AB315CD");
            NodeTreeChecker(chunkCount: fullLayer + fullLayer + 1, expectedNodeCount: 3, expectedHeight: 2, expectedHash: "4B040FA332A82944D109162C8FFA62E73E22C125B95B14BD900AEA3713FE4311");
            NodeTreeChecker(chunkCount: twoFullLayers, expectedNodeCount: fullLayer + 1, expectedHeight: 2, expectedHash: "192CBA0E7953791D074E657C9D52025DD4732DA41680CCFFA2D81D5D13EAE7C1");
            NodeTreeChecker(chunkCount: twoFullLayers + 1, expectedNodeCount: fullLayer + 1 + 1, expectedHeight: 3, expectedHash: "DC2DE7A94B36C4C4A8F6F363A1E29DD5083F441ED03E8338E628DC61AB08D7F5");
            NodeTreeChecker(chunkCount: twoFullLayers + twoFullLayers, expectedNodeCount: fullLayer + fullLayer + 2 + 1, expectedHeight: 3, expectedHash: "D1049D334427690722B75EF93DFF5E5CE6E34D2A6041CC46F07C623D9A9503EB");
            NodeTreeChecker(chunkCount: twoFullLayers + twoFullLayers + 1, expectedNodeCount: fullLayer + fullLayer + 2 + 1, expectedHeight: 3, expectedHash: "097825EA5BB402CCE749F92750AF8A944DE9209C61A0E68BC5321DB7FD5337B5");
        }

        private void NodeTreeChecker(int chunkCount, int expectedNodeCount, uint expectedHeight, string expectedHash)
        {
            var r = new Random(Seed: 0);
            var actualChunks = Enumerable
                .Range(0, chunkCount)
                .Select(i =>
                    {
                        unchecked {
                            byte[] hash = new byte[32];
                            r.NextBytes(hash);
                            hash[0] = (byte)i;
                            hash[1] = (byte)(i >> 8);
                            hash[2] = (byte)(i >> 16);
                            hash[3] = (byte)(i >> 24);

                            return new ChunkInfo(0, 64 * 1024, hash);
                        }
                    })
                .ToList();

            var node = DedupNodeTree.Create(actualChunks, DedupNodeTree.Algorithm.MaximallyPacked);
            Assert.Equal<string>(expectedHash, node.Hash.ToHex());
            Assert.NotNull(node.Height);
            Assert.Equal(expectedHeight, node.Height.Value);
            var nodes = node.EnumerateInnerNodesDepthFirst().ToList();
            var nodeChunks = node.EnumerateChunkLeafsInOrder().ToList();

            var node2 = PackedDedupNodeTree.EnumerateTree(actualChunks).Last();
            Assert.Equal(node.Hash.ToHex(), node2.Hash.ToHex());

            foreach (var n in nodes)
            {
                var roundTrip = DedupNode.Deserialize(n.Serialize());
                Assert.Equal(n.Hash, roundTrip.Hash, ByteArrayComparer.Instance);
                Assert.Equal(n.ChildNodes.Count, roundTrip.ChildNodes.Count);
                Assert.True(
                    n.ChildNodes.Zip(roundTrip.ChildNodes, (e1, e2) =>
                    {
                        if (e1.Type != e2.Type)
                        {
                            return false;
                        }
                        else if (e1.TransitiveContentBytes != e2.TransitiveContentBytes)
                        {
                            return false;
                        }
                        else if (!ByteArrayComparer.Instance.Equals(e1.Hash, e2.Hash))
                        {
                            return false;
                        }

                        return true;
                    }).All(result => result));
            }

            Assert.Equal(
                actualChunks.Select(c => c.Hash.ToHex()),
                nodeChunks.Select(c => c.Hash.ToHex()));
            Assert.Equal(chunkCount, nodeChunks.Count);
            Assert.Equal(expectedNodeCount, nodes.Count);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void CanChunkReallyLargeFiles()
        {
            using (var hasher = new DedupNodeHashAlgorithm())
            {
                hasher.Initialize();
                var r = new Random(Seed: 0);
                const int AverageChunkSize = 64 * 1024;

                byte[] bytes = new byte[2 * DedupNode.MaxDirectChildrenPerNode * AverageChunkSize];
                r.NextBytes(bytes);

                // Limiting the number of iterations get the final size of about 2Gb
                // (bytes.Length is about 64Mb times 30)
                const int N = 30;

                // The loop is weird but is left for historical reasons.
                for (long totalSize = 0; totalSize < N * bytes.LongLength; totalSize += bytes.LongLength)
                {
                    hasher.TransformBlock(bytes, 0, bytes.Length, null, 0);
                }

                hasher.TransformFinalBlock(new byte[0], 0, 0);

                var node = hasher.GetNode();
                Assert.Equal<string>("0F790653BDF2B3DB8EB718897BCD46714EDC593196ECB4CAC7A9D1FA827B2442", node.Hash.ToHex());

                var chunks = node.EnumerateChunkLeafsInOrder().ToList();
                var nodes = node.EnumerateInnerNodesDepthFirst().ToList();
                int minExpectedChunks = DedupNode.MaxDirectChildrenPerNode * N;
                Assert.True(chunks.Count > minExpectedChunks, $"Expecting at least '{minExpectedChunks}' chunks but got '{chunks.Count}'.");

                int thisLevel = chunks.Count;
                int expectedNodes = 0;
                while (thisLevel > 1)
                {
                    int parentNodesForThisLevel = (thisLevel + DedupNode.MaxDirectChildrenPerNode - 1) / DedupNode.MaxDirectChildrenPerNode;
                    expectedNodes += parentNodesForThisLevel;
                    thisLevel = parentNodesForThisLevel;
                }

                Assert.True(nodes.Count <= expectedNodes);
            }
        }

        [Fact]
        public void ChunksAndNodesInCommonInSimilarFiles()
        {
            ChunksAndNodesInCommonInSimilarFiles(DedupNodeTree.Algorithm.MaximallyPacked);
            ChunksAndNodesInCommonInSimilarFiles(DedupNodeTree.Algorithm.RollingHash);
        }

        private void ChunksAndNodesInCommonInSimilarFiles(DedupNodeTree.Algorithm algorithm)
        {
            using (var hasher = new DedupNodeHashAlgorithm(algorithm))
            {
                byte[] bytes = new byte[50 * 1024 * 1024];
                int offsetForSecondFile = 200 * 1024;

                var r = new Random(Seed: 0);
                r.NextBytes(bytes);

                hasher.Initialize();
                byte[] hash1 = hasher.ComputeHash(bytes, 0, bytes.Length);
                var node1 = hasher.GetNode();
                HashSet<string> chunks1 = node1.EnumerateChunkLeafsInOrder().Select(c => c.Hash.ToHex()).ToHashSet();
                HashSet<string> nodes1 = node1.EnumerateInnerNodesDepthFirst().Select(c => c.Hash.ToHex()).ToHashSet();

                hasher.Initialize();
                byte[] hash2 = hasher.ComputeHash(bytes, offsetForSecondFile, bytes.Length - offsetForSecondFile);
                var node2 = hasher.GetNode();
                HashSet<string> chunks2 = node2.EnumerateChunkLeafsInOrder().Select(c => c.Hash.ToHex()).ToHashSet();
                HashSet<string> nodes2 = node2.EnumerateInnerNodesDepthFirst().Select(c => c.Hash.ToHex()).ToHashSet();

                Assert.NotEqual(hash1, hash2, ByteArrayComparer.Instance);

                var commonChunks = new HashSet<string>(chunks1);
                commonChunks.IntersectWith(chunks2);
                Assert.Subset(chunks1, commonChunks);
                Assert.Subset(chunks2, commonChunks);
                Assert.InRange(commonChunks.Count, chunks1.Count - (chunks1.Count / 10), chunks1.Count);
                Assert.InRange(commonChunks.Count, chunks2.Count - (chunks2.Count / 10), chunks2.Count);

                var commonNodes = new HashSet<string>(nodes1);
                commonNodes.IntersectWith(nodes2);
                Assert.Subset(nodes1, commonNodes);
                Assert.Subset(nodes2, commonNodes);

                int nodeQueries = 0;
                int chunkQueries = 0;
                node2.VisitPreorder(n =>
                {
                    switch (n.Type)
                    {
                        case DedupNode.NodeType.ChunkLeaf:
                            chunkQueries++;
                            break;
                        case DedupNode.NodeType.InnerNode:
                            nodeQueries++;
                            break;
                    }

                    return !nodes1.Contains(n.Hash.ToHex());
                });

                switch (algorithm)
                {
                    case DedupNodeTree.Algorithm.MaximallyPacked:
                        Assert.Equal(0, commonNodes.Count);
                        Assert.Equal(nodeQueries, nodes2.Count);
                        Assert.Equal(chunkQueries, chunks2.Count);
                        break;
                    case DedupNodeTree.Algorithm.RollingHash:
                        Assert.True(commonNodes.Count > 0);
                        Assert.True(nodeQueries <= nodes2.Count);
                        Assert.True(chunkQueries < chunks2.Count);
                        break;
                    default:
                        Assert.True(false);
                        break;
                }
            }
        }
    }
}
