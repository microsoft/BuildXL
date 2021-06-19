// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.Service.Grpc;
using FluentAssertions;
using Xunit;

using static BuildXL.Cache.ContentStore.Distributed.Sessions.ReadOnlyDistributedContentSession.Counters;

namespace ContentStoreTest.Distributed.Sessions
{
    public partial class LocalLocationStoreDistributedContentTests
    {
        [Fact]
        public Task ProactiveCopyDistributedTest()
        {
            EnableProactiveCopy = true;

            var contentHashes = new List<ContentHash>();

            int machineCount = 2;
            ConfigureWithOneMaster();

            return RunTestAsync(
                machineCount,
                async context =>
                {
                    var masterStore = context.GetMaster();
                    var defaultFileSize = (Config.MaxSizeQuota.Hard / 4) + 1;

                    var sessions = context.EnumerateWorkersIndices().Select(i => context.GetSession(i)).ToArray();

                    // Insert random file #1 into worker #1
                    var putResult1 = await sessions[0].PutRandomAsync(context, HashType.Vso0, false, defaultFileSize, Token).ShouldBeSuccess();
                    var hash1 = putResult1.ContentHash;

                    var getBulkResult1 = await masterStore.GetBulkAsync(context, hash1, GetBulkOrigin.Global).ShouldBeSuccess();

                    // Proactive copy should have replicated the content.
                    getBulkResult1.ContentHashesInfo[0].Locations.Count.Should().Be(2);
                },
                implicitPin: ImplicitPin.None);
        }

        [Fact]
        public Task ProactiveCopyRetryTest()
        {
            EnableProactiveCopy = true;
            ProactiveCopyRetries = 2;

            var contentHashes = new List<ContentHash>();

            int machineCount = 2;
            ConfigureWithOneMaster();

            return RunTestAsync(
                machineCount,
                async context =>
                {
                    var masterStore = context.GetMaster();
                    var defaultFileSize = (Config.MaxSizeQuota.Hard / 4) + 1;

                    var sessions = context.EnumerateWorkersIndices().Select(i => context.GetDistributedSession(i)).ToArray();

                    // Insert random file #1 into worker #1
                    var putResult1 = await sessions[0].PutRandomAsync(context, HashType.Vso0, false, defaultFileSize, Token).ShouldBeSuccess();
                    var hash1 = putResult1.ContentHash;

                    var getBulkResult1 = await masterStore.GetBulkAsync(context, hash1, GetBulkOrigin.Global).ShouldBeSuccess();

                    // Proactive copy should have replicated the content.
                    getBulkResult1.ContentHashesInfo[0].Locations.Count.Should().Be(2);

                    var counters = sessions[0].GetCounters().ToDictionaryIntegral();
                    counters["ProactiveCopyRetries.Count"].Should().Be(ProactiveCopyRetries);
                    counters["ProactiveCopyOutsideRingRetries.Count"].Should().Be(ProactiveCopyRetries);
                },
                testCopier: new ErrorReturningTestFileCopier(errorsToReturn: ProactiveCopyRetries, failingResult: PushFileResult.ServerUnavailable()),
                implicitPin: ImplicitPin.None);
        }

        [Theory]
        [InlineData(CopyResultCode.Disabled, false)]
        [InlineData(CopyResultCode.Unknown, false)]
        [InlineData(CopyResultCode.Rejected_ContentAvailableLocally, false)]
        [InlineData(CopyResultCode.Rejected_OngoingCopy, false)]
        [InlineData(CopyResultCode.FileNotFoundError, false)]
        [InlineData(CopyResultCode.Success, false)]
        [InlineData(CopyResultCode.Rejected_CopyLimitReached, true)]
        [InlineData(CopyResultCode.Rejected_NotSupported, true)]
        [InlineData(CopyResultCode.Rejected_OlderThanLastEvictedContent, true)]
        [InlineData(CopyResultCode.Rejected_Unknown, true)]
        [InlineData(CopyResultCode.ServerUnavailable, true)]
        public void ProactiveCopyStatusQualifiesForRetryTest(CopyResultCode code, bool shouldSucceed)
        {
            if (shouldSucceed)
            {
                code.QualifiesForRetry().Should().BeTrue();
            }
            else
            {
                code.QualifiesForRetry().Should().BeFalse();
            }
        }

