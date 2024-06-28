// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Engine.Cache.Fingerprints.TwoPhase;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Cache;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tasks;
using BuildXL.Utilities.Instrumentation.Common;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Engine.Cache.Fingerprints.PipCacheDescriptorV2Metadata.Types;

namespace Test.BuildXL.Scheduler
{
    public class HistoricMetadataCacheTests : TemporaryStorageTestBase
    {
        public HistoricMetadataCacheTests(ITestOutputHelper output)
            : base(output) { }

        /// <summary>
        /// This test validates the behaviour of HistoricMetadataCache in HashToHashAndMetadata mode.
        /// In this mode we will not be able to retrieve any of the HistoricMetadataCacheEntries.
        /// </summary>
        [Fact(Skip = "Flaky: https://dev.azure.com/mseng/1ES/_workitems/edit/2191478")]
        public async Task TestHistoricMetadataPathStringRoundtrip()
        {
            LoggingContext loggingContext = CreateLoggingContextForTest();
            var hmcFolderName = "hmc";

            for (int i = 0; i < 3; i++)
            {

                PipExecutionContext context = BuildXLContext.CreateInstanceForTesting();
                PipTwoPhaseCache cache = null;

                CreateHistoricCache(loggingContext, hmcFolderName, HistoricMetadataCacheMode.HashToHashAndMetadata, context, out cache, out var memoryArtifactCache);

                var cacheConfigData = CreateCacheConfigData(context, cache, memoryArtifactCache);

                var storedPathSet1 = await cache.TryStorePathSetAsync(cacheConfigData.PathSet1, preservePathCasing: false);
                var storedMetadata1 = await cache.TryStoreMetadataAsync(cacheConfigData.Metadata1);
                var cacheEntry = new CacheEntry(storedMetadata1.Result, nameof(HistoricMetadataCacheTests), ArrayView<ContentHash>.Empty);
                var publishedCacheEntry = await cache.TryPublishCacheEntryAsync(cacheConfigData.Process1, cacheConfigData.WeakFingerprint1, storedPathSet1.Result, cacheConfigData.StrongFingerprint1, cacheEntry);

                var storedPathSet2 = await cache.TryStorePathSetAsync(cacheConfigData.PathSet2, preservePathCasing: false);
                var storedMetadata2 = await cache.TryStoreMetadataAsync(cacheConfigData.Metadata2);
                var cacheEntry2 = new CacheEntry(storedMetadata2.Result, nameof(HistoricMetadataCacheTests), ArrayView<ContentHash>.Empty);
                var publishedCacheEntry2 = await cache.TryPublishCacheEntryAsync(cacheConfigData.Process1, cacheConfigData.WeakFingerprint1, storedPathSet2.Result, cacheConfigData.StrongFingerprint2, cacheEntry2);

                await cache.CloseAsync();
                memoryArtifactCache.Clear();

                PipTwoPhaseCache loadedCache;
                TaskSourceSlim<bool> loadCompletionSource = TaskSourceSlim.Create<bool>();
                TaskSourceSlim<bool> loadCalled = TaskSourceSlim.Create<bool>();

                CreateHistoricCache(loggingContext, "hmc", HistoricMetadataCacheMode.HashToHashAndMetadata, context, out loadedCache, out memoryArtifactCache, loadTask: async hmc =>
                {
                    loadCalled.SetResult(true);
                    await loadCompletionSource.Task;
                });

                var operationContext = OperationContext.CreateUntracked(loggingContext);
                var retrievePathSet1Task = loadedCache.TryRetrievePathSetAsync(operationContext, WeakContentFingerprint.Zero, storedPathSet1.Result);
                var retrievdMetadata1Task = loadedCache.TryRetrieveMetadataAsync(
                    cacheConfigData.Process1,
                    WeakContentFingerprint.Zero,
                    StrongContentFingerprint.Zero,
                    storedMetadata1.Result,
                    storedPathSet1.Result);

                var getCacheEntry1Task = loadedCache.TryGetCacheEntryAsync(
                    cacheConfigData.Process1,
                    cacheConfigData.WeakFingerprint1,
                    storedPathSet1.Result,
                    cacheConfigData.StrongFingerprint1,
                    hints: default);

                // This verifies that the historic metadatacache retrieval operations wait for the completion of the asynchronous load process, ensuring that
                // the loading mechanism via prepareAsync function is functioning correctly.
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
                    cacheConfigData.Process2,
                    WeakContentFingerprint.Zero,
                    StrongContentFingerprint.Zero,
                    storedMetadata2.Result,
                    storedPathSet2.Result);

                AssertPathSetEquals(cacheConfigData.PathTable, cacheConfigData.PathSet1, context.PathTable, maybeLoadedPathSet1.Result);
                AssertPathSetEquals(cacheConfigData.PathTable, cacheConfigData.PathSet2, context.PathTable, maybeLoadedPathSet2.Result);
                AssertMetadataEquals(cacheConfigData.Metadata1, maybeLoadedMetadata1.Result);
                AssertMetadataEquals(cacheConfigData.Metadata2, maybeLoadedMetadata2.Result);

                await loadedCache.CloseAsync();
            }

        }

