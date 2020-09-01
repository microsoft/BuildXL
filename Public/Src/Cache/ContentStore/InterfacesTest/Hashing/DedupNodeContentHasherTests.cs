// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Test;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using Xunit;
using System.Diagnostics;


// VS Test Explorer can't find Mta*, so change the false to true to run the tests in VS.
#if false
using MtaFact = Xunit.FactAttribute;
using MtaTheory = Xunit.TheoryAttribute;
#endif

namespace BuildXL.Cache.ContentStore.InterfacesTest.Hashing
{
    public class DedupContentHasherTestsBase : ContentHasherTests<DedupNodeOrChunkHashAlgorithm>
    {
        static DedupContentHasherTestsBase()
        {
            Assert.Equal(8, Marshal.SizeOf<IntPtr>());
        }

        protected DedupNode HashIsStable(HashType hashType, uint byteCount, string expectedHash, int seed = 0)
        {
            DedupNode node;
            if (hashType == HashType.Dedup64K) // COMChunker only supports 64K.
            {
                if (Chunker.IsComChunkerSupported)
                {
                    using (var chunker = new ComChunker(ChunkerConfiguration.SupportedComChunkerConfiguration))
                    using (var defaultHasher = new DedupNodeOrChunkHashAlgorithm(chunker))
                    {
                        node = HashIsStableForChunker(defaultHasher, byteCount, expectedHash, seed, false);
                        node = HashIsStableForChunker(defaultHasher, byteCount, expectedHash, seed, true);
                        node = HashIsStableForChunker(defaultHasher, byteCount, expectedHash, seed, false);
                        node = HashIsStableForChunker(defaultHasher, byteCount, expectedHash, seed, true);
                    }
                }
            }

            using (var defaultHasher = new DedupNodeOrChunkHashAlgorithm(new ManagedChunker(hashType.GetChunkerConfiguration())))
            {
                node = HashIsStableForChunker(defaultHasher, byteCount, expectedHash, seed, false);
                node = HashIsStableForChunker(defaultHasher, byteCount, expectedHash, seed, true);
                node = HashIsStableForChunker(defaultHasher, byteCount, expectedHash, seed, false);
                node = HashIsStableForChunker(defaultHasher, byteCount, expectedHash, seed, true);
            }

            return node;
        }

        protected static void FillBufferWithTestContent(int seed, byte[] bytes)
        {
            if (seed < 0)
            {
                return;
            }

            var r = new Random(seed);
            r.NextBytes(bytes);
            int startZeroes = r.Next(bytes.Length);
            int endZeroes = r.Next(startZeroes, bytes.Length);
            for (int i = startZeroes; i < endZeroes; i++)
            {
                bytes[i] = 0;
            }
        }

        protected DedupNode HashIsStableForChunker(DedupNodeOrChunkHashAlgorithm hasher, uint byteCount, string expectedHash, int seed, bool sizeHint)
        {
            DedupNode node;

            if (sizeHint)
            {
                hasher.SetInputLength(byteCount);
            }

            byte[] bytes = new byte[byteCount];

            if (byteCount > 0)
            {
                FillBufferWithTestContent(seed, bytes);
            }

            hasher.ComputeHash(bytes, 0, bytes.Length);
            node = hasher.GetNode();
            Assert.Equal<long>((long)byteCount, node.EnumerateChunkLeafsInOrder().Sum(c => (long)c.TransitiveContentBytes));

            Assert.Equal(byteCount > 0, node.EnumerateChunkLeafsInOrder().All(c => c.TransitiveContentBytes != 0));

            string header = $"Seed:{seed} Length:{byteCount} Hash:";
            Assert.Equal<string>($"{header}{expectedHash}", $"{header}{node.Hash.ToHex()}");

            // TODO: Chunk size optimization - consider re-enabling this *new* proposed assert.
            //if (node.Type == DedupNode.NodeType.InnerNode)
            //{
            //    Assert.True(node.ChildNodes.Count > 1);
            //}

            return node;
        }
    }

