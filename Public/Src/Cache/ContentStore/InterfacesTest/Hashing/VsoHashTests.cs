// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Hashing
{
    public class VsoHashTests
    {
        private static readonly int[] TestSizes =
        {
            0, 1,
            VsoHash.PageSize - 1, VsoHash.PageSize, VsoHash.PageSize + 1,
            (2 * VsoHash.PageSize) - 1, 2 * VsoHash.PageSize, (2 * VsoHash.PageSize) + 1,
            (3 * VsoHash.PageSize) - 1, 3 * VsoHash.PageSize, (3 * VsoHash.PageSize) + 1,
            VsoHash.BlockSize - 1, VsoHash.BlockSize, VsoHash.BlockSize + 1,
            (2 * VsoHash.BlockSize) - 1, 2 * VsoHash.BlockSize, (2 * VsoHash.BlockSize) + 1,
            (3 * VsoHash.BlockSize) - 1, 3 * VsoHash.BlockSize, (3 * VsoHash.BlockSize) + 1
        };

        [Fact]
        public void BlockHashesDoNotChange()
        {
            var knownValues = new Dictionary<IEnumerable<int>, string>()
            {
                {Enumerable.Empty<int>(), "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855" },
                {Enumerable.Range(0, 1), "1406E05881E299367766D313E26C05564EC91BF721D31726BD6E46E60689539A" },
                {Enumerable.Range(0, VsoHash.PageSize - 1), "12078762B8EDA8A5499C46E9B5C7F8D37BAB3A684571AEFC2A7D3ABCD56C093E" },
                {Enumerable.Range(0, VsoHash.PageSize), "B00BE365B41949CF3571F69F8F5AD95514F6AFDFE094ABA614ECD34BD828272B" },
                {Enumerable.Range(0, VsoHash.PageSize + 1), "4A3E85BABDD4243495A3617E9316BDF9CDC4526F97AA0E435A47226876C3D167" },
                {Enumerable.Range(0, VsoHash.BlockSize - 1), "3492B19DDCD76EA1ED5C07090A021705ADC7E7D5C5AAD4FF619FD12FAECEB197" },
                {Enumerable.Range(0, VsoHash.BlockSize), "E8DEEF25ED53357D2A738D7156067E69892A7BDC190818CD2AD698A3A1F95E03" },
            };

            foreach (var knownValue in knownValues)
            {
                byte[] bytes = knownValue.Key.Select(i => (byte)(i & 0xFF)).ToArray();
                Assert.Equal(
                    knownValue.Value,
                    VsoHash.HashBlock(bytes, bytes.Length).HashString);
            }
        }

        [Fact]
        public void BlobIdsDoNotChange()
        {
            var knownValues = new Dictionary<IEnumerable<int>, string>()
            {
                {Enumerable.Empty<int>(), "1E57CF2792A900D06C1CDFB3C453F35BC86F72788AA9724C96C929D1CC6B456A00" },
                {Enumerable.Range(0, 1), "3DA32150B5E69B54E7AD1765D9573BC5E6E05D3B6529556C1B4A436A76A511F400" },
                {Enumerable.Range(0, VsoHash.PageSize - 1), "4AE1AD6462D75D117A5DAFCF98167981371A4B21E1CEE49D0B982DE2CE01032300" },
                {Enumerable.Range(0, VsoHash.PageSize), "85840E1CB7CBFD78B464921C54C96F68C19066F20860EFA8CCE671B40BA5162300" },
                {Enumerable.Range(0, VsoHash.PageSize + 1), "D92A37C547F9D5B6B7B791A24F587DA8189CCA14EBC8511D2482E7448763E2BD00" },
                {Enumerable.Range(0, VsoHash.BlockSize - 1), "1C3C73F7E829E84A5BA05631195105FB49E033FA23BDA6D379B3E46B5D73EF3700" },
                {Enumerable.Range(0, VsoHash.BlockSize), "6DAE3ED3E623AED293297C289C3D20A53083529138B7631E99920EF0D93AF3CD00" },
                {Enumerable.Range(0, VsoHash.BlockSize + 1), "1F9F3C008EA37ECB65BC5FB14A420CEBB3CA72A9601EC056709A6B431F91807100" },
                {Enumerable.Range(0, (2 * VsoHash.BlockSize) - 1), "DF0E0DB15E866592DBFA9BCA74E6D547D67789F7EB088839FC1A5CEFA862353700" },
                {Enumerable.Range(0, 2 * VsoHash.BlockSize), "5E3A80B2ACB2284CD21A08979C49CBB80874E1377940699B07A8ABEE9175113200" },
                {Enumerable.Range(0, (2 * VsoHash.BlockSize) + 1), "B9A44A420593FA18453B3BE7B63922DF43C93FF52D88F2CAB26FE1FADBA7003100" },
            };

            foreach (var knownValue in knownValues)
            {
                Assert.Equal(
                    knownValue.Value,
                    VsoHash.CalculateBlobIdentifier(knownValue.Key.Select(i => (byte)(i & 0xFF)).ToArray()).ValueString);
            }
        }

        [Fact]
        public async Task HashOfEmptyStreamIsCorrect()
        {
            using (Stream contentStream = MockBuilder.GetContentStream(contentLength: 0))
            {
                Assert.Equal(contentStream.CalculateBlobIdentifierWithBlocks(), VsoHash.OfNothing);
                contentStream.Position = 0;
                Assert.Equal(await contentStream.CalculateBlobIdentifierWithBlocksAsync(), VsoHash.OfNothing);
            }
        }

        [Fact]
        public void CalculatedIdentifiersForIdenticalStreamsMatchExactly()
        {
            using (Stream contentStream = MockBuilder.GetContentStream())
            {
                byte[] contentId1 = contentStream.CalculateBlobIdentifier().Bytes;
                contentStream.Position = 0;
                byte[] contentId2 = contentStream.CalculateBlobIdentifier().Bytes;

                Assert.NotNull(contentId1);
                Assert.NotNull(contentId2);
                Assert.Equal(contentId1, contentId2);
            }
        }

        [Fact]
        public void CalculateBlobIdentifierEntireBytesForMatchingArraysMatchExactly()
        {
            byte[] content = MockBuilder.GetContent();
            byte[] contentId1 = content.CalculateBlobIdentifier().Bytes;
            byte[] contentId2 = content.CalculateBlobIdentifier().Bytes;

            Assert.NotNull(contentId1);
            Assert.NotNull(contentId2);
            Assert.Equal(contentId1, contentId2);
        }

        [Fact]
        public async Task WalkAllBlocks()
        {
            var random = new Random(0);
            await TryWithDifferentSizesAsync(async testSize =>
            {
                var bytes = new byte[testSize];
                random.NextBytes(bytes);

                using (var byteStream = new MemoryStream(bytes))
                {
                    var blobIdWithBlocks = await VsoHash.WalkAllBlobBlocksAsync(
                        byteStream,
                        null,
                        true,
                        multipleBlockCallback: (block, blockLength, blockHash, isFinalBlock) => Task.FromResult(0));

                    byteStream.Position = 0;
                    Assert.Equal(await VsoHash.CalculateBlobIdentifierWithBlocksAsync(byteStream), blobIdWithBlocks);

                    byteStream.Position = 0;
                    Assert.Equal(await VsoHash.CalculateBlobIdentifierWithBlocksAsync(byteStream), blobIdWithBlocks);
                }
            });
        }

        [Fact]
        public void ChunkHashesMatchAndRollupToIdentifier()
        {
            var random = new Random();
            TryWithDifferentSizes(testSize =>
            {
                var bytes = new byte[testSize];
                random.NextBytes(bytes);
                var blocks = new List<BlobBlockHash>();
                var rollingId = new VsoHash.RollingBlobIdentifierWithBlocks();

                BlobIdentifierWithBlocks blobIdentifierWithBlocks = null;
                for (int i = 0; i < testSize;)
                {
                    int blockSize = Math.Min(testSize - i, VsoHash.BlockSize);
                    var block = new byte[blockSize];
                    Array.Copy(bytes, i, block, 0, blockSize);
                    BlobBlockHash blockHash = VsoHash.HashBlock(block, blockSize);
                    blocks.Add(blockHash);

                    i += blockSize;
                    if (i < testSize)
                    {
                        rollingId.Update(blockHash);
                    }
                    else
                    {
                        blobIdentifierWithBlocks = rollingId.Finalize(blockHash);
                    }
                }

                if (testSize == 0)
                {
                    BlobBlockHash blockHash = VsoHash.HashBlock(new byte[] { }, 0);
                    blocks.Add(blockHash);
                    blobIdentifierWithBlocks = rollingId.Finalize(blockHash);
                }

                using (var byteStream = new MemoryStream(bytes))
                {
                    BlobIdentifierWithBlocks identifierWithBlocks = VsoHash.CalculateBlobIdentifierWithBlocks(byteStream);
                    Assert.True(identifierWithBlocks.BlockHashes.SequenceEqual(blocks));
                    Assert.Equal(identifierWithBlocks, blobIdentifierWithBlocks);
                }
            });
        }

        [Fact]
        public void CalculateBlobIdentifierEntireBytesBuiltInBlocksForIdenticalArraysMatchExactly()
        {
            using (MockBuilder.GetContentStream(VsoHash.BlockSize))
            {
                byte[] content = MockBuilder.GetContent(VsoHash.BlockSize);

                string contentId1 = VsoHash.CalculateBlobIdentifier(content).ValueString;
                string contentId2 = VsoHash.CalculateBlobIdentifier(content).ValueString;

                if (contentId1 == null || contentId2 == null)
                {
                    Assert.True(false, "inconclusive");
                }

                Assert.Equal(contentId1, contentId2);
            }
        }

        [Fact]
        public void CalculateBlobIdentifierFromArrayAndStreamMatchEachOther()
        {
            byte[] content = MockBuilder.GetContent();
            string contentId1;

            using (Stream contentStream = MockBuilder.GetContentStream())
            {
                contentId1 = VsoHash.CalculateBlobIdentifier(contentStream).ValueString;
            }

            var contentId2 = VsoHash.CalculateBlobIdentifier(content).ValueString;

            if (contentId1 == null || contentId2 == null)
            {
                Assert.True(false, "inconclusive");
            }

            Assert.Equal(contentId1, contentId2);
        }

        [Fact]
        public void CalculateBlobIdentifierFromArrayAndStreamUsingBlocksMatchEachOther()
        {
            using (Stream contentStream = MockBuilder.GetContentStream(VsoHash.BlockSize))
            {
                byte[] content = MockBuilder.GetContent(VsoHash.BlockSize);

                string contentId1 = VsoHash.CalculateBlobIdentifierWithBlocks(contentStream).BlobId.ValueString;
                string contentId2 = VsoHash.CalculateBlobIdentifier(content).ValueString;

                if (contentId1 == null || contentId2 == null)
                {
                    Assert.True(false, "inconclusive");
                }

                Assert.Equal(contentId1, contentId2);
            }
        }

        [Fact]
        public void CalculatedIdentifiersForIdenticalByteArrayBlocksMatchExactly()
        {
            byte[] content = MockBuilder.GetContent();

            string contentId1 = VsoHash.CalculateBlobIdentifier(content).ValueString;
            string contentId2 = VsoHash.CalculateBlobIdentifier(content).ValueString;

            if (contentId1 == null || contentId2 == null)
            {
                Assert.True(false, "inconclusive");
            }

            Assert.Equal(contentId1, contentId2);
        }

        [Fact]
        public void CalculateBlobIdentifierMethodThrowsWhenAStreamIsNotProvided()
        {
            Assert.Throws<ArgumentNullException>(() => VsoHash.CalculateBlobIdentifier((Stream)null));
        }

        [Fact]
        public void CalculateBlobIdentifierEntireBytesMethodThrowsWhenAByteArrayIsNotProvided()
        {
            Assert.Throws<ArgumentNullException>(() => VsoHash.CalculateBlobIdentifier((byte[])null));
        }

        [Fact]
        public async Task ThrowInParallelCallback()
        {
            try
            {
                using (var content = new MemoryStream(Enumerable.Range(0, 4 * VsoHash.BlockSize).Select(i => (byte)(i % 256)).ToArray()))
                {
                    using (var semaphore = new SemaphoreSlim(1, 1))
                    {
                        try
                        {
                            await VsoHash.WalkBlocksAsync(
                                content,
                                semaphore,
                                true,
                                singleBlockCallback: (block, blockLength, blockHash) => Task.FromResult(0),
                                multipleBlockCallback: (block, blockLength, blockHash, isFinalBlock) => Task.Run(() =>
                                {
                                    throw new FileNotFoundException();
                                }),
                                multipleBlockSealCallback: blobIdWithBlocks => Task.FromResult(0));
                        }
                        catch (AggregateException aggEx)
                        {
                            // ReSharper disable once PossibleNullReferenceException
                            throw aggEx.InnerException;
                        }
                    }
                }

                Assert.True(false, "should have thrown");
            }
            catch (FileNotFoundException)
            {
            }
        }

        private void TryWithDifferentSizes(Action<int> testAction)
        {
            foreach (int testSize in TestSizes)
            {
                testAction(testSize);
            }
        }

        private async Task TryWithDifferentSizesAsync(Func<int, Task> testAction)
        {
            foreach (int testSize in TestSizes)
            {
                await testAction(testSize);
            }
        }
    }
}
