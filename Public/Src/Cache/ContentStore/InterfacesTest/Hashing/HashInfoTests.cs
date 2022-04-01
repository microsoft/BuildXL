// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Hashing
{
    public class HashInfoTests : TestWithOutput
    {
        /// <inheritdoc />
        public HashInfoTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public async Task RoundtripFullBinary()
        {
            HashType hashType = HashType.Vso0;

            byte[] data = ThreadSafeRandom.GetBytes(40 * 1024);

            int chunkCount = 100;

            await WriteFiles(hashType, data, chunkCount, false);
            await WriteFiles(hashType, data, chunkCount, true);

            int iterationCount = 100;

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterationCount; i++)
            {
                await WriteFiles(hashType, data, chunkCount, true);
            }

            var parallelDuration = sw.Elapsed;

            sw.Reset();

            for (int i = 0; i < iterationCount; i++)
            {
                await WriteFiles(hashType, data, chunkCount, false);
            }

            var sequentialDuration = sw.Elapsed;

            Output.WriteLine($"Parallel: {parallelDuration}, Sequential: {sequentialDuration}");
        }

        private async Task<ContentHash> WriteFiles(HashType hashType, byte[] chunkToWrite, int chunkCount, bool useParallelHashing)
        {
            int fileSize = chunkCount * chunkToWrite.Length;
            var hasher = HashInfoLookup.GetContentHasher(hashType);

            {
                var target = new MemoryStream();
                await using var writer = hasher.CreateWriteHashingStream(target, useParallelHashing ? fileSize : -1);
                await writer.WriteAsync(chunkToWrite, 0, chunkToWrite.Length);

                return await writer.GetContentHashAsync();
            }

        }
        [Fact]
        public void ConstLengths()
        {
            Assert.Equal(16, MD5HashInfo.Length);
            Assert.Equal(20, SHA1HashInfo.Length);
            Assert.Equal(32, SHA256HashInfo.Length);
            Assert.Equal(33, VsoHashInfo.Length);
            Assert.Equal(32, DedupSingleChunkHashInfo.Length);
        }

        [Fact]
        public void ByteLengths()
        {
            Assert.Equal(16, MD5HashInfo.Instance.ByteLength);
            Assert.Equal(20, SHA1HashInfo.Instance.ByteLength);
            Assert.Equal(32, SHA256HashInfo.Instance.ByteLength);
            Assert.Equal(33, VsoHashInfo.Instance.ByteLength);
            Assert.Equal(32, DedupSingleChunkHashInfo.Instance.ByteLength);
        }

        [Fact]
        public void EmptyHashes()
        {
            Assert.Equal("MD5:D41D8CD98F00B204E9800998ECF8427E", MD5HashInfo.Instance.EmptyHash.Serialize());
            Assert.Equal("SHA1:DA39A3EE5E6B4B0D3255BFEF95601890AFD80709", SHA1HashInfo.Instance.EmptyHash.Serialize());
            Assert.Equal(
                "SHA256:E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855",
                SHA256HashInfo.Instance.EmptyHash.Serialize());
            Assert.Equal(
                "VSO0:1E57CF2792A900D06C1CDFB3C453F35BC86F72788AA9724C96C929D1CC6B456A00",
                VsoHashInfo.Instance.EmptyHash.Serialize());
            Assert.Equal(
                "DEDUPCHUNK:CF83E1357EEFB8BDF1542850D66D8007D620E4050B5715DC83F4A921D36CE9CE",
                DedupSingleChunkHashInfo.Instance.EmptyHash.Serialize());
        }
    }
}