    public class DedupContentHasherTestsSimple : DedupContentHasherTestsBase
    {
        [MtaFact]
        public void PublicConstructor()
        {
            using (var hasher = new ContentHasher<DedupNodeOrChunkHashAlgorithm>(DedupNode64KHashInfo.Instance))
            {
                Assert.NotNull(hasher);
            }
        }

        [MtaFact]
        public void IsComChunkerSupported()
        {
            Assert.Equal(
                Chunker.IsComChunkerSupported,
#if PLATFORM_WIN
                true
#else
                false
#endif
            );
        }

        private void HashOfChunksInNodeMatchesChunkHashAlgorithmInner(int expectedChunkCount, ChunkerConfiguration config, IChunker chunker)
        {
            using (DedupNodeOrChunkHashAlgorithm nodeHasher = new DedupNodeOrChunkHashAlgorithm(chunker))
            using (DedupChunkHashAlgorithm chunkHasher = new DedupChunkHashAlgorithm())
            {
                byte[] bytes = new byte[expectedChunkCount * config.AvgChunkSize];

                nodeHasher.SetInputLength(bytes.Length);

                var r = new Random(Seed: 0);
                r.NextBytes(bytes);

                nodeHasher.ComputeHash(bytes, 0, bytes.Length);
                var node = nodeHasher.GetNode();
                Assert.NotNull(node.Height);
                if (expectedChunkCount >= 2 * DedupNode.MaxDirectChildrenPerNode)
                {
                    Assert.Equal((uint)2, node.Height.Value);
                }

                ulong offset = 0;
                int chunkCount = 0;
                foreach (var chunkInNode in node.EnumerateChunkLeafsInOrder())
                {
                    byte[] chunkHash = chunkHasher.ComputeHash(bytes, (int)offset, (int)chunkInNode.TransitiveContentBytes);
                    Assert.Equal(chunkHash.ToHex(), chunkInNode.Hash.ToHex());
                    offset += chunkInNode.TransitiveContentBytes;
                    chunkCount += 1;
                }

                Assert.Equal(offset, node.TransitiveContentBytes);

                double ratio = (1.0 * expectedChunkCount) / chunkCount;
                Assert.True(Math.Abs(ratio - 1.0) < 0.3); // within 30% of expected
            }
        }

        [MtaTheory]                                                                     // avg chnk   | total bytes
        [InlineData(HashType.Dedup64K, 2 * DedupNode.MaxDirectChildrenPerNode, 1, 1)]    // 64K        | 1024 * 64 K            64MB
        [InlineData(HashType.Dedup64K, DedupNode.MaxDirectChildrenPerNode / 2, 16, 1)]   // 1MB        | 256 * 16 * 64 K        256MB
        [InlineData(HashType.Dedup1024K, DedupNode.MaxDirectChildrenPerNode / 2, 1, 1)]  // 1MB        | 256 * 1024 K           256MB
        public void HashOfChunksInNodeMatchesChunkHashAlgorithm(HashType hashType, int expectedChunkCount, int multiplier, int divider)
        {
            Assert.True(hashType.IsValidDedup(), $"Hash type: {hashType} is not a valid dedup.");
            var config = new ChunkerConfiguration((multiplier * hashType.GetAvgChunkSize()) / divider);

            HashOfChunksInNodeMatchesChunkHashAlgorithmInner(expectedChunkCount, config, new ManagedChunker(config));

            if (Chunker.IsComChunkerSupported &&
                config.AvgChunkSize == ChunkerConfiguration.SupportedComChunkerConfiguration.AvgChunkSize && // COM chunker only supports 64K.
                hashType == HashType.Dedup64K) // No COMchunker support for any other chunk sizes.
            {
                HashOfChunksInNodeMatchesChunkHashAlgorithmInner(expectedChunkCount, config, new ComChunker(config));
            }
        }