        /// <summary>
        /// This test validates the behaviour of HistoricMetadataCache in HashToHashOnly mode.
        /// In this mode we will not be able to retrieve any of the HistoricMetadataCacheEntries.
        /// </summary>
        [Fact]
        public async Task ValidateHistoricMetadataCacheBehaviorInHashToHashOnlyMode()
        {
            LoggingContext loggingContext = CreateLoggingContextForTest();

            PipExecutionContext context = BuildXLContext.CreateInstanceForTesting();
            PipTwoPhaseCache cache = null;
            var hmcFolderName = "hmc";

            CreateHistoricCache(loggingContext, hmcFolderName, HistoricMetadataCacheMode.HashToHashOnly, context, out cache, out var memoryArtifactCache);

            var cacheConfigData = CreateCacheConfigData(context, cache, memoryArtifactCache);

            var storedPathSet1 = await cache.TryStorePathSetAsync(cacheConfigData.PathSet1, preservePathCasing: false);
            var storedMetadata1 = await cache.TryStoreMetadataAsync(cacheConfigData.Metadata1);
            var cacheEntry = new CacheEntry(storedMetadata1.Result, nameof(HistoricMetadataCacheTests), ArrayView<ContentHash>.Empty);
            var publishedCacheEntry = await cache.TryPublishCacheEntryAsync(cacheConfigData.Process1, cacheConfigData.WeakFingerprint1, storedPathSet1.Result, cacheConfigData.StrongFingerprint1, cacheEntry);

            var storedPathSet2 = await cache.TryStorePathSetAsync(cacheConfigData.PathSet2, preservePathCasing: false);
            var storedMetadata2 = await cache.TryStoreMetadataAsync(cacheConfigData.Metadata2);
            var cacheEntry2 = new CacheEntry(storedMetadata2.Result, nameof(HistoricMetadataCacheTests), ArrayView<ContentHash>.Empty);
            var publishedCacheEntry2 = await cache.TryPublishCacheEntryAsync(cacheConfigData.Process1, cacheConfigData.WeakFingerprint1, storedPathSet2.Result, cacheConfigData.StrongFingerprint2, cacheEntry2);

            await cache.CloseAsync();
            memoryArtifactCache.Clear();

            CreateHistoricCache(loggingContext, "hmc", HistoricMetadataCacheMode.HashToHashOnly, context, out cache, out memoryArtifactCache);

            var operationContext = OperationContext.CreateUntracked(loggingContext);
            var retrievePathSet1Task = cache.TryRetrievePathSetAsync(operationContext, WeakContentFingerprint.Zero, storedPathSet1.Result);
            var retrievedMetadata1Task = cache.TryRetrieveMetadataAsync(
                cacheConfigData.Process1,
                WeakContentFingerprint.Zero,
                StrongContentFingerprint.Zero,
                storedMetadata1.Result,
                storedPathSet1.Result);

            var getCacheEntry1Task = cache.TryGetCacheEntryAsync(
                cacheConfigData.Process1,
                cacheConfigData.WeakFingerprint1,
                storedPathSet1.Result,
                cacheConfigData.StrongFingerprint1,
                hints: default);

            var maybeLoadedPathSet1 = await retrievePathSet1Task;
            var maybeLoadedMetadata1 = await retrievedMetadata1Task;
            var maybeLoadedCacheEntry1 = await getCacheEntry1Task;

            XAssert.IsNull(maybeLoadedCacheEntry1.Result);

            var maybeLoadedPathSet2 = await cache.TryRetrievePathSetAsync(operationContext, WeakContentFingerprint.Zero, storedPathSet2.Result);
            var maybeLoadedMetadata2 = await cache.TryRetrieveMetadataAsync(
                cacheConfigData.Process2,
                WeakContentFingerprint.Zero,
                StrongContentFingerprint.Zero,
                storedMetadata2.Result,
                storedPathSet2.Result);

            // In the HashToHashOnly we do not make use of HistoricMetadataCache hence we do not expect any kind of pathSet or metadata retrievals to yield successfull results.
            XAssert.IsFalse(maybeLoadedPathSet1.Succeeded);
            XAssert.IsFalse(maybeLoadedPathSet2.Succeeded);
            XAssert.IsNull(maybeLoadedMetadata1.Result);
            XAssert.IsNull(maybeLoadedMetadata2.Result);

            await cache.CloseAsync();
        }

