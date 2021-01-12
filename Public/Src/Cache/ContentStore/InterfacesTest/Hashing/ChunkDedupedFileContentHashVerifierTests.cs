// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using Xunit;

namespace BuildXL.Cache.ContentStore.Interfaces.Test.Hashing
{
    public class ChunkDedupedFileContentHashVerifierTests
    {
        private static readonly IContentHasher ChunkHasher = DedupSingleChunkHashInfo.Instance.CreateContentHasher();
        private const int FixedChunkSize = 1024;

        [Fact]
        public async Task VerifySuccessForZeroContent()
        {
            (byte[] content, DedupNode expectedNode) = GetTestContent(0, FixedChunkSize);
            var success = await VerifyContentAsync(content, expectedNode);

            Assert.True(success);
        }

        [Fact]
        public async Task VerifySuccess()
        {
            (byte[] content, DedupNode expectedNode) = GetTestContent(2, FixedChunkSize);
            var success = await VerifyContentAsync(content, expectedNode);

            Assert.True(success);
        }

        [Fact]
        public async Task VerifyFailsWhenContentIsDifferent()
        {
            (byte[] content, DedupNode expectedNode) = GetTestContent(2, FixedChunkSize);
            var differentContent = new ArraySegment<byte>(content).ToArray();
            differentContent[2 * FixedChunkSize - 1] ^= 1;

            var success = await VerifyContentAsync(differentContent, expectedNode);

            Assert.False(success);
        }

        [Fact]
        public async Task VerifyFailsWhenContentIsPartial()
        {
            (byte[] content, DedupNode expectedNode) = GetTestContent(2, FixedChunkSize);
            var partialContent = new ArraySegment<byte>(content, 0, FixedChunkSize);
            var success = await VerifyContentAsync(partialContent.ToArray(), expectedNode);

            Assert.False(success);
        }

        [Fact]
        public async Task VerifyFailsWhenContentIsLonger()
        {
            (byte[] content, DedupNode expectedNode) = GetTestContent(2, FixedChunkSize);
            byte[] longerContent = new byte[2 * FixedChunkSize + 1];
            Buffer.BlockCopy(content, 0, longerContent, 0, content.Length);

            var success = await VerifyContentAsync(longerContent, expectedNode);

            Assert.False(success);
        }

        private (byte[] bytes, DedupNode contentNode) GetTestContent(int numberOfChunks, int chunkSize)
        {
            byte[] buffer = new byte[chunkSize * numberOfChunks];
            List<ChunkInfo> storedChunks = new List<ChunkInfo>();

            var random = new Random();
            random.NextBytes(buffer);

            ulong offset = 0;
            for (int i = 0; i < numberOfChunks; i++)
            {
                var newChunk = new ChunkInfo(
                    offset,
                    (uint)chunkSize,
                    ChunkHasher.GetContentHash(buffer, (int)offset, chunkSize).ToHashByteArray());

                storedChunks.Add(newChunk);
                offset += (ulong)chunkSize;
            }

            return (buffer, DedupNode.Create(storedChunks));
        }

        private async Task<bool> VerifyContentAsync(byte [] bytes, DedupNode expectedNode)
        {
            return await ChunkDedupedFileContentHashVerifier.VerifyStreamAsync(
               new MemoryStream(bytes),
               expectedNode.GetChunks().ToList(),
               new ChunkDedupedFileContentHash(expectedNode.Hash.Take(DedupSingleChunkHashInfo.Length).ToArray()),
               CancellationToken.None);
        }
    }
}