        [MtaTheory]                                                                        // avg chnk   | total bytes
        [InlineData(HashType.Dedup64K, 2 * DedupNode.MaxDirectChildrenPerNode, 1, 2)]      // 32K        | 1024 * 32K           32MB
        [InlineData(HashType.Dedup64K, 2 * DedupNode.MaxDirectChildrenPerNode, 2, 1)]      // 128K       | 1024 * 128K         128MB
        [InlineData(HashType.Dedup64K, 2 * DedupNode.MaxDirectChildrenPerNode, 4, 1)]      // 256K       | 1024 * 256K         256MB
        [InlineData(HashType.Dedup64K, DedupNode.MaxDirectChildrenPerNode, 8, 1)]          // 512K       | 512 * 512K          512MB
        [InlineData(HashType.Dedup64K, DedupNode.MaxDirectChildrenPerNode / 4, 32, 1)]     // 2MB        | 128 * 2 * 1024K     256MB
        [InlineData(HashType.Dedup64K, DedupNode.MaxDirectChildrenPerNode / 8, 64, 1)]     // 4MB        | 64 * 4 * 1024K      256MB
        // Test data for 1024K chunk sizes.
        [InlineData(HashType.Dedup1024K, 2 * DedupNode.MaxDirectChildrenPerNode, 1, 32)]    // 32K        | 1024 * 32K           32MB
        [InlineData(HashType.Dedup1024K, 2 * DedupNode.MaxDirectChildrenPerNode, 2, 16)]    // 128K       | 1024 * 128K         128MB
        [InlineData(HashType.Dedup1024K, 2 * DedupNode.MaxDirectChildrenPerNode, 4, 16)]    // 256K       | 1024 * 256K         256MB
        [InlineData(HashType.Dedup1024K, DedupNode.MaxDirectChildrenPerNode, 8, 16)]        // 512K       | 512 * 512K          512MB
        [InlineData(HashType.Dedup1024K, DedupNode.MaxDirectChildrenPerNode / 4, 32, 16)]   // 2MB        | 128 * 2 * 1024K     256MB
        [InlineData(HashType.Dedup1024K, DedupNode.MaxDirectChildrenPerNode / 8, 64, 16)]   // 4MB        | 64 * 4 * 1024K      256MB
        public void HashOfChunksInNodeMatchesChunkHashAlgorithmNegative(HashType hashType, int expectedChunkCount, int multiplier, int divider)
        {
            Assert.True(hashType.IsValidDedup(), $"Hash type: {hashType} is not a valid dedup.");
            var config = new ChunkerConfiguration((multiplier * hashType.GetAvgChunkSize()) / divider);

            Assert.Throws<NotImplementedException>(() => HashOfChunksInNodeMatchesChunkHashAlgorithmInner(expectedChunkCount, config, new ManagedChunker(config)));

            if (Chunker.IsComChunkerSupported &&
                config.AvgChunkSize == ChunkerConfiguration.SupportedComChunkerConfiguration.AvgChunkSize &&
                hashType == HashType.Dedup64K) // No COMchunker support for any other chunk sizes.
            {
                Assert.Throws<NotImplementedException>(() => HashOfChunksInNodeMatchesChunkHashAlgorithmInner(expectedChunkCount, config, new ComChunker(config)));
            }
        }

        [MtaFact]
        public void TestMismatch()
        {
            // ManagedChunker = 3C46AECFB2872004ADA998A1DAB7D03FB13E9B1A2D316B230EB673B8D8839CAB
            // ComChunker = DDD18C25F8EDE1AA79CB3401560764470316F4CA52167CD529B6E58726800255
            HashIsStable(HashType.Dedup64K, 1254972, "DDD18C25F8EDE1AA79CB3401560764470316F4CA52167CD529B6E58726800255", seed: 69519);
        }

        [MtaFact]
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

            var node = DedupNodeTree.Create(actualChunks);
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
    }

