// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Hashing
{
    public class ContentHasherPerformanceTests : TestWithOutput
    {
        /// <nodoc />
        public ContentHasherPerformanceTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact(Skip = "For profiling only.")]
        public async Task ProfileHashing()
        {
            // This test creates HashingStream instances with a no-op memory stream
            // to hash the content during streaming and check the performance characteristics
            // of the sequential vs. parallel hashing.

            // Debugger.Launch at the beginning and the end of the test simplifies the profiling to connect and disconnect the profiling tool.
            Debugger.Launch();
            HashType hashType = HashType.Vso0;

            byte[] data = ThreadSafeRandom.GetBytes(8 * 1024);

            int chunkCount = 20000;

            await HashContentWithStreamAsync(hashType, data, chunkCount, false);
            await HashContentWithStreamAsync(hashType, data, chunkCount, true);

            int iterationCount = 20;

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterationCount; i++)
            {
                await HashContentWithStreamAsync(hashType, data, chunkCount, true);
            }

            var parallelDuration = sw.Elapsed;

            sw.Restart();
            
            for (int i = 0; i < iterationCount; i++)
            {
                await HashContentWithStreamAsync(hashType, data, chunkCount, false);
            }

            var sequentialDuration = sw.Elapsed;

            Output.WriteLine($"Parallel: {parallelDuration}, Parallel (old): Sequential: {sequentialDuration}");
            Debugger.Launch();
        }

        private async Task<ContentHash> HashContentWithStreamAsync(HashType hashType, byte[] chunkToWrite, int chunkCount, bool useParallelHashing)
        {
            var hasher = HashInfoLookup.GetContentHasher(hashType);
            {
                var target = new NoOpMemoryStream();
                await using var writer = hasher.CreateWriteHashingStream(target, useParallelHashing ? 0 : -1);
                for (int i = 0; i < chunkCount; i++)
                {
                    await writer.WriteAsync(chunkToWrite, 0, chunkToWrite.Length);
                }

                return await writer.GetContentHashAsync();
            }
        }

        private class NoOpMemoryStream : MemoryStream
        {
            /// <inheritdoc />
            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }
        }
    }
}
