// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Scheduler;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using System.Threading.Tasks;
using BuildXL.Scheduler.Cache;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Fingerprints.TwoPhase;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Pips;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Engine.Cache.Fingerprints;
using System.IO;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using System;
using BuildXL.Storage;
using BuildXL.Utilities.Tasks;
using BuildXL.Cache.ContentStore.Hashing;

namespace Test.BuildXL.Scheduler
{
    public class HistoricMetadataCacheTests : TemporaryStorageTestBase
    {
        public HistoricMetadataCacheTests(ITestOutputHelper output)
            : base(output) { }

        [Fact(Skip = "Failed on RunCheckInTest or Rolling build")]
        public async Task TestHistoricMetadataPathStringRoundtrip()
        {
            LoggingContext loggingContext = CreateLoggingContextForTest();

            PipExecutionContext context;
            HistoricMetadataCache cache = null;
            var hmcFolderName = "hmc";

            for (int i = 0; i < 3; i++)
            {
                CreateHistoricCache(loggingContext, hmcFolderName, out context, out cache, out var memoryArtifactCache);

                var process1 = CreateDummyProcess(context, new PipId(1));
                var process2 = CreateDummyProcess(context, new PipId(2));

                var pathTable = context.PathTable;

                // Add some random paths to ensure path table indices are different after loading
                AbsolutePath.Create(pathTable, X("/H/aslj/sfas/832.stxt"));
                AbsolutePath.Create(pathTable, X("/R/f/s/Historic"));
                AbsolutePath.Create(pathTable, X("/M/hgf/sf4as/83afsd"));
                AbsolutePath.Create(pathTable, X("/Z/bd/sfas/Cache"));

                var pathSet1 = ObservedPathSetTestUtilities.CreatePathSet(
                    pathTable,
                    X("/X/a/b/c"),
                    X("/X/d/e"),
                    X("/X/a/b/c/d"));

                PipCacheDescriptorV2Metadata metadata1 =
                    new PipCacheDescriptorV2Metadata
                    {
                        StaticOutputHashes = new List<BondFileMaterializationInfo> { new BondFileMaterializationInfo { FileName = "p1OUT.bin" } }
                    };

                var storedPathSet1 = await cache.TryStorePathSetAsync(pathSet1);
                var storedMetadata1 = await cache.TryStoreMetadataAsync(metadata1);

                var weakFingerprint1 = new WeakContentFingerprint(FingerprintUtilities.CreateRandom());
                var strongFingerprint1 = new StrongContentFingerprint(FingerprintUtilities.CreateRandom());

                var cacheEntry = new CacheEntry(storedMetadata1.Result, nameof(HistoricMetadataCacheTests), ArrayView<ContentHash>.Empty);

                var publishedCacheEntry = await cache.TryPublishCacheEntryAsync(process1, weakFingerprint1, storedPathSet1.Result, strongFingerprint1, cacheEntry);

                var pathSet2 = ObservedPathSetTestUtilities.CreatePathSet(
                    pathTable,
                    X("/F/a/y/c"),
                    X("/B/d/e"),
                    X("/G/a/z/c/d"),
                    X("/B/a/b/c"));

                PipCacheDescriptorV2Metadata metadata2 =
                    new PipCacheDescriptorV2Metadata
                    {
                        StaticOutputHashes = new List<BondFileMaterializationInfo> { new BondFileMaterializationInfo { FileName = "P2.txt" } },
                        DynamicOutputs = new List<List<RelativePathFileMaterializationInfo>>
                                         {
                                         new List<RelativePathFileMaterializationInfo>
                                         {
                                             new RelativePathFileMaterializationInfo
                                             {
                                                 RelativePath = @"dir\P2Dynamic.txt",
                                                 Info = new BondFileMaterializationInfo {FileName = "p2dynamic.txt"}
                                             },
                                             new RelativePathFileMaterializationInfo
                                             {
                                                 RelativePath = @"dir\P2dynout2.txt",
                                                 Info = new BondFileMaterializationInfo {FileName = null}
                                             }
                                         }
                                         }
                    };

                var storedPathSet2 = await cache.TryStorePathSetAsync(pathSet2);
                var storedMetadata2 = await cache.TryStoreMetadataAsync(metadata2);
                var cacheEntry2 = new CacheEntry(storedMetadata2.Result, nameof(HistoricMetadataCacheTests), ArrayView<ContentHash>.Empty);

                var strongFingerprint2 = new StrongContentFingerprint(FingerprintUtilities.CreateRandom());

                var publishedCacheEntry2 = await cache.TryPublishCacheEntryAsync(process1, weakFingerprint1, storedPathSet2.Result, strongFingerprint2, cacheEntry2);

                await cache.CloseAsync();
                memoryArtifactCache.Clear();

                PipExecutionContext loadedContext;
                HistoricMetadataCache loadedCache;

                TaskSourceSlim<bool> loadCompletionSource = TaskSourceSlim.Create<bool>();
                TaskSourceSlim<bool> loadCalled = TaskSourceSlim.Create<bool>();
                BoxRef<bool> calledLoad = new BoxRef<bool>();
                CreateHistoricCache(loggingContext, "hmc", out loadedContext, out loadedCache, out memoryArtifactCache, loadTask: async hmc =>
                {
                    loadCalled.SetResult(true);
                    await loadCompletionSource.Task;
                });

                var operationContext = OperationContext.CreateUntracked(loggingContext);
                var retrievePathSet1Task = loadedCache.TryRetrievePathSetAsync(operationContext, WeakContentFingerprint.Zero, storedPathSet1.Result);
                var retrievdMetadata1Task = loadedCache.TryRetrieveMetadataAsync(
                    process1,
                    WeakContentFingerprint.Zero,
                    StrongContentFingerprint.Zero,
                    storedMetadata1.Result,
                    storedPathSet1.Result);

                var getCacheEntry1Task = loadedCache.TryGetCacheEntryAsync(
                    process1,
                    weakFingerprint1,
                    storedPathSet1.Result,
                    strongFingerprint1);

                Assert.False(retrievePathSet1Task.IsCompleted, "Before load task completes. TryRetrievePathSetAsync operations should block");
                Assert.False(retrievdMetadata1Task.IsCompleted, "Before load task completes. TryRetrieveMetadataAsync operations should block");
                Assert.False(getCacheEntry1Task.IsCompleted, "Before load task completes. TryGetCacheEntryAsync operations should block");

                Assert.True(loadCalled.Task.Wait(TimeSpan.FromSeconds(10)) && loadCalled.Task.Result, "Load should have been called in as a result of querying");
                loadCompletionSource.SetResult(true);

                var maybeLoadedPathSet1 = await retrievePathSet1Task;
                var maybeLoadedMetadata1 = await retrievdMetadata1Task;
                var maybeLoadedCacheEntry1 = await getCacheEntry1Task;

                Assert.Equal(storedMetadata1.Result, maybeLoadedCacheEntry1.Result.Value.MetadataHash);

                var maybeLoadedPathSet2 = await loadedCache.TryRetrievePathSetAsync(operationContext, WeakContentFingerprint.Zero, storedPathSet2.Result);
                var maybeLoadedMetadata2 = await loadedCache.TryRetrieveMetadataAsync(
                    process2,
                    WeakContentFingerprint.Zero,
                    StrongContentFingerprint.Zero,
                    storedMetadata2.Result,
                    storedPathSet2.Result);

                AssertPathSetEquals(pathTable, pathSet1, loadedContext.PathTable, maybeLoadedPathSet1.Result);
                AssertPathSetEquals(pathTable, pathSet2, loadedContext.PathTable, maybeLoadedPathSet2.Result);
                AssertMetadataEquals(metadata1, maybeLoadedMetadata1.Result);
                AssertMetadataEquals(metadata2, maybeLoadedMetadata2.Result);

                await loadedCache.CloseAsync();
            }
        }