    public class DedupContentHasherTestsBasic64KChunkerSizes : DedupContentHasherTestsBase
    {
        [MtaTheory]
        // Special cases
        [InlineData(0, "CF83E1357EEFB8BDF1542850D66D8007D620E4050B5715DC83F4A921D36CE9CE")]
        [InlineData(1, "E8D0F119F2C42791C1B61150F68CC305B4054F21189F7940482F0AEDBCB28605")]
        // Min chunk size +- (1)
        [InlineData(32 * 1024 - 1, "D99C15ADC1E6F2C5C380B6589FACA4DE22057254CDBBD23C94624F0F240DCB34")]
        [InlineData(32 * 1024 + 0, "11C309A0295D6D422F40753598A72885F0C73752CACBF273FA916D5A71A77779")]
        [InlineData(32 * 1024 + 1, "4D44DAA921613193B6BEE0D41598969FA00B12BD17448FC382B48CD764BA4927")]
        // Avg chunk size.
        [InlineData(64 * 1024 - 1, "C6165BBBF51DBCB97BCCD05D27B2D40DD56815DC71B40B868FA9F3FBADAE4ADA")]
        [InlineData(64 * 1024 + 0, "E347F2D06AFA55AE4F928EA70A8180B37447F55B87E784EE2B31FE90B97718B0")]
        [InlineData(64 * 1024 + 1, "BBECCB2381484A621244D2C325C82CC6B236AEE8666BAE5682F4A0D5D4A9B47B")]
        // Max chunk size +- (1)
        [InlineData(2 * 64 * 1024 - 1, "540770B3F5DF9DD459319164D2AFCAD1B942CB24B41985AA1E0F081D6AC16639")]
        [InlineData(2 * 64 * 1024 + 0, "3175B5C2595B419DBE5BDA9554208A4E39EFDBCE1FC6F7C7CB959E5B39DF2DF0")]
        [InlineData(2 * 64 * 1024 + 1, "B39D401B85748FDFC41980A0ABE838BA05805BFFAE16344CE74EA638EE42DEA5")]
        // 512
        [InlineData(512 * 1024 - 1, "BCB207314CB1031712CAD10EB131A2CF3FBE73D0A93C1C58A3C74880C5CD5665")]
        [InlineData(512 * 1024 + 0, "200D334B0A1F4A4CCA8AC8DD266410852C7A6868B400CAF9EB8E1F55DB2B34D2")]
        [InlineData(512 * 1024 + 1, "BEBFEE89E63808866D9C6BE4B5B7C9D2A228828D6EA1BD3518E4A0A5ABEF0664")]
        // Push buffer size +- (1)
        [InlineData(1024 * 1024 - 1, "82CB11C6FBF387D4EF798C419C7F5660CAF6729742F0A5ECC37F9B5AE4AC0A11")] // Math.Max(1K, 2 * MaxChunk)
        [InlineData(1024 * 1024 + 0, "3C7D506720601D668D8AD9DE23112591876F3021D411D51F377BF6CF7B2A453C")]
        [InlineData(1024 * 1024 + 1, "39FB7E365F622543D01DE46F1BE4F51E870E9CDF4C93A633BD29EE4A24BEDBB0")]
        // Push buffer size  - 2x
        [InlineData(2 * 1024 * 1024 - 1, "63B06CEB8ECAA6747F974450446E5072A48E3F26B4AE0192FEC41DDF61B83364")]
        [InlineData(2 * 1024 * 1024 + 0, "27032B90442309EE9C4098F64AECC9BACD9B481C7A969EECFE2C56D2BDD7CA2B")]
        [InlineData(2 * 1024 * 1024 + 1, "F1AB48587008EC813EC4B69F7A938EA448CA362497D9EE4A24DEA88D8E92812B")]
        public void Basic64KChunkerSizes(uint byteCount, string expectedHash)
        {
            HashIsStable(HashType.Dedup64K, byteCount, expectedHash);
        }
    }

