// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Test;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Stores
{

    public sealed class FileSystemContentStoreInternalPassThroughFsStressTests : ContentStoreInternalTests<TestFileSystemContentStoreInternal>
    {
        private static readonly MemoryClock Clock = new MemoryClock();
        private static readonly ContentStoreConfiguration Config = ContentStoreConfiguration.CreateWithMaxSizeQuotaMB(1);

        public FileSystemContentStoreInternalPassThroughFsStressTests(ITestOutputHelper output)
            : base(() => new PassThroughFileSystem(), TestGlobal.Logger, output)
        {
        }

        protected override TestFileSystemContentStoreInternal CreateStore(DisposableDirectory testDirectory)
        {
            return new TestFileSystemContentStoreInternal(FileSystem, Clock, testDirectory.Path, Config);
        }

        [Fact(Skip = "Stress test, meant to be run by hand")]
        public async Task PerformanceTestsForWriteToTemporaryFileAsync()
        {
            // This method shows the value of always passing the length to WriteToTemporaryFileAsync.
            // On the dev machine with ssd drive and count == 100 I got the following results:
            // WriteToTemporaryFiles with no setting lengths: 17163.5408ms
            // WriteToTemporaryFiles with setting file length: 12151.7383ms
            //
            // And the profiler shows that setting the file length up-front eliminates the costly calls to SetLengthCore
            // that happen in FileStream.BeginWriteCore method if the new position is greater than the current FileStream's length.

            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var context = new Context(Logger);

                var store = CreateStore(testDirectory);

                var lengths = new int[] { 10, 100, 1000, 10_000, 100_000, 1_000_000, 10_000_000, 20_000_000 };
                var random = new Random(42);
                var streams = lengths.Select(
                    length =>
                    {
                        var result = new byte[length];
                        random.NextBytes(result);
                        return new MemoryStream(result);
                    }).ToArray();

                // Running the operation once to warm up the system.
                await WriteDataToTemporaryFiles(streams, setLength: true);

                // The number of iterations.
                int count = 100;

                FullGC();

                await WithNoSetLength();

                FullGC();

                await WithWithSetLength();

                async Task WithWithSetLength()
                {
                    var sw = Stopwatch.StartNew();
                    await WriteRandomDataNTimes(streams, setLength: true);
                    Output.WriteLine($"WriteToTemporaryFiles with setting file length: {sw.Elapsed.TotalMilliseconds}ms");
                }

                async Task WithNoSetLength()
                {
                    var sw = Stopwatch.StartNew();
                    await WriteRandomDataNTimes(streams, setLength: false);
                    Output.WriteLine($"WriteToTemporaryFiles with no setting lengths: {sw.Elapsed.TotalMilliseconds}ms");
                }


                async Task WriteRandomDataNTimes(MemoryStream[] sources, bool setLength)
                {
                    for (int i = 0; i < count; i++)
                    {
                        await WriteDataToTemporaryFiles(sources, setLength);
                    }
                }

                async Task WriteDataToTemporaryFiles(MemoryStream[] sources, bool setLength)
                {
                    foreach (var s in sources)
                    {
                        s.Position = 0;

                        await store.WriteToTemporaryFileAsync(context, s, setLength ? s.Length : null);
                    }
                }
            }
        }

        [Fact(Skip = "Failing on Linux")]
        public override Task OverwriteTests()
        {
            return Task.CompletedTask;
        }

        [Fact(Skip = "Failing on Linux")]
        public override Task PlaceCopyOverwriteReadOnly()
        {
            return Task.CompletedTask;
        }

        [Fact(Skip = "Failing on Linux")]
        public override Task PlaceReadOnly()
        {
            return Task.CompletedTask;
        }

        private static void FullGC()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        protected override void CorruptContent(TestFileSystemContentStoreInternal store, ContentHash contentHash)
        {
            store.CorruptContent(contentHash);
        }
    }
}
