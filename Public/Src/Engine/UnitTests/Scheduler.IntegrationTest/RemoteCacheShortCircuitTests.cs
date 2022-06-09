// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Engine.Cache.Fingerprints.TwoPhase;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Tasks;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler
{
    /// <summary>
    /// Integration test for the vertical aggregator cache cutoff feature
    /// </summary>
    public class RemoteCacheShortCircuitTests : SchedulerIntegrationTestBase
    {
        private readonly HintReportingInMemoryTwoPhaseFingerprintStore m_fingerprintStore;
        private readonly HintReportingInMemoryArtifactContentCache m_artifactContentCache;

        public RemoteCacheShortCircuitTests(ITestOutputHelper output) : base(output)
        {
            m_fingerprintStore = new HintReportingInMemoryTwoPhaseFingerprintStore();
            m_artifactContentCache = new HintReportingInMemoryArtifactContentCache();
            Cache = new EngineCache(m_artifactContentCache, m_fingerprintStore);
            Configuration.Schedule.RemoteCacheCutoff = true;
            Configuration.Schedule.RemoteCacheCutoffLength = 2;
        }

        [Fact]
        public void ConsistentHints()
        {
            var skipExtraneousPins = EngineEnvironmentSettings.SkipExtraneousPins.Value;

            try
            {
                // If SkipExtraneousPins is false, we do some cache operations while loading pathsets
                // We want to exercise that codepath for some assertions below
                EngineEnvironmentSettings.SkipExtraneousPins.Value = false;

                var outDir = CreateOutputDirectoryArtifact();
                var outDirStr = ArtifactToString(outDir);
                Directory.CreateDirectory(outDirStr);

                // Create a chain PipA <- PipB <- PipC <- PipD
                // PipC always produces the same output so PipD should be a cache hit every time

                FileArtifact aTxtFile = CreateFileArtifactWithName("a.txt", ReadonlyRoot);
                var outA = CreateOutputFileArtifact(outDirStr);
                Process pipA = CreateAndSchedulePipBuilder(new Operation[]
                {
                Operation.ReadFile(aTxtFile),
                Operation.WriteFile(outA)
                }).Process;

                var outB = CreateOutputFileArtifact(outDirStr);
                Process pipB = CreateAndSchedulePipBuilder(new Operation[]
                {
                Operation.ReadFile(outA),
                Operation.WriteFile(outB)
                }).Process;

                var outC = CreateOutputFileArtifact(outDirStr);
                Process pipC = CreateAndSchedulePipBuilder(new Operation[]
                {
                Operation.ReadFile(outB),
                Operation.WriteFile(outC, "consistent content")
                }).Process;

                var outD = CreateOutputFileArtifact(outDirStr);
                Process pipD = CreateAndSchedulePipBuilder(new Operation[]
                {
                Operation.ReadFile(outC),
                Operation.WriteFile(outD)
                }).Process;

                RunScheduler().AssertCacheMiss(pipA.PipId, pipB.PipId, pipC.PipId, pipD.PipId);

                // Let's count the number of times the hint is set to true for each operation
                int avoidLookupListWeakFingerprints = 0;
                int avoidLookupTryGetCacheEntry = 0;
                int avoidLookupTryLoadAvailableContent = 0;

                m_fingerprintStore.ListPublishedEntriesByWeakFingerprintCallback = hints =>
                {
                    if (hints.AvoidRemote)
                    {
                        Interlocked.Increment(ref avoidLookupListWeakFingerprints);
                    }
                };

                m_fingerprintStore.TryGetCacheEntryCallback = hints =>
                {
                    if (hints.AvoidRemote)
                    {
                        Interlocked.Increment(ref avoidLookupTryGetCacheEntry);
                    }
                };

                m_artifactContentCache.TryLoadAvailableContentCallback = hints =>
                {
                    if (hints.AvoidRemote)
                    {
                        Interlocked.Increment(ref avoidLookupTryLoadAvailableContent);
                    }
                };

                // Induce a miss for pip A 
                File.WriteAllText(ArtifactToString(aTxtFile), "aTxtFile");

                // A, B and C are misses. D is a hit.
                var result = RunScheduler().AssertCacheMiss(pipA.PipId, pipB.PipId, pipC.PipId).AssertCacheHit(pipD.PipId);

                // There should only be one path set per pip
                // Because A,B,C are misses, both C and D have AvoidRemote = true
                Assert.Equal(2, result.PipExecutorCounters.GetCounterValue(global::BuildXL.Scheduler.PipExecutorCounter.TotalCacheLookupsAvoidingRemote));
                Assert.Equal(2, avoidLookupListWeakFingerprints);

                // Only PipD is a pathset / cache hit
                Assert.Equal(1, avoidLookupTryLoadAvailableContent);
                Assert.Equal(1, avoidLookupTryGetCacheEntry);

                // Run again. We should get cache hits so the hint is always false
                avoidLookupListWeakFingerprints = avoidLookupTryGetCacheEntry = avoidLookupTryLoadAvailableContent = 0;
                result = RunScheduler().AssertCacheHit(pipA.PipId, pipB.PipId, pipC.PipId, pipD.PipId);
                Assert.Equal(0, result.PipExecutorCounters.GetCounterValue(global::BuildXL.Scheduler.PipExecutorCounter.TotalCacheLookupsAvoidingRemote));
                Assert.Equal(0, avoidLookupTryGetCacheEntry);
                Assert.Equal(0, avoidLookupTryLoadAvailableContent);
                Assert.Equal(0, avoidLookupListWeakFingerprints);
            }
            finally
            {
                EngineEnvironmentSettings.SkipExtraneousPins.Value = skipExtraneousPins;
            }
        }

        [Fact]
        public void RemoteCacheShortCircuit()
        {
            var outDir = CreateOutputDirectoryArtifact();
            var outDirStr = ArtifactToString(outDir);
            Directory.CreateDirectory(outDirStr);

            // Create a chain PipA <- PipB <- PipC 
            var outA = CreateOutputFileArtifact(outDirStr);
            Process pipA = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.WriteFile(outA, "contentsA")
            }).Process;

            var outB = CreateOutputFileArtifact(outDirStr);
            Process pipB = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.ReadFile(outA),
                Operation.WriteFile(outB, "contentsB")
            }).Process;

            var outC = CreateOutputFileArtifact(outDirStr);
            Process pipC = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.ReadFile(outB),
                Operation.WriteFile(outC)
            }).Process;

            // Count the number of times the hint is set to true
            int avoidLookupWhileListWeakFingerprints = 0;

            m_fingerprintStore.ListPublishedEntriesByWeakFingerprintCallback = hints =>
            {
                if (hints.AvoidRemote)
                {
                    Interlocked.Increment(ref avoidLookupWhileListWeakFingerprints);
                }
            };

            var result = RunScheduler().AssertCacheMiss(pipA.PipId, pipB.PipId, pipC.PipId);

            // PipC's cache lookup was marked with the hint
            Assert.Equal(1, avoidLookupWhileListWeakFingerprints);
            Assert.Equal(1, result.PipExecutorCounters.GetCounterValue(global::BuildXL.Scheduler.PipExecutorCounter.TotalCacheLookupsAvoidingRemote));

            // Run again. We should get cache hits so the hint is always false
            avoidLookupWhileListWeakFingerprints = 0;
            result = RunScheduler().AssertCacheHit(pipA.PipId, pipB.PipId, pipC.PipId);
            Assert.Equal(0, avoidLookupWhileListWeakFingerprints);
            Assert.Equal(0, result.PipExecutorCounters.GetCounterValue(global::BuildXL.Scheduler.PipExecutorCounter.TotalCacheLookupsAvoidingRemote));
        }



        [Fact]
        public void RemoteCacheShortCircuitWithOpaqueHit()
        {
            var outDir = CreateOutputDirectoryArtifact();
            var outDirStr = ArtifactToString(outDir);

            var sod = CreateOutputDirectoryArtifact();

            Directory.CreateDirectory(outDirStr);

            // Create a chain PipA <- PipB <- [SealDirectoryPip] <- PipC 
            // We want to validate that if A,B are misses and the SealDirectory is a hit,
            // PipC still gets the avoidRemote hint as we should only consider process pips in the chain.

            FileArtifact aTxtFile = CreateFileArtifactWithName("a.txt", ReadonlyRoot);
            var outA = CreateOutputFileArtifact(outDirStr);
            Process pipA = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.ReadFile(aTxtFile),   // to induce misses
                Operation.WriteFile(outA, "contentsA"),
            }).Process;

            FileArtifact bTxtFile = CreateFileArtifactWithName("b.txt", ReadonlyRoot);
            var pipBBuilder = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(bTxtFile),   // to induce misses
                Operation.ReadFile(outA),
                Operation.WriteFile(CreateOutputFileArtifact()),
                Operation.WriteFile(CreateFileArtifactWithName("sod_content.txt", sod.Path.ToString(Context.PathTable)), "sodcontents", doNotInfer: true) // SOD contents won't change
            });

            pipBBuilder.AddOutputDirectory(sod, SealDirectoryKind.SharedOpaque);
            var pipBWithOutputs = SchedulePipBuilder(pipBBuilder);
            pipBWithOutputs.ProcessOutputs.TryGetOutputDirectory(sod.Path, out var pipBOutput);
            var pipB = pipBWithOutputs.Process;

            // PipC will consume the shared opaque directory produced by PipB.
            var pipCBuilder = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact())
            });
            pipCBuilder.AddInputDirectory(pipBOutput.Root);
            Process pipC = SchedulePipBuilder(pipCBuilder).Process;

            // Count the number of times the hint is set to true
            int avoidLookupWhileListWeakFingerprints = 0;
            m_fingerprintStore.ListPublishedEntriesByWeakFingerprintCallback = hints =>
            {
                if (hints.AvoidRemote)
                {
                    Interlocked.Increment(ref avoidLookupWhileListWeakFingerprints);
                }
            };

            // First run
            var result = RunScheduler().AssertCacheMiss(pipA.PipId, pipB.PipId, pipC.PipId);
            // PipC's cache lookup was marked with the hint
            Assert.Equal(1, avoidLookupWhileListWeakFingerprints);
            Assert.Equal(1, result.PipExecutorCounters.GetCounterValue(global::BuildXL.Scheduler.PipExecutorCounter.TotalCacheLookupsAvoidingRemote));

            // Run again inducing cache misses for PipA and PipB.
            // The SealDirectoryPip will be a "cache hit" but PipC should still get the hint
            avoidLookupWhileListWeakFingerprints = 0;
            File.WriteAllText(ArtifactToString(aTxtFile), "anothercontent");
            File.WriteAllText(ArtifactToString(bTxtFile), "anothercontent_b");

            result = RunScheduler().AssertCacheMiss(pipA.PipId, pipB.PipId);
            Assert.Equal(1, avoidLookupWhileListWeakFingerprints);
            Assert.Equal(1, result.PipExecutorCounters.GetCounterValue(global::BuildXL.Scheduler.PipExecutorCounter.TotalCacheLookupsAvoidingRemote));
        }

        #region Cache wrappers with injected callbacks
        private class HintReportingInMemoryArtifactContentCache : IArtifactContentCache
        {
            // Backing cache
            private readonly InMemoryArtifactContentCache m_cache;

            public HintReportingInMemoryArtifactContentCache() => m_cache = new();

            public Action<OperationHints> TryLoadAvailableContentCallback;

            public Task<Possible<ContentAvailabilityBatchResult, Failure>> TryLoadAvailableContentAsync(IReadOnlyList<ContentHash> hashes, CancellationToken cancellationToken, OperationHints hints = default)
            {
                TryLoadAvailableContentCallback?.Invoke(hints);
                return m_cache.TryLoadAvailableContentAsync(hashes, cancellationToken, hints);
            }


            /// <inheritdoc />
            public Task<Possible<Unit, Failure>> TryMaterializeAsync(global::BuildXL.Engine.Cache.Artifacts.FileRealizationMode fileRealizationModes, ExpandedAbsolutePath path, ContentHash contentHash, CancellationToken cancellationToken)
                => m_cache.TryMaterializeAsync(fileRealizationModes, path, contentHash, cancellationToken);

            /// <inheritdoc />
            public Task<Possible<StreamWithLength, Failure>> TryOpenContentStreamAsync(ContentHash contentHash)
                => m_cache.TryOpenContentStreamAsync(contentHash);

            /// <inheritdoc />
            public Task<Possible<Unit, Failure>> TryStoreAsync(global::BuildXL.Engine.Cache.Artifacts.FileRealizationMode fileRealizationModes, ExpandedAbsolutePath path, ContentHash contentHash, StoreArtifactOptions options = default)
                => m_cache.TryStoreAsync(fileRealizationModes, path, contentHash, options);

            /// <inheritdoc />
            public Task<Possible<ContentHash, Failure>> TryStoreAsync(global::BuildXL.Engine.Cache.Artifacts.FileRealizationMode fileRealizationModes, ExpandedAbsolutePath path, StoreArtifactOptions options = default)
                => m_cache.TryStoreAsync(fileRealizationModes, path, options);

            /// <inheritdoc />
            public Task<Possible<Unit, Failure>> TryStoreAsync(Stream content, ContentHash contentHash, StoreArtifactOptions options = default)
                => m_cache.TryStoreAsync(content, contentHash, options);
        }

        private class HintReportingInMemoryTwoPhaseFingerprintStore : ITwoPhaseFingerprintStore
        {
            // Backing store
            private readonly InMemoryTwoPhaseFingerprintStore m_store;

            public HintReportingInMemoryTwoPhaseFingerprintStore() => m_store = new();

            public Action<OperationHints> ListPublishedEntriesByWeakFingerprintCallback;
            public Action<OperationHints> TryGetCacheEntryCallback;

            /// <inheritdoc />
            public IEnumerable<Task<Possible<PublishedEntryRef, Failure>>> ListPublishedEntriesByWeakFingerprint(WeakContentFingerprint weak, OperationHints hints = default)
            {
                ListPublishedEntriesByWeakFingerprintCallback?.Invoke(hints);
                return m_store.ListPublishedEntriesByWeakFingerprint(weak, hints);
            }

            /// <inheritdoc />
            public Task<Possible<CacheEntry?, Failure>> TryGetCacheEntryAsync(WeakContentFingerprint weakFingerprint, ContentHash pathSetHash, StrongContentFingerprint strongFingerprint, OperationHints hints)
            {
                TryGetCacheEntryCallback?.Invoke(hints);
                return m_store.TryGetCacheEntryAsync(weakFingerprint, pathSetHash, strongFingerprint, hints);
            }

            /// <inheritdoc />
            public Task<Possible<CacheEntryPublishResult, Failure>> TryPublishCacheEntryAsync(WeakContentFingerprint weakFingerprint, ContentHash pathSetHash, StrongContentFingerprint strongFingerprint, CacheEntry entry, CacheEntryPublishMode mode, PublishCacheEntryOptions options)
            {
                return m_store.TryPublishCacheEntryAsync(weakFingerprint, pathSetHash, strongFingerprint, entry, mode, options);
            }
        }
        #endregion
    }
}
