// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using Xunit;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Test;

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
        public void IsComChunkerSupported()
        {
            Assert.Equal(
                DedupNodeHashAlgorithm.IsComChunkerSupported,
#if PLATFORM_WIN
                true
#else
                false
#endif
            );    
        }

        private void HashOfChunksInNodeMatchesChunkHashAlgorithmInner(IChunker chunker)
        {
            using (var nodeHasher = new DedupNodeHashAlgorithm(chunker))
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
                    Assert.Equal(chunkHash.ToHex(), chunkInNode.Hash.ToHex());
                    offset += chunkInNode.TransitiveContentBytes;
                }
                Assert.Equal(offset, node.TransitiveContentBytes);
            }
        }

        [MtaFact]
        public void HashOfChunksInNodeMatchesChunkHashAlgorithm()
        {
            if (DedupNodeHashAlgorithm.IsComChunkerSupported)
            {
                HashOfChunksInNodeMatchesChunkHashAlgorithmInner(new ComChunker());
            }

            HashOfChunksInNodeMatchesChunkHashAlgorithmInner(new ManagedChunker());
        }

        private DedupNode HashIsStable(uint byteCount, string expectedHash, int seed = 0)
        {
            if (DedupNodeHashAlgorithm.IsComChunkerSupported)
            {
                using (var chunker = new ComChunker())
                {
                    HashIsStableForChunker(chunker, byteCount, expectedHash, seed);
                }
            }

            using (var chunker = new ManagedChunker())
            {
                return HashIsStableForChunker(chunker, byteCount, expectedHash, seed);
            }
        }

        private static void FillBufferWithTestContent(int seed, byte[] bytes)
        {
            var r = new Random(seed);
            r.NextBytes(bytes);
            int startZeroes = r.Next(bytes.Length);
            int endZeroes = r.Next(startZeroes, bytes.Length);
            for (int i = startZeroes; i < endZeroes; i++)
            {
                bytes[i] = 0;
            }
        }

        private DedupNode HashIsStableForChunker(IChunker chunker, uint byteCount, string expectedHash, int seed)
        {
            using (var hasher = new DedupNodeHashAlgorithm(chunker))
            {
                byte[] bytes = new byte[byteCount];

                if (byteCount > 0)
                {
                    FillBufferWithTestContent(seed, bytes);
                }

                hasher.Initialize();
                hasher.ComputeHash(bytes, 0, bytes.Length);
                var node = hasher.GetNode();
                Assert.Equal<long>((long)byteCount, node.EnumerateChunkLeafsInOrder().Sum(c => (long)c.TransitiveContentBytes));

                string header = $"Chunker:{chunker.GetType().Name} Seed:{seed} Length:{byteCount} Hash:";
                Assert.Equal<string>($"{header}{expectedHash}", $"{header}{node.Hash.ToHex()}");
                return node;
            }
        }

        [MtaTheory]
        [InlineData(0, "A7B5F4F67CDA9A678DE6DCBFDE1BE2902407CA2E6E899F843D4EFD1E62778D63")]
        [InlineData(1, "266CCDBB8509CCADDDD739F1F0751141D154667E9C4754604EB66B1DEE133961")]
        [InlineData(32 * 1024 - 1, "E697ED9F1250A079DC60AF3FD53793064E020231E96D69554028DD7C2E69D476")]
        [InlineData(32 * 1024 + 0, "02BB285FBEF36871C6B7694BD684822F5A36104801379B2D225B34A6739946A0")]
        [InlineData(32 * 1024 + 1, "41D54465B526473D36808AA1B1884CE98278FF1EC4BD83A84CA99590F8809818")]
        [InlineData(64 * 1024 + 0, "E347F2D06AFA55AE4F928EA70A8180B37447F55B87E784EE2B31FE90B97718B0")]
        [InlineData(2 * 64 * 1024 - 1, "540770B3F5DF9DD459319164D2AFCAD1B942CB24B41985AA1E0F081D6AC16639")]
        [InlineData(2 * 64 * 1024 + 0, "3175B5C2595B419DBE5BDA9554208A4E39EFDBCE1FC6F7C7CB959E5B39DF2DF0")]
        [InlineData(2 * 64 * 1024 + 1, "B39D401B85748FDFC41980A0ABE838BA05805BFFAE16344CE74EA638EE42DEA5")]
        [InlineData(Chunker.MinPushBufferSize - 1, "82CB11C6FBF387D4EF798C419C7F5660CAF6729742F0A5ECC37F9B5AE4AC0A11")]
        [InlineData(Chunker.MinPushBufferSize + 0, "3C7D506720601D668D8AD9DE23112591876F3021D411D51F377BF6CF7B2A453C")]
        [InlineData(Chunker.MinPushBufferSize + 1, "39FB7E365F622543D01DE46F1BE4F51E870E9CDF4C93A633BD29EE4A24BEDBB0")]
        [InlineData(2 * Chunker.MinPushBufferSize - 1, "63B06CEB8ECAA6747F974450446E5072A48E3F26B4AE0192FEC41DDF61B83364")]
        [InlineData(2 * Chunker.MinPushBufferSize + 0, "27032B90442309EE9C4098F64AECC9BACD9B481C7A969EECFE2C56D2BDD7CA2B")]
        [InlineData(2 * Chunker.MinPushBufferSize + 1, "F1AB48587008EC813EC4B69F7A938EA448CA362497D9EE4A24DEA88D8E92812B")]
        public void BasicSizes(uint byteCount, string expectedHash)
        {
            HashIsStable(byteCount, expectedHash);
        }

        [MtaFact]
        public void TestMismatch()
        {
            // ManagedChunker = 3C46AECFB2872004ADA998A1DAB7D03FB13E9B1A2D316B230EB673B8D8839CAB
            // ComChunker = DDD18C25F8EDE1AA79CB3401560764470316F4CA52167CD529B6E58726800255
            HashIsStable(1254972, "DDD18C25F8EDE1AA79CB3401560764470316F4CA52167CD529B6E58726800255", seed: 69519);
        }

        [Fact]
        public void CanCreateMultipleLayersOfNodes()
        {
            const int FullLayer = DedupNode.MaxDirectChildrenPerNode;
            const int TwoFullLayers = FullLayer * FullLayer;
            NodeTreeChecker(chunkCount: FullLayer, expectedNodeCount: 1, expectedHeight: 1, expectedHash: "3CB2CC48422740075AE74987134D5A4577AC0010710ED67AE5235C8C5CEAF440");
            NodeTreeChecker(chunkCount: FullLayer + 1, expectedNodeCount: 2, expectedHeight: 2, expectedHash: "F8F69BFFE63DF35931C9F7146C2BB496266E2DB3F45CADE924B3A6C02B665F2F");
            NodeTreeChecker(chunkCount: FullLayer + FullLayer - 1, expectedNodeCount: 2, expectedHeight: 2, expectedHash: "1C4A9F6B398194236EA0495BD7DF45804F9A031C4A6AFEC62A9F91CBDC2B8B88");
            NodeTreeChecker(chunkCount: FullLayer + FullLayer, expectedNodeCount: 3, expectedHeight: 2, expectedHash: "632F232D5111165FAA7B381DE72637D4B205578658F65D26F26004643AB315CD");
            NodeTreeChecker(chunkCount: FullLayer + FullLayer + 1, expectedNodeCount: 3, expectedHeight: 2, expectedHash: "4B040FA332A82944D109162C8FFA62E73E22C125B95B14BD900AEA3713FE4311");
            NodeTreeChecker(chunkCount: TwoFullLayers, expectedNodeCount: FullLayer + 1, expectedHeight: 2, expectedHash: "192CBA0E7953791D074E657C9D52025DD4732DA41680CCFFA2D81D5D13EAE7C1");
            NodeTreeChecker(chunkCount: TwoFullLayers + 1, expectedNodeCount: FullLayer + 1 + 1, expectedHeight: 3, expectedHash: "DC2DE7A94B36C4C4A8F6F363A1E29DD5083F441ED03E8338E628DC61AB08D7F5");
            NodeTreeChecker(chunkCount: TwoFullLayers + TwoFullLayers, expectedNodeCount: FullLayer + FullLayer + 2 + 1, expectedHeight: 3, expectedHash: "D1049D334427690722B75EF93DFF5E5CE6E34D2A6041CC46F07C623D9A9503EB");
            NodeTreeChecker(chunkCount: TwoFullLayers + TwoFullLayers + 1, expectedNodeCount: FullLayer + FullLayer + 2 + 1, expectedHeight: 3, expectedHash: "097825EA5BB402CCE749F92750AF8A944DE9209C61A0E68BC5321DB7FD5337B5");
        }

        private void NodeTreeChecker(int chunkCount, int expectedNodeCount, uint expectedHeight, string expectedHash)
        {
            var r = new Random(Seed: 0);
            var actualChunks = Enumerable
                .Range(0, chunkCount)
                .Select(i =>
                {
                    unchecked
                    {
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

        private DedupNode CanChunkLargeFilesHelper(int blockSize, int blockCount, string expected)
        {
            var r = new Random(Seed: 0);
            byte[] bytes = new byte[blockSize];

            using (var mgdHasher = new DedupNodeHashAlgorithm(new ManagedChunker()))
            using (var comHasher = DedupNodeHashAlgorithm.IsComChunkerSupported ? new DedupNodeHashAlgorithm(new ComChunker()) : null)
            {
                mgdHasher.Initialize();
                comHasher?.Initialize();

                for (int i = 0; i < blockCount; i++)
                {
                    FillBufferWithTestContent(seed: r.Next(), bytes);

                    Task.WaitAll(
                        Task.Run(() => mgdHasher.TransformBlock(bytes, 0, bytes.Length, null, 0)),
                        Task.Run(() => comHasher?.TransformBlock(bytes, 0, bytes.Length, null, 0))
                    );
                }

                mgdHasher.TransformFinalBlock(new byte[0], 0, 0);
                comHasher?.TransformFinalBlock(new byte[0], 0, 0);

                var node = mgdHasher.GetNode();
                Assert.Equal<long>(
                    (long)blockSize * blockCount,
                    node.EnumerateChunkLeafsInOrder().Sum(c => (long)c.TransitiveContentBytes));

                Assert.Equal<string>(expected, node.Hash.ToHex());
                if (comHasher != null)
                {
                    Assert.Equal<string>(expected, comHasher.GetNode().Hash.ToHex());
                }

                return node;
            }
        }

        private const int AverageChunkSize = 64 * 1024;

        [MtaFact]
        public void CanChunkLargeFiles()
        {
            var node = CanChunkLargeFilesHelper(
                AverageChunkSize,
                2 * DedupNode.MaxDirectChildrenPerNode,
                "E0DFD15C22AB95F46A26B3ECCCE42008058FCAA06AE0CB2B56B13411A32A4592");
            var chunks = new HashSet<string>(node.EnumerateChunkLeafsInOrder().Select(n => n.Hash.ToHex()));
            Assert.True(chunks.Count > DedupNode.MaxDirectChildrenPerNode, $"{chunks.Count} should be > DedupNode.MaxDirectChildrenPerNode");
        }

        [MtaFact]
        [Trait("Category", "Integration")]
        public void CanChunkReallyLargeFiles()
        {
            // We want to make sure this goes past uint.MaxValue == 4GB

            int blockSize = 2 * DedupNode.MaxDirectChildrenPerNode * AverageChunkSize; // ~64MB
            int blockCount = (int)((uint.MaxValue / (uint)blockSize) + 1);
            Assert.True(((long)blockSize * (long)blockCount) > (long)uint.MaxValue);

            var node = CanChunkLargeFilesHelper(
                blockSize,
                blockCount,
                "A09C8CB4C1B23022C571E75CA143040F9ED8D9A593A7FEECDE2B98725A19E3F5");

            var chunks = node.EnumerateChunkLeafsInOrder().ToList();
            var nodes = node.EnumerateInnerNodesDepthFirst().ToList();
            int minExpectedChunks = DedupNode.MaxDirectChildrenPerNode * blockCount;
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