        /// <summary>
        /// Verifies the behavior of HistoricMetadataCache across different cache modes by testing the retrieval of hash to hash mappings.
        /// In the disabled mode we do not have HMC enabled hence we do not expect any kind of hash mapping retrieval.
        /// </summary>
        [Theory]
        [InlineData(HistoricMetadataCacheMode.HashToHashAndMetadata)]
        [InlineData(HistoricMetadataCacheMode.HashToHashOnly)]
        [InlineData(HistoricMetadataCacheMode.Disable)]
        public async Task TestHashToHashLookup(HistoricMetadataCacheMode historicMetadataCacheMode)
        {
            LoggingContext loggingContext = CreateLoggingContextForTest();
            PipExecutionContext context = BuildXLContext.CreateInstanceForTesting();
            PipTwoPhaseCache loadedCache;
            InMemoryArtifactContentCache memoryArtifactCache;

            CreateHistoricCache(loggingContext, "hmc", historicMetadataCacheMode, context, out loadedCache, out memoryArtifactCache);

            var contentHash1 = ContentHash.Random();
            var remappedContentHash = ContentHash.Random(contentHash1.HashType);
            loadedCache.TryStoreRemappedContentHash(contentHash1, remappedContentHash);

            await loadedCache.CloseAsync();
            memoryArtifactCache.Clear();

            CreateHistoricCache(loggingContext, "hmc", historicMetadataCacheMode, context, out loadedCache, out memoryArtifactCache);
            // When HistoricMetadataCache is disabled, PipTwoPhaseCache is enabled. Hence we do not expect any kind of hash mapping retrieval to happen.
            // But in the other two modes, HMC is enabled which allows the storage and retrieval of hash mapping.
            if (historicMetadataCacheMode == HistoricMetadataCacheMode.Disable)
            {
                XAssert.AreEqual(HashType.Unknown, loadedCache.TryGetMappedContentHash(contentHash1, contentHash1.HashType).HashType);
            }
            else
            {
                XAssert.AreEqual(remappedContentHash, loadedCache.TryGetMappedContentHash(contentHash1, contentHash1.HashType));
            }

            await loadedCache.CloseAsync();
        }