    public class DedupContentHasherTestsBasic1024KChunkerSizes : DedupContentHasherTestsBase
    {
        [MtaTheory]
        // Special cases
        [InlineData(0, "CF83E1357EEFB8BDF1542850D66D8007D620E4050B5715DC83F4A921D36CE9CE")]
        [InlineData(1, "E8D0F119F2C42791C1B61150F68CC305B4054F21189F7940482F0AEDBCB28605")]
        // Min chunk size +- (1)
        [InlineData(512 * 1024 - 1, "CDFCB5EE410DE554D2CB919A74E7487A2EB1A7C89C0D12915AE83EBFF2DB9140")]
        [InlineData(512 * 1024 + 0, "1D2797D3A8AEF7798CA22FDEF388EA97E7D17320DB25308CBDF71D06F2627ECC")]
        [InlineData(512 * 1024 + 1, "379F9E951429E8656D8B3F0E8AEDA03B35A6599631D696911F7E26A15B93FC62")]
        // Avg chunk size.
        [InlineData(1024 * 1024 - 1, "0840F7287CE5FB9C2647D10EA1D39E8D90DD1EC40FE3C3D973ED5DE163874209")]
        [InlineData(1024 * 1024 + 0, "589EC38BE8C1E06DC5C10EC1C36FE8156D5ECFD043B21D6E148A811AE226C1DA")]
        [InlineData(1024 * 1024 + 1, "9A0F63B35164A17A3B0CDB9744E09E5B476E9469D3E6A519E2BC3FEDFD702F8C")]
        // Max chunk size +- (1)
        [InlineData(2 * 1024 * 1024 - 1, "2FDE14F503B4D6488D56D17FAC4FC303540A64409DDBAABF7D580EAA3CE4362E")]
        [InlineData(2 * 1024 * 1024 + 0, "078F4D5660ADB8867062DCC85490DCD07E51F3B78A2D6513F6CA574D7A4326D4")]
        [InlineData(2 * 1024 * 1024 + 1, "15AA1EACC39B0F585C15E4022A995CF79FCDCF3C8B401E1D0654BAD374FF4447")]
        // Push buffer size +- (1)
        [InlineData(2 * 1024 * 1024 - 1, "2FDE14F503B4D6488D56D17FAC4FC303540A64409DDBAABF7D580EAA3CE4362E")] // Math.Max(1K, 2 * MaxChunk)
        [InlineData(2 * 1024 * 1024 + 0, "078F4D5660ADB8867062DCC85490DCD07E51F3B78A2D6513F6CA574D7A4326D4")]
        [InlineData(2 * 1024 * 1024 + 1, "15AA1EACC39B0F585C15E4022A995CF79FCDCF3C8B401E1D0654BAD374FF4447")]
        // Push buffer size  - 2x
        [InlineData(2 * 2 * 1024 * 1024 - 1, "DBFAD198E6D77784998D9D160C39AE5AE234BAB3975A06C3C216804FBFF1BE84")]
        [InlineData(2 * 2 * 1024 * 1024 + 0, "721FB7B96A15788E9B73386CAADBF9D051D658626AA6EEF65A5E080FFC30C84A")]
        [InlineData(2 * 2 * 1024 * 1024 + 1, "08E1A21465D37CA75FE58FA3745CA2D38D8684740AE4CE46F514760596942C70")]
        public void Basic1024KChunkerSizes(uint byteCount, string expectedHash)
        {
            HashIsStable(HashType.Dedup1024K, byteCount, expectedHash);
        }
    }