        private void CreateHistoricCache(
            LoggingContext loggingContext,
            string locationName,
            out PipExecutionContext context,
            out HistoricMetadataCache cache,
            out InMemoryArtifactContentCache memoryCache,
            Func<HistoricMetadataCache, Task> loadTask = null)
        {
            context = BuildXLContext.CreateInstanceForTesting();
            memoryCache = new InMemoryArtifactContentCache();
            cache = new HistoricMetadataCache(
                loggingContext,
                new EngineCache(
                    memoryCache,
                    new InMemoryTwoPhaseFingerprintStore()),
                context,
                new PathExpander(),
                AbsolutePath.Create(context.PathTable, Path.Combine(TemporaryDirectory, locationName)),
                loadTask);
        }

        private static Process CreateDummyProcess(PipExecutionContext context, PipId pipId)
        {
            var exe = FileArtifact.CreateSourceFile(AbsolutePath.Create(context.PathTable, X("/X/exe")));
            List<FileArtifact> dependencies = new List<FileArtifact> { exe };

            var p = new Process(
                directoryDependencies: ReadOnlyArray<DirectoryArtifact>.Empty,
                executable: exe,
                workingDirectory: AbsolutePath.Create(context.PathTable, X("/X")),
                arguments: new PipDataBuilder(context.StringTable).ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                responseFile: FileArtifact.Invalid,
                responseFileData: PipData.Invalid,
                environmentVariables: ReadOnlyArray<EnvironmentVariable>.Empty,
                standardInput: FileArtifact.Invalid,
                standardOutput: FileArtifact.Invalid,
                standardError: FileArtifact.Invalid,
                standardDirectory: AbsolutePath.Create(context.PathTable, X("/X/std")),
                warningTimeout: null,
                timeout: null,
                dependencies: ReadOnlyArray<FileArtifact>.From(dependencies),
                outputs: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                directoryOutputs: ReadOnlyArray<DirectoryArtifact>.Empty,
                orderDependencies: ReadOnlyArray<PipId>.Empty,
                untrackedPaths: ReadOnlyArray<AbsolutePath>.Empty,
                untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty,
                tags: ReadOnlyArray<StringId>.Empty,
                successExitCodes: ReadOnlyArray<int>.Empty,
                semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                provenance: PipProvenance.CreateDummy(context),
                toolDescription: StringId.Invalid,
                additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty)
            { PipId = pipId };

            return p;
        }