        private void CreateHistoricCache(
            LoggingContext loggingContext,
            string locationName,
            HistoricMetadataCacheMode cacheMode,
            PipExecutionContext context,
            out PipTwoPhaseCache cache,
            out InMemoryArtifactContentCache memoryCache,
            Func<PipTwoPhaseCacheWithHashLookup, Task> loadTask = null)
        {
            memoryCache = new InMemoryArtifactContentCache();
            cache = null;
            if (cacheMode == HistoricMetadataCacheMode.HashToHashAndMetadata)
            {
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
            else if (cacheMode == HistoricMetadataCacheMode.HashToHashOnly)
            {
                cache = new PipTwoPhaseCacheWithHashLookup(
                    loggingContext,
                    new EngineCache(
                        memoryCache,
                        new InMemoryTwoPhaseFingerprintStore()),
                    context,
                    new PathExpander(),
                    AbsolutePath.Create(context.PathTable, Path.Combine(TemporaryDirectory, locationName)),
                    loadTask);
            }
            else if (cacheMode == HistoricMetadataCacheMode.Disable)
            {
                cache = new PipTwoPhaseCache(
                    loggingContext,
                    new EngineCache(
                        memoryCache,
                        new InMemoryTwoPhaseFingerprintStore()),
                    context,
                    new PathExpander());
            }
        }

        /// <summary>
        /// Helper method to create HistoricMetadataCache entries.
        /// </summary>
        private static CacheConfigData CreateCacheConfigData(PipExecutionContext context, PipTwoPhaseCache cache, InMemoryArtifactContentCache memoryArtifactCache)
        {
            var process1 = SchedulerTest.CreateDummyProcess(context, new PipId(1));
            var process2 = SchedulerTest.CreateDummyProcess(context, new PipId(2));

            var pathTable = context.PathTable;

            // Add some random paths to ensure path table indices are different after loading
            AbsolutePath.Create(pathTable, X("/H/aslj/sfas/832.stxt"));
            AbsolutePath.Create(pathTable, X("/R/f/s/Historic"));
            AbsolutePath.Create(pathTable, X("/M/hgf/sf4as/83afsd"));
            AbsolutePath.Create(pathTable, X("/Z/bd/sfas/Cache"));

            var abPath1 = AbsolutePath.Create(pathTable, X("/H/aslj/sfas/p1OUT.bin"));
            var abPath2 = AbsolutePath.Create(pathTable, X("/H/aslj/sfas/P2.txt"));

            var pathSet1 = ObservedPathSetTestUtilities.CreatePathSet(
                pathTable,
                X("/X/a/b/c"),
                X("/X/d/e"),
                X("/X/a/b/c/d"));

            PipCacheDescriptorV2Metadata metadata1 = new PipCacheDescriptorV2Metadata();

            metadata1.StaticOutputHashes.Add(new AbsolutePathFileMaterializationInfo
            {
                AbsolutePath = abPath1.GetName(pathTable).ToString(context.StringTable),
                Info = new GrpcFileMaterializationInfo { FileName = "p1OUT.bin" }
            });

            var pathSet2 = ObservedPathSetTestUtilities.CreatePathSet(
                pathTable,
                X("/F/a/y/c"),
                X("/B/d/e"),
                X("/G/a/z/c/d"),
                X("/B/a/b/c"));

            PipCacheDescriptorV2Metadata metadata2 = new PipCacheDescriptorV2Metadata();
            metadata2.StaticOutputHashes.Add(new AbsolutePathFileMaterializationInfo
            {
                AbsolutePath = abPath2.ToString(pathTable),
                Info = new GrpcFileMaterializationInfo { FileName = abPath2.GetName(pathTable).ToString(context.StringTable) }
            });
            RelativePathFileMaterializationInfoList infoList = new RelativePathFileMaterializationInfoList();
            infoList.RelativePathFileMaterializationInfos.Add(new List<RelativePathFileMaterializationInfo>
                {
                    new RelativePathFileMaterializationInfo
                    {
                        RelativePath = @"dir\P2Dynamic.txt",
                        Info = new GrpcFileMaterializationInfo { FileName = "p2dynamic.txt" }
                    },
                    new RelativePathFileMaterializationInfo
                    {
                        RelativePath = @"dir\P2dynout2.txt",
                        Info = new GrpcFileMaterializationInfo { FileName = null }
                    }
                });
            metadata2.DynamicOutputs.Add(infoList);

            var weakFingerprint1 = new WeakContentFingerprint(FingerprintUtilities.CreateRandom());
            var strongFingerprint1 = new StrongContentFingerprint(FingerprintUtilities.CreateRandom());
            var strongFingerprint2 = new StrongContentFingerprint(FingerprintUtilities.CreateRandom());

            return new CacheConfigData
            {
                Process1 = process1,
                Process2 = process2,
                Metadata1 = metadata1,
                Metadata2 = metadata2,
                PathSet1 = pathSet1,
                PathSet2 = pathSet2,
                WeakFingerprint1 = weakFingerprint1,
                StrongFingerprint1 = strongFingerprint1,
                StrongFingerprint2 = strongFingerprint2,
                PathTable = pathTable
            };
        }

        private static void AssertPathSetEquals(PathTable pathTable1, ObservedPathSet pathSet1, PathTable pathTable2, ObservedPathSet pathSet2)
        {
            Assert.Equal(pathSet1.Paths.Length, pathSet2.Paths.Length);
            for (int i = 0; i < pathSet1.Paths.Length; i++)
            {
                Assert.Equal(
                    pathSet1.Paths[i].Path.ToString(pathTable1).ToCanonicalizedPath(),
                    pathSet2.Paths[i].Path.ToString(pathTable2).ToCanonicalizedPath());
            }
        }

        private static void AssertMetadataEquals(PipCacheDescriptorV2Metadata metadata1, PipCacheDescriptorV2Metadata metadata2)
        {
            Assert.Equal(metadata1.StaticOutputHashes.Count, metadata2.StaticOutputHashes.Count);
            for (int i = 0; i < metadata1.StaticOutputHashes.Count; i++)
            {
                Assert.Equal(metadata1.StaticOutputHashes[i].Info.FileName, metadata2.StaticOutputHashes[i].Info.FileName);
            }

            Assert.Equal(metadata1.DynamicOutputs.Count, metadata2.DynamicOutputs.Count);
            for (int i = 0; i < metadata1.DynamicOutputs.Count; i++)
            {
                Assert.Equal(metadata1.DynamicOutputs[i].RelativePathFileMaterializationInfos.Count, metadata2.DynamicOutputs[i].RelativePathFileMaterializationInfos.Count);
                for (int j = 0; j < metadata1.DynamicOutputs[i].RelativePathFileMaterializationInfos.Count; j++)
                {
                    Assert.Equal(metadata1.DynamicOutputs[i].RelativePathFileMaterializationInfos[j].RelativePath, metadata2.DynamicOutputs[i].RelativePathFileMaterializationInfos[j].RelativePath);
                    Assert.Equal(metadata1.DynamicOutputs[i].RelativePathFileMaterializationInfos[j].Info.FileName, metadata2.DynamicOutputs[i].RelativePathFileMaterializationInfos[j].Info.FileName);
                }
            }
        }

        /// <summary>
        /// Represents the properties necessary for setting up cache entries.
        /// </summary>
        private class CacheConfigData
        {
            internal Process Process1 { get; set; }
            internal Process Process2 { get; set; }
            internal PipCacheDescriptorV2Metadata Metadata1 { get; set; }
            internal PipCacheDescriptorV2Metadata Metadata2 { get; set; }
            internal ObservedPathSet PathSet1 { get; set; }
            internal ObservedPathSet PathSet2 { get; set; }
            internal WeakContentFingerprint WeakFingerprint1 { get; set; }
            internal StrongContentFingerprint StrongFingerprint1 { get; set; }
            internal StrongContentFingerprint StrongFingerprint2 { get; set; }
            internal PathTable PathTable { get; set; }
        }
    }
}