    public class DedupContentHasherTestsBasic64KSizesZeros : DedupContentHasherTestsBase
    {
        [MtaTheory]
        [InlineData(0, "CF83E1357EEFB8BDF1542850D66D8007D620E4050B5715DC83F4A921D36CE9CE")]
        [InlineData(1, "B8244D028981D693AF7B456AF8EFA4CAD63D282E19FF14942C246E50D9351D22")]
        [InlineData(32 * 1024 - 1, "874EFDBEF5CC034B716636A5B2173B9D5B50BF08648AA36CF1477EA7A6C1FD03")]
        [InlineData(32 * 1024 + 0, "768007E06B0CD9E62D50F458B9435C6DDA0A6D272F0B15550F97C478394B7433")]
        [InlineData(32 * 1024 + 1, "0CCA81DA5AB6ADF93C93F3F8492C5AE94F1BDA0D9C3F56F4D572B2950B14DF9C")]
        [InlineData(64 * 1024 - 1, "A90E69C10764AFDA4E65362C3D61323BFC26E1587D62E72CA40F50663815E827")]
        [InlineData(64 * 1024 + 0, "73E4153936DAB198397B74EE9EFC26093DDA721EAAB2F8D92786891153B45B04")]
        [InlineData(64 * 1024 + 1, "490821004E5A6025FE335A11F6C27B0F73CAE0434BD9D2E5AC7AEE3370BD4217")]
        [InlineData(2 * 64 * 1024 - 1, "050CBD635FB5A9F7CFFD1AB8217AE522C38AA4F5101ACBFCCBF786BB6FCEE532")]
        [InlineData(2 * 64 * 1024 + 0, "4ED83E40C9CF32AC2C59125A01170BC97F20550952C8CA20FFE1B2A59D1B1ED9")]
        [InlineData(2 * 64 * 1024 + 1, "F0D3A41AF14908E120BBCEFC431E2FE191A7D9D63BCC4FD13AE31C056E5F7A19")]
        [InlineData(512 * 1024 - 1, "9F26C3C71F61C84EF1755F8A1FD2C52FAF9CF343B03E594B0BB2FBA9D7A1B041")]
        [InlineData(512 * 1024 + 0, "7D9F0098F455D8DCDDC9F7A0EBD1F47E9BEE8868C1253EE91F2F5E7FA5E10988")]
        [InlineData(512 * 1024 + 1, "E57D134FA2511BEF58D54663648A24AC59129E006540B7360DC86DDACCCBCAFF")]
        [InlineData(1024 * 1024 - 1, "3917A5315BF6C30029ABA7A979882CA45425E20658ABBB7BA8A773718DD72896")]
        [InlineData(1024 * 1024 + 0, "48B59F990BD33E2B185DE8216D2DB5FF71173A4324709471F35A13BEAEDE93E6")]
        [InlineData(1024 * 1024 + 1, "ECDC8B0210D223ACE688076A8A5DA02F9283C9BC4A71623F3A62771B30BE0B1E")]
        [InlineData(2 * 1024 * 1024 - 1, "AD35F94D6FEEA7C2A86C74F76F5BD6E9406CDBC06003457D9AF1A9ECBEA5EB49")]
        [InlineData(2 * 1024 * 1024 + 0, "1A72355C2496C58E74F0C38612C929C340C5B33F9FFEA50CDB9C11A0FB93A357")]
        [InlineData(2 * 1024 * 1024 + 1, "EFEA345445532D152702B48ECCCCDC47379FB24F531380C0C29A61DD4299851E")]
        public void Basic64KSizesZeros(uint byteCount, string expectedHash)
        {
            HashIsStable(HashType.Dedup64K, byteCount, expectedHash, seed:-1);
        }
    }
    
    public class DedupContentHasherTestsBasic1024KSizesZeros : DedupContentHasherTestsBase
    {
        [MtaTheory]
        // Special cases
        [InlineData(0, "CF83E1357EEFB8BDF1542850D66D8007D620E4050B5715DC83F4A921D36CE9CE")]
        [InlineData(1, "B8244D028981D693AF7B456AF8EFA4CAD63D282E19FF14942C246E50D9351D22")]
        // Min chunk size +- (1)
        [InlineData(512 * 1024 - 1, "514030AD2FFCFCDE277538426C1A780D7E6888E07811DFF9AA70DCBC3CD8E8AB")]
        [InlineData(512 * 1024 + 0, "EEDB6CADBCEB2C991FC6F68DCCB80463B3F660C5358ACD7D705398AE2E3DF2B4")]
        [InlineData(512 * 1024 + 1, "915A4F532CD2FE02B897F5607EBA4BDCEC9535D536E4C4B05E836282ADCE2278")]
        // Avg chunk size.
        [InlineData(1024 * 1024 - 1, "6FDF8F07CC5FE47D2816C404DD210AE38248A383ABD6E14FE631CFEB348AF3A2")]
        [InlineData(1024 * 1024 + 0, "D6292685B380E338E025B3415A90FE8F9D39A46E7BDBA8CB78C50A338CEFCA74")]
        [InlineData(1024 * 1024 + 1, "E5EAF1EF45B2356A4877189A28555ADEFE9213DA13CE13C3D81010381EC8A451")]
        // Max chunk size +- (1)
        [InlineData(2 * 1024 * 1024 - 1, "50BB1EC40845C8F72A5E4720A0DCCDE1319448BB6508F97693974682D271B0F0")]
        [InlineData(2 * 1024 * 1024 + 0, "731859029215873FDAC1C9F2F8BD25A334ABF0F3A9E1B057CF2CACC2826D86B0")]
        [InlineData(2 * 1024 * 1024 + 1, "9DAAE363504E76E77D31F2B14B5495274FF01EFF6080A0DB6AE921F5C934154E")]
        // Push buffer size +- (1)
        [InlineData(2 * 1024 * 1024 - 1, "50BB1EC40845C8F72A5E4720A0DCCDE1319448BB6508F97693974682D271B0F0")] // Math.Max(1K, 2 * MaxChunk)
        [InlineData(2 * 1024 * 1024 + 0, "731859029215873FDAC1C9F2F8BD25A334ABF0F3A9E1B057CF2CACC2826D86B0")]
        [InlineData(2 * 1024 * 1024 + 1, "9DAAE363504E76E77D31F2B14B5495274FF01EFF6080A0DB6AE921F5C934154E")]
        // Push buffer size  - 2x
        [InlineData(2 * 2 * 1024 * 1024 - 1, "3724B43FD1832C99B48D005C7FA1BBF5D96DEA8258F73D7B0DE5624031649316")]
        [InlineData(2 * 2 * 1024 * 1024 + 0, "CEA6FD57A332CA0015AF6EE3A28FABD33826B9C3EF96B0B87EF261179A39093D")]
        [InlineData(2 * 2 * 1024 * 1024 + 1, "22C2A206940DE60E6E1A3C88E3451AE951A9477D28F1919C00DE088314B2A8D2")]
        public void Basic1024KSizesZeros(uint byteCount, string expectedHash)
        {
            HashIsStable(HashType.Dedup1024K, byteCount, expectedHash, seed:-1);
        }
    }