        [Fact]
        public Task ProactiveCopyForEmptyHash2TimesDistributedTest()
        {
            EnableProactiveCopy = true;

            int machineCount = 3;
            ConfigureWithOneMaster();

            return RunTestAsync(
                machineCount,
                async context =>
                {
                    var masterStore = context.GetMaster();
                    var sessions = context.EnumerateWorkersIndices().Select(i => context.GetSession(i)).ToArray();

                    // Insert random file #1 into worker #1
                    var putResult1 = await sessions[0].PutContentAsync(context, string.Empty).ShouldBeSuccess();
                    var hash1 = putResult1.ContentHash;

                    var getBulkResult1 = await masterStore.GetBulkAsync(context, hash1, GetBulkOrigin.Global).ShouldBeSuccess();

                    // We should not be pushing empty hashes to other machines.
                    getBulkResult1.ContentHashesInfo[0].Locations.Count.Should().Be(1);

                    // Empty file should not be closed in the process.
                    putResult1 = await sessions[0].PutContentAsync(context, string.Empty).ShouldBeSuccess();
                },
                implicitPin: ImplicitPin.None);
        }

        [Fact]
        public Task PushedProactiveCopyDistributedTest()
        {
            EnableProactiveCopy = true;
            ProactiveCopyOnPuts = true;

            var contentHashes = new List<ContentHash>();

            int machineCount = 3;
            ConfigureWithOneMaster();

            return RunTestAsync(
                machineCount,
                async context =>
                {
                    var masterStore = context.GetMaster();
                    var defaultFileSize = (Config.MaxSizeQuota.Hard / 4) + 1;

                    var sessions = context.EnumerateWorkersIndices().Select(i => context.GetSession(i)).ToArray();

                    // Insert random file #1 into worker #1
                    var putResult1 = await sessions[0].PutRandomAsync(context, HashType.Vso0, false, defaultFileSize, Token).ShouldBeSuccess();
                    var hash1 = putResult1.ContentHash;

                    var getBulkResult1 = await masterStore.GetBulkAsync(context, hash1, GetBulkOrigin.Global).ShouldBeSuccess();

                    // Proactive copy should have replicated the content.
                    getBulkResult1.ContentHashesInfo[0].Locations.Count.Should().Be(2);
                },
                implicitPin: ImplicitPin.None);
        }

        [Fact]
        public Task ProactiveCopyInsideRingTest()
        {
            EnableProactiveCopy = true;
            ProactiveCopyMode = ProactiveCopyMode.InsideRing;
            ProactiveCopyOnPuts = true;
            ProactiveCopyOnPins = true;
            ProactiveCopyLocationThreshold = 4; // Large enough that we 'always' try to push.

            var contentHashes = new List<ContentHash>();

            int machineCount = 3;
            ConfigureWithOneMaster();

            var buildId = Guid.NewGuid().ToString();

            return RunTestAsync(
                machineCount,
                async context =>
                {
                    var masterStore = context.GetMaster();
                    var defaultFileSize = (Config.MaxSizeQuota.Hard / 4) + 1;

                    var sessions = context.GetDistributedSessions();

                    // Insert random file #1 into worker #1
                    var putResult = await sessions[0].PutRandomAsync(context, HashType.Vso0, false, defaultFileSize, Token).ShouldBeSuccess();
                    var hash = putResult.ContentHash;

                    var getBulkResult = await masterStore.GetBulkAsync(context, hash, GetBulkOrigin.Global).ShouldBeSuccess();

                    // Proactive copy should have replicated the content.
                    getBulkResult.ContentHashesInfo[0].Locations.Count.Should().Be(2);

                    var counters = sessions[0].GetCounters().ToDictionaryIntegral();
                    counters["ProactiveCopy_InsideRingCopies.Count"].Should().Be(1);
                    counters["ProactiveCopy_InsideRingFullyReplicated.Count"].Should().Be(0);

                    // Pin the content. Should fail the proactive copy because there re no more build-ring machines available.
                    await sessions[0].PinAsync(context, hash, CancellationToken.None).ShouldBeError();

                    getBulkResult = await masterStore.GetBulkAsync(context, hash, GetBulkOrigin.Global).ShouldBeSuccess();
                    getBulkResult.ContentHashesInfo[0].Locations.Count.Should().Be(2);

                    counters = sessions[0].GetCounters().ToDictionaryIntegral();
                    counters["ProactiveCopy_InsideRingCopies.Count"].Should().Be(2);
                    counters["ProactiveCopy_InsideRingFullyReplicated.Count"].Should().Be(1);
                },
                implicitPin: ImplicitPin.None,
                buildId: buildId);
        }

