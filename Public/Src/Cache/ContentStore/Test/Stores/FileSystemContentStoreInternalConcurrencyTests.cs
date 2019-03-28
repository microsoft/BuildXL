// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Test;
using Xunit;

namespace ContentStoreTest.Stores
{
    public class FileSystemContentStoreInternalConcurrencyTests : FileSystemContentStoreInternalTestBase
    {
        private const int ParallelFileCount = 100;
        private const int ParallelRandomContentBytes = 10;
        private readonly MemoryClock _clock;

        public FileSystemContentStoreInternalConcurrencyTests()
            : base(() => new MemoryFileSystem(new MemoryClock()), TestGlobal.Logger)
        {
            _clock = (MemoryClock)((MemoryFileSystem)FileSystem).Clock;
        }

        [Fact]
        public Task ParallelPutsGetsCopyRandomContent()
        {
            return ParallelPutsGets(true, FileRealizationMode.Copy);
        }

        [Fact]
        public Task ParallelPutsGetsHardLinkRandomContent()
        {
            return ParallelPutsGets(true, FileRealizationMode.HardLink);
        }

        [Fact]
        public Task ParallelPutsGetsCopyEmptyContent()
        {
            return ParallelPutsGets(false, FileRealizationMode.Copy);
        }

        [Fact]
        public Task ParallelPutsGetsHardLinkEmptyContent()
        {
            return ParallelPutsGets(false, FileRealizationMode.HardLink);
        }

        private Task ParallelPutsGets(bool useRandomContent, FileRealizationMode realizationModes)
        {
            var context = new Context(Logger);
            return TestStore(context, _clock, async store =>
            {
                using (var tempDirectory = new DisposableDirectory(FileSystem))
                {
                    IReadOnlyList<AbsolutePath> pathsToContent = Enumerable.Range(1, ParallelFileCount)
                        .Select(i => tempDirectory.Path / ("tempContent" + i + ".txt")).ToList();

                    using (var hasher = HashInfoLookup.Find(ContentHashType).CreateContentHasher())
                    {
                        IReadOnlyList<ContentHash> contentHashes = pathsToContent.Select(pathToContent =>
                        {
                            var bytes = useRandomContent
                                ? ThreadSafeRandom.GetBytes(ParallelRandomContentBytes)
                                : new byte[0];
                            FileSystem.WriteAllBytes(pathToContent, bytes);
                            return hasher.GetContentHash(bytes);
                        }).ToList();
                        IReadOnlyList<AbsolutePath> outPathsToContent =
                            pathsToContent.Select(pathToContent => tempDirectory.Path / ("out" + pathToContent.FileName))
                                .ToList();

                        var puts = Enumerable.Range(0, pathsToContent.Count).Select(i => Task.Run(async () =>
                            await store.PutFileAsync(context, pathsToContent[i], realizationModes, ContentHashType, null)
));
                        var gets = Enumerable.Range(0, outPathsToContent.Count).Select(i =>
                            Task.Run(async () => await store.PlaceFileAsync(
                                context,
                                contentHashes[i],
                                outPathsToContent[i],
                                FileAccessMode.ReadOnly,
                                FileReplacementMode.FailIfExists,
                                realizationModes,
                                null)));

                        await TaskSafetyHelpers.WhenAll(puts);
                        await TaskSafetyHelpers.WhenAll(gets);
                    }
                }
            });
        }
    }
}