    abstract public class DedupContentHasherTestsCanChunkLargeFilesBase : DedupContentHasherTestsBase
    {   
        protected DedupNode CanChunkLargeFilesHelper(HashType hashType, int blockSize, int blockCount, string expected)
        {
            var r = new Random(Seed: 0);
            byte[] bytes = new byte[blockSize];
            byte[] tempBytes = new byte[blockSize];

            using (var mgdHasher = new DedupNodeOrChunkHashAlgorithm(new ManagedChunker(hashType.GetChunkerConfiguration())))
            using (var comHasher = (Chunker.IsComChunkerSupported  && hashType == HashType.Dedup64K) ?
                        new DedupNodeOrChunkHashAlgorithm(new ComChunker(ChunkerConfiguration.SupportedComChunkerConfiguration)) :
                        null)
            {
                long totalLength = (long)blockSize * blockCount;
                mgdHasher.SetInputLength(totalLength);
                comHasher?.SetInputLength(totalLength);

                FillBufferWithTestContent(seed: r.Next(), bytes);

                for (int i = 0; i < blockCount; i++)
                {
                    Task.WaitAll(
                        Task.Run(() => mgdHasher.TransformBlock(bytes, 0, bytes.Length, null, 0)),
                        Task.Run(() => comHasher?.TransformBlock(bytes, 0, bytes.Length, null, 0)),
                        // Filling the buffer for the next iteration in parallel with actual work
                        // to speed up the tests.
                        Task.Run(
                            () =>
                            {
                                if (i < blockCount - 1)
                                {
                                    FillBufferWithTestContent(seed: r.Next(), tempBytes);
                                }
                            })
                    );

                    swap(ref bytes, ref tempBytes);
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

            static void swap<T>(ref T left, ref T right) where T : class
            {
                T temp = left;
                left = right;
                right = temp;
            }
        }
    }
        
    public class DedupContentHasherTestsCanChunkLargeFiles : DedupContentHasherTestsCanChunkLargeFilesBase
    {
        [MtaFact]
        public void CanChunkLargeFiles()
        {
            var hashTypes = Enum.GetValues(typeof(HashType)).Cast<HashType>();
            foreach(var hashType in hashTypes)
            {
                if (hashType.IsValidDedup())
                {
                    var node = CanChunkLargeFilesHelper(
                        hashType,
                        hashType.GetAvgChunkSize(),
                        2 * DedupNode.MaxDirectChildrenPerNode,
                        (hashType == HashType.Dedup1024K)
                        ? "B3BFE7DD5FCB63E24E108FCE499C950003E424E2BE80B6B02639A64B43797D0A" :
                          "E0DFD15C22AB95F46A26B3ECCCE42008058FCAA06AE0CB2B56B13411A32A4592");
                    var chunks = new HashSet<string>(node.EnumerateChunkLeafsInOrder().Select(n => n.Hash.ToHex()));
                    Assert.True(chunks.Count > DedupNode.MaxDirectChildrenPerNode, $"{chunks.Count} should be > DedupNode.MaxDirectChildrenPerNode");
                }
            }
        }
    }
        
    public class DedupContentHasherTestsCan64KChunkReallyLargeFiles : DedupContentHasherTestsCanChunkLargeFilesBase
    {
        [MtaFact]
        [Trait("Category", "Integration")]
        public void Can64KChunkReallyLargeFiles()
        {
            // We want to make sure this goes past uint.MaxValue == 4GB

            HashType defaultHashType = HashType.Dedup64K;

            int blockSize = 2 * DedupNode.MaxDirectChildrenPerNode * defaultHashType.GetAvgChunkSize(); // ~64MB
            int blockCount = (int)((uint.MaxValue / (uint)blockSize) + 1);
            Assert.True(((long)blockSize * (long)blockCount) > (long)uint.MaxValue);

            var node = CanChunkLargeFilesHelper(
                defaultHashType,
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
    }
        
    public class DedupContentHasherTestsCan1024KChunkReallyLargeFiles : DedupContentHasherTestsCanChunkLargeFilesBase
    {
        [MtaFact]
        [Trait("Category", "Integration2")] // Using a different category name to allow all integration tests to run in parallel.
        public void Can1024KChunkReallyLargeFiles()
        {
            // We want to make sure this goes past uint.MaxValue == 4GB
            HashType defaultHashType = HashType.Dedup1024K;

            int blockSize = (DedupNode.MaxDirectChildrenPerNode * 2) * defaultHashType.GetAvgChunkSize(); // ~1GB => 1024 * 1024K
            int blockCount = (int)((uint.MaxValue / (uint)blockSize) + 1); // 3-4 blocks?
            Assert.True(((long)blockSize * (long)blockCount) > (long)uint.MaxValue);

            var node = CanChunkLargeFilesHelper(
                defaultHashType,
                blockSize,
                blockCount,
                "CE6299176DC223B083363ED4DF81646198DD1E4423C676B82196B7FA17031A42");

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
    }
    
    public class DedupContentHasherTestsChunksAndNodesInCommonInSimilarFiles : DedupContentHasherTestsBase
    {
        [Theory]
        [InlineData(HashType.Dedup64K)]
        [InlineData(HashType.Dedup1024K)]
        public void ChunksAndNodesInCommonInSimilarFiles(HashType hashType)
        {
            ChunksAndNodesInCommonInSimilarFilesInternal(hashType);
        }

        private void ChunksAndNodesInCommonInSimilarFilesInternal(HashType hashType)
        {
            using var hasher = new DedupNodeOrChunkHashAlgorithm(new ManagedChunker(hashType.GetChunkerConfiguration()));
            byte[] bytes = new byte[50 * 1024 * 1024];

            int offsetForSecondFile = 200 * 1024;

            var r = new Random(Seed: 0);
            r.NextBytes(bytes);

            hasher.SetInputLength(bytes.Length);
            byte[] hash1 = hasher.ComputeHash(bytes, 0, bytes.Length);
            var node1 = hasher.GetNode();
            HashSet<string> chunks1 = node1.EnumerateChunkLeafsInOrder().Select(c => c.Hash.ToHex()).ToHashSet();
            HashSet<string> nodes1 = node1.EnumerateInnerNodesDepthFirst().Select(c => c.Hash.ToHex()).ToHashSet();

            hasher.SetInputLength(bytes.Length);
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

            Assert.Equal(0, commonNodes.Count);
            Assert.Equal(nodeQueries, nodes2.Count);
            Assert.Equal(chunkQueries, chunks2.Count);
        }
    }
}