        private static void AssertPathSetEquals(PathTable pathTable1, ObservedPathSet pathSet1, PathTable pathTable2, ObservedPathSet pathSet2)
        {
            Assert.Equal(pathSet1.Paths.Length, pathSet2.Paths.Length);
            for (int i = 0; i < pathSet1.Paths.Length; i++)
            {
                Assert.Equal(
                    pathSet1.Paths[i].Path.ToString(pathTable1).ToUpperInvariant(),
                    pathSet2.Paths[i].Path.ToString(pathTable2).ToUpperInvariant());
            }
        }

        private static void AssertMetadataEquals(PipCacheDescriptorV2Metadata metadata1, PipCacheDescriptorV2Metadata metadata2)
        {
            Assert.Equal(metadata1.StaticOutputHashes.Count, metadata2.StaticOutputHashes.Count);
            for (int i = 0; i < metadata1.StaticOutputHashes.Count; i++)
            {
                Assert.Equal(metadata1.StaticOutputHashes[i].FileName, metadata2.StaticOutputHashes[i].FileName);
            }

            Assert.Equal(metadata1.DynamicOutputs.Count, metadata2.DynamicOutputs.Count);
            for (int i = 0; i < metadata1.DynamicOutputs.Count; i++)
            {
                Assert.Equal(metadata1.DynamicOutputs[i].Count, metadata2.DynamicOutputs[i].Count);
                for (int j = 0; j < metadata1.DynamicOutputs[i].Count; j++)
                {
                    Assert.Equal(metadata1.DynamicOutputs[i][j].RelativePath, metadata2.DynamicOutputs[i][j].RelativePath);
                    Assert.Equal(metadata1.DynamicOutputs[i][j].Info.FileName, metadata2.DynamicOutputs[i][j].Info.FileName);
                }
            }
        }
    }
}