        [Fact]
        public Task ProactiveCopyOutsideRingDoesNotPickTheSameLocation()
        {
            // This test checks that if inside ring copy picked machine A, then
            // outside ring copy should not pick it again.
            EnableProactiveCopy = true;
            ProactiveCopyUsePreferredLocations = true;

            ProactiveCopyMode = ProactiveCopyMode.Both;
            ProactiveCopyOnPuts = true;
            ProactiveCopyLocationThreshold = 4; // Large enough that we 'always' try to push.
            ConfigureWithOneMaster(dcs =>
            {
                dcs.RestoreCheckpointAgeThresholdMinutes = 0;
            });

            int machineCount = 3;

            var buildId = Guid.NewGuid().ToString();

            return RunTestAsync(
                machineCount,
                async context =>
                {
                    var master = context.GetMasterIndex();
                    var workerIndex = context.GetFirstWorkerIndex();

                    var ls = Enumerable.Range(0, machineCount).Select(n => context.GetLocationStore(n)).ToArray();

                    // Adding random content just to generate events for the master, because otherwise checkpoint can't be created.
                    await context.GetDistributedSession(master).PutRandomAsync(context, HashType.Vso0, false, ContentByteCount, Token).ShouldBeSuccess();
                    await ls[master].LocalLocationStore.CreateCheckpointAsync(context).ShouldBeSuccess();

                    // Restoring the checkpoint because the bin manager is stored in the checkpoint instance.
                    TestClock.UtcNow += TimeSpan.FromMinutes(5);
                    await ls[workerIndex].LocalLocationStore.HeartbeatAsync(context, inline: true, forceRestore: true).ShouldBeSuccess();

                    // We have 3 stores, meaning that we should be able to replicate the content on two other machines if we never pick
                    // the same machine more than once.
                    // We used to have an issue when a designated machine was inside the ring and we tried pushing to the same machine twice.
                    var workerSession = context.GetDistributedSession(workerIndex);
                    int contentCount = 10;
                    for (int i = 0; i < contentCount; i++)
                    {
                        var putResult = await workerSession.PutRandomAsync(context, HashType.Vso0, false, ContentByteCount, Token).ShouldBeSuccess();

                        var masterResult = await ls[workerIndex].GetBulkAsync(context, new[] { putResult.ContentHash }, Token, UrgencyHint.Nominal, GetBulkOrigin.Global).ShouldBeSuccess();
                        masterResult.ContentHashesInfo[0].Locations.Count.Should().Be(3);
                    }
                },
                implicitPin: ImplicitPin.None,
                buildId: buildId);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public Task ProactiveReplicationTest(bool usePreferredLocations)
        {
            EnableProactiveReplication = true;
            EnableProactiveCopy = true;
            ProactiveCopyOnPuts = false;
            ProactiveCopyOnPins = false;
            ProactiveCopyUsePreferredLocations = usePreferredLocations;

            // Master does not participate in proactive copies when using preferred locations.
            var storeCount = usePreferredLocations ? 3 : 2;

            ConfigureWithOneMaster(dcs =>
            {
                dcs.RestoreCheckpointAgeThresholdMinutes = 0;
                dcs.ProactiveCopyLocationsThreshold = 2;

                // Due to timing, proactive copy may happen from random source if allow proactive
                // copies 
                dcs.EnableProactiveReplication = dcs.TestIteration == 2;
            });

            PutResult putResult = default;

            var proactiveReplicator = 1;

            return RunTestAsync(
                storeCount,
                // Iteration 0 content is put and state (checkpoint, bin manager) is uploaded
                // Iteration 1 is just to allow state to propagate before
                // Iteration 2 where proactive replication is enabled and we verify correct behavior
                iterations: 3,
                storeToStartupLast: proactiveReplicator,
                testFunc: async context =>
                {
                    var sessions = context.Sessions;
                    var master = context.GetMasterIndex();

                    var ls = Enumerable.Range(0, storeCount).Select(n => context.GetLocationStore(n)).ToArray();
                    var lls = Enumerable.Range(0, storeCount).Select(n => context.GetLocalLocationStore(n)).ToArray();

                    if (context.Iteration == 0)
                    {
                        putResult = await sessions[proactiveReplicator].PutRandomAsync(context, ContentHashType, false, ContentByteCount, Token).ShouldBeSuccess();

                        // Content should be available in only one session, with proactive put set to false.
                        var masterResult = await ls[master].GetBulkAsync(context, new[] { putResult.ContentHash }, Token, UrgencyHint.Nominal, GetBulkOrigin.Local).ShouldBeSuccess();
                        masterResult.ContentHashesInfo[0].Locations.Count.Should().Be(1);
                        await ls[master].LocalLocationStore.CreateCheckpointAsync(context).ShouldBeSuccess();

                        TestClock.UtcNow += TimeSpan.FromMinutes(5);
                    }
                    else if (context.Iteration == 2)
                    {
                        var proactiveStore = (DistributedContentStore)context.GetDistributedStore(proactiveReplicator);
                        var proactiveSession = (await proactiveStore.ProactiveCopySession.Value).ThrowIfFailure();
                        var counters = proactiveSession.GetCounters().ToDictionaryIntegral();
                        counters["ProactiveCopy_OutsideRingFromPreferredLocations.Count"].Should().Be(usePreferredLocations ? 1 : 0);

                        // Content should be available in two sessions, due to proactive replication in second iteration.
                        var masterResult = await ls[master].GetBulkAsync(context, new[] { putResult.ContentHash }, Token, UrgencyHint.Nominal, GetBulkOrigin.Global).ShouldBeSuccess();
                        masterResult.ContentHashesInfo[0].Locations.Count.Should().Be(2);
                    }
                });
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public Task ProactiveReplicationIterationRespectsLimit(bool usePreferredLocations)
        {
            EnableProactiveReplication = true;
            EnableProactiveCopy = true;
            ProactiveCopyOnPuts = false;
            ProactiveCopyOnPins = false;
            ProactiveCopyUsePreferredLocations = usePreferredLocations;

            // Master does not participate in proactive copies when using preferred locations.
            var storeCount = usePreferredLocations ? 3 : 2;
            var proactiveReplicationCopyLimit = 2;

            ConfigureWithOneMaster(dcs =>
            {
                dcs.RestoreCheckpointAgeThresholdMinutes = 0;
                dcs.ProactiveCopyLocationsThreshold = 2;

                dcs.ProactiveReplicationCopyLimit = proactiveReplicationCopyLimit;

                // Due to timing, proactive copy may happen from random source if allow proactive
                // copies 
                dcs.EnableProactiveReplication = dcs.TestIteration == 2;
            });

            PutResult[] putResults = new PutResult[proactiveReplicationCopyLimit + 1]; // One piece of content should not be proactively replicated.

            var proactiveReplicator = 1;

            return RunTestAsync(
                storeCount,
                // Iteration 0 content is put and state (checkpoint, bin manager) is uploaded
                // Iteration 1 is just to allow state to propagate before
                // Iteration 2 where proactive replication is enabled and we verify correct behavior
                iterations: 3,
                storeToStartupLast: proactiveReplicator,
                testFunc: async context =>
                {
                    var sessions = context.Sessions;
                    var master = context.GetMasterIndex();

                    var ls = Enumerable.Range(0, storeCount).Select(n => context.GetLocationStore(n)).ToArray();
                    var lls = Enumerable.Range(0, storeCount).Select(n => context.GetLocalLocationStore(n)).ToArray();

                    if (context.Iteration == 0)
                    {
                        putResults = await Task.WhenAll(Enumerable.Range(0, putResults.Length)
                            .Select(_ => sessions[proactiveReplicator].PutRandomAsync(context, ContentHashType, false, ContentByteCount, Token).ShouldBeSuccess()));

                        foreach (var putResult in putResults)
                        {
                            // Content should be available in only one session, with proactive put set to false.
                            var masterResult = await ls[master].GetBulkAsync(context, new[] { putResult.ContentHash }, Token, UrgencyHint.Nominal, GetBulkOrigin.Local).ShouldBeSuccess();
                            masterResult.ContentHashesInfo[0].Locations.Count.Should().Be(1);
                            await ls[master].LocalLocationStore.CreateCheckpointAsync(context).ShouldBeSuccess();
                        }

                        TestClock.UtcNow += TimeSpan.FromMinutes(5);
                    }
                    else if (context.Iteration == 2)
                    {
                        var proactiveStore = context.GetDistributedStore(proactiveReplicator);
                        var proactiveSession = (await proactiveStore.ProactiveCopySession.Value).ThrowIfFailure();
                        var counters = proactiveSession.GetCounters().ToDictionaryIntegral();
                        counters["ProactiveCopy_OutsideRingFromPreferredLocations.Count"].Should().Be(usePreferredLocations ? proactiveReplicationCopyLimit : 0);

                        var copied = 0;
                        foreach (var putResult in putResults)
                        {
                            // Content should be available in two sessions, due to proactive replication in second iteration.
                            var masterResult = await ls[master].GetBulkAsync(context, new[] { putResult.ContentHash }, Token, UrgencyHint.Nominal, GetBulkOrigin.Global).ShouldBeSuccess();
                            if (masterResult.ContentHashesInfo[0].Locations.Count == 2)
                            {
                                copied++;
                            }
                        }
                        copied.Should().Be(proactiveReplicationCopyLimit);
                    }
                });
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public Task ProactiveCopyOnPinTest(bool usePreferredLocations)
        {
            EnableProactiveCopy = true;
            ProactiveCopyOnPuts = false;
            ProactiveCopyOnPins = true;
            ProactiveCopyUsePreferredLocations = usePreferredLocations;

            int batchSize = 5;

            ConfigureWithOneMaster(dcs =>
            {
                dcs.RestoreCheckpointAgeThresholdMinutes = 0;

                // There are two batches (one per iteration). So ensure proactive copy
                // triggers eagerly when doing both.
                dcs.ProactiveCopyGetBulkBatchSize = batchSize * 2;
                dcs.ProactiveCopyLocationsThreshold = 2;
            });

            List<ContentHash> putHashes = new List<ContentHash>();

            return RunTestAsync(
                storeCount: 3,
                iterations: 2,
                testFunc: async context =>
                {
                    var sessions = context.Sessions;
                    var workers = context.EnumerateWorkersIndices().ToList();
                    var master = context.GetMasterIndex();
                    var proactiveWorkerSession = context.GetDistributedSession(workers[0]);
                    var otherSession = context.GetSession(workers[1]);
                    var counters = proactiveWorkerSession.SessionCounters;

                    var ls = Enumerable.Range(0, 3).Select(n => context.GetLocationStore(n)).ToArray();
                    var lls = Enumerable.Range(0, 3).Select(n => context.GetLocalLocationStore(n)).ToArray();

                    async Task putInProactiveWorkerAsync()
                    {
                        var putResults = await Task.WhenAll(Enumerable.Range(0, batchSize)
                            .Select(i => proactiveWorkerSession.PutRandomAsync(context, ContentHashType, false, ContentByteCount, Token).ShouldBeSuccess()));
                        putHashes.AddRange(putResults.Select(p => p.ContentHash));
                    }

                    async Task ensureHashesInOtherWorkerAsync()
                    {
                        foreach (var hash in putHashes)
                        {
                            await otherSession.OpenStreamAsync(context, hash, Token).ShouldBeSuccess();
                        }
                    }

                    async Task ensureReplicasAsync(bool afterProactiveCopy)
                    {
                        var masterResult = await context.GetLocationStore(master)
                            .GetBulkAsync(context, putHashes, Token, UrgencyHint.Nominal, GetBulkOrigin.Local).ShouldBeSuccess();
                        masterResult.ContentHashesInfo[0].Locations.Count.Should().Be(2);

                        // Only hashes from second batch should be proactively copied after pin
                        counters[ProactiveCopy_OutsideRingFromPreferredLocations].Value.Should().Be((usePreferredLocations && afterProactiveCopy) ? batchSize : 0);
                    }

                    if (context.Iteration == 0)
                    {
                        await putInProactiveWorkerAsync();
                        await ensureHashesInOtherWorkerAsync();

                        await ls[master].LocalLocationStore.CreateCheckpointAsync(context).ShouldBeSuccess();

                        await ensureReplicasAsync(afterProactiveCopy: false);
                    }
                    if (context.Iteration == 1)
                    {
                        await putInProactiveWorkerAsync();

                        var pinResult = await proactiveWorkerSession.PinAsync(context, putHashes, Token);

                        await ensureReplicasAsync(afterProactiveCopy: true);
                    }
                });
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public Task ProactivePutTest(bool usePreferredLocations)
        {
            EnableProactiveCopy = true;
            ProactiveCopyOnPuts = true;
            ProactiveCopyUsePreferredLocations = usePreferredLocations;

            ConfigureWithOneMaster(dcs =>
            {
                dcs.RestoreCheckpointAgeThresholdMinutes = 0;
            });

            PutResult putResult = default;

            return RunTestAsync(
                storeCount: 3,
                iterations: 2,
                testFunc: async context =>
                {
                    var sessions = context.Sessions;
                    var master = context.GetMasterIndex();

                    var ls = Enumerable.Range(0, 3).Select(n => context.GetLocationStore(n)).ToArray();
                    var lls = Enumerable.Range(0, 3).Select(n => context.GetLocalLocationStore(n)).ToArray();

                    if (context.Iteration == 0)
                    {
                        // Put into master to ensure it has something to checkpoint
                        await sessions[master].PutRandomAsync(context, ContentHashType, false, ContentByteCount, Token).ShouldBeSuccess();
                        await ls[master].LocalLocationStore.CreateCheckpointAsync(context).ShouldBeSuccess();
                    }
                    if (context.Iteration == 1)
                    {
                        putResult = await sessions[1].PutRandomAsync(context, ContentHashType, false, ContentByteCount, Token).ShouldBeSuccess();

                        // Content should be available in two sessions, because of proactive put.
                        var masterResult = await ls[master].GetBulkAsync(context, new[] { putResult.ContentHash }, Token, UrgencyHint.Nominal, GetBulkOrigin.Local).ShouldBeSuccess();
                        masterResult.ContentHashesInfo[0].Locations.Count.Should().Be(2);

                        var proactiveSession = context.GetDistributedSession(1);
                        var counters = proactiveSession.GetCounters().ToDictionaryIntegral();
                        counters["ProactiveCopy_OutsideRingFromPreferredLocations.Count"].Should().Be(usePreferredLocations ? 1 : 0);
                    }
                });
        }

        [Fact]
        public Task ProactiveCopyEvictionRejectionTest()
        {
            EnableProactiveReplication = false;
            EnableProactiveCopy = true;
            ProactiveCopyOnPuts = false;
            UseGrpcServer = true;

            ConfigureWithOneMaster(dcs => dcs.TouchFrequencyMinutes = 1);

            var largeFileSize = Config.MaxSizeQuota.Hard / 2 + 1;

            return RunTestAsync(
                storeCount: 2,
                iterations: 1,
                implicitPin: ImplicitPin.None,
                testFunc: async context =>
                {
                    var session0 = context.GetDistributedSession(0);
                    var store0 = (DistributedContentStore)context.GetDistributedStore(0);

                    var session1 = context.GetSession(1);
                    var store1 = (DistributedContentStore)context.GetDistributedStore(1);

                    var putResult0 = await session0.PutRandomAsync(context, HashType.MD5, provideHash: false, size: largeFileSize, CancellationToken.None);
                    var oldHash = putResult0.ContentHash;

                    TestClock.Increment();

                    // Put a large file.
                    var putResult = await session1.PutRandomAsync(context, HashType.MD5, provideHash: false, size: largeFileSize, CancellationToken.None);

                    await UploadCheckpointOnMasterAndRestoreOnWorkers(context);

                    // Put another large file, which should trigger eviction.
                    // Last eviction should be newer than last access time of the old hash.
                    var putResult2 = await session1.PutRandomAsync(context, HashType.MD5, provideHash: false, size: largeFileSize, CancellationToken.None);

                    store1.CounterCollection[DistributedContentStore.Counters.RejectedPushCopyCount_OlderThanEvicted].Value.Should().Be(0);
                    var result = await session0.ProactiveCopyIfNeededAsync(context, oldHash, tryBuildRing: false, CopyReason.ProactiveBackground);
                    result.Succeeded.Should().BeFalse();
                    store1.CounterCollection[DistributedContentStore.Counters.RejectedPushCopyCount_OlderThanEvicted].Value.Should().Be(1);

                    TestClock.UtcNow += TimeSpan.FromMinutes(2); // Need to increase to make checkpoints happen.

                    // Bump last access time.
                    await session0.PinAsync(context, oldHash, CancellationToken.None).ShouldBeSuccess();

                    await UploadCheckpointOnMasterAndRestoreOnWorkers(context);

                    // Copy should not be rejected.
                    store1.CounterCollection[DistributedContentStore.Counters.RejectedPushCopyCount_OlderThanEvicted].Value.Should().Be(1);
                    await session0.ProactiveCopyIfNeededAsync(context, oldHash, tryBuildRing: false, CopyReason.ProactiveBackground).ShouldBeSuccess();
                    store1.CounterCollection[DistributedContentStore.Counters.RejectedPushCopyCount_OlderThanEvicted].Value.Should().Be(1);
                });
        }

        [Fact]
        public async Task ProactiveCopyWithTooManyRequestsTest()
        {
            // This test adds a lot of files in a session (without proactive copies on put),
            // and then explicitly forces a push to another session in parallel.
            // The test expects that some of the operations would be rejected (even though the push itself is considered successful).
            EnableProactiveReplication = false;
            EnableProactiveCopy = true;
            ProactiveCopyOnPuts = false;
            UseGrpcServer = true;
            ConfigureWithOneMaster(dcs => dcs.TouchFrequencyMinutes = 1);
            // Limiting the concurrency and expecting that at least some of them will fail.
            ProactivePushCountLimit = 1;
            await runTestAsync(count: 100, expectAllSuccesses: false);
            // Increasing the concurrency to the number of operations. All the pushes should be successful and accepted.
            ProactivePushCountLimit = 100;
            await runTestAsync(count: 100, expectAllSuccesses: true);
            Task runTestAsync(int count, bool expectAllSuccesses)
            {
                return RunTestAsync(
                    storeCount: 2,
                    iterations: 1,
                    implicitPin: ImplicitPin.None,
                    testFunc: async context =>
                    {
                        var session0 = context.GetDistributedSession(0);
                        var putResults = new List<PutResult>();
                        for (int i = 0; i < count; i++)
                        {
                            putResults.Add(
                                await session0.PutRandomAsync(context, HashType.MD5, provideHash: false, size: 31, CancellationToken.None));
                        }
                        TestClock.Increment();
                        var tasks = putResults.Select(
                            async pr =>
                            {
                                var result = await session0.ProactiveCopyIfNeededAsync(
                                    context,
                                    pr.ContentHash,
                                    tryBuildRing: false,
                                    CopyReason.ProactiveBackground);
                                return result.OutsideRingCopyResult;
                            });
                        var results = await Task.WhenAll(tasks);
                        // We should have at least some skipped operations, because we tried 100 pushes at the same time with 1 as the push count limit.
                        var acceptedSuccesses = results.Count(r => r.Status == CopyResultCode.Success);
                        if (expectAllSuccesses)
                        {
                            acceptedSuccesses.Should().Be(count);
                        }
                        else
                        {
                            acceptedSuccesses.Should().NotBe(count);
                        }
                    });
            }
        }

        [Fact]
        public Task ProactiveCopyInsideRingRetriesTest()
        {
            EnableProactiveCopy = true;
            ProactiveCopyMode = ProactiveCopyMode.InsideRing; // Disable outside-ring proactive copies.
            ProactiveCopyOnPuts = true;
            ProactiveCopyOnPins = true;
            ProactiveCopyLocationThreshold = 4; // Large enough that we 'always' try to push.
            ProactiveCopyRetries = 2;

            var contentHashes = new List<ContentHash>();

            int machineCount = 3;
            ConfigureWithOneMaster();

            var buildId = Guid.NewGuid().ToString();

            return RunTestAsync(
                machineCount,
                async context =>
                {
                    var masterStore = context.GetMaster();
                    var defaultFileSize = (Config.MaxSizeQuota.Hard / 4) + 1;

                    var sessions = context.GetDistributedSessions();

                    // Insert random file #1 into worker #1
                    var putResult = await sessions[0].PutRandomAsync(context, HashType.Vso0, false, defaultFileSize, Token).ShouldBeSuccess();
                    var hash = putResult.ContentHash;

                    var getBulkResult = await masterStore.GetBulkAsync(context, hash, GetBulkOrigin.Global).ShouldBeSuccess();

                    // Proactive copy should have replicated the content.
                    getBulkResult.ContentHashesInfo[0].Locations.Count.Should().Be(2);

                    var counters = sessions[0].GetCounters().ToDictionaryIntegral();
                    counters["ProactiveCopyInsideRingRetries.Count"].Should().Be(ProactiveCopyRetries);
                    counters["ProactiveCopyRetries.Count"].Should().Be(ProactiveCopyRetries);
                },
                implicitPin: ImplicitPin.None,
                buildId: buildId,
                testCopier: new ErrorReturningTestFileCopier(errorsToReturn: ProactiveCopyRetries, failingResult: PushFileResult.ServerUnavailable()));
        }
    }
}
