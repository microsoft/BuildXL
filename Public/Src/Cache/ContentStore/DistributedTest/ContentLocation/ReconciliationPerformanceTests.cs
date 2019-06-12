// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.Collections;
using ContentStoreTest.Distributed.Redis;
using Xunit;
using Xunit.Abstractions;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace ContentStoreTest.Distributed.Sessions
{
    [Trait("Category", "Integration")]
    [Trait("Category", "LongRunningTest")]
    [Collection("Redis-based tests")]
    public class ReconciliationPerformanceTests : LocalLocationStoreDistributedContentTests
    {
        /// <nodoc />
        public ReconciliationPerformanceTests(LocalRedisFixture redis, ITestOutputHelper output)
            : base(redis, output)
        {
        }

        [Fact(Skip = "For manual testing only")]
        public async Task MimicReconciliationWithFullDatabaseEnumerationByKeys()
        {
            // This is an original and slower version of reconciliation.
            string databasePath = @"C:\Users\seteplia\AppData\Local\Temp\CloudStore\DatabaseTest\";
            var db = new RocksDbContentLocationDatabase(TestClock, new RocksDbContentLocationDatabaseConfiguration(new AbsolutePath(databasePath))
            {
                CleanOnInitialize = false
            }, () => CollectionUtilities.EmptyArray<MachineId>());
            

            var context = new OperationContext(new Context(Logger));
            await db.StartupAsync(context).ThrowIfFailure();

            var sw = Stopwatch.StartNew();
            var reconcile = MimicOldReconcile(context, machineId: 42, db);
            sw.Stop();

            Output.WriteLine($"Reconcile by {sw.ElapsedMilliseconds}ms. Added: {reconcile.addedContent.Count}, removed: {reconcile.removedContent.Count}");
        }

        [Fact(Skip = "For manual testing only")]
        public async Task MimicReconcileWithMachineIdFilteringWithNoDeserialization()
        {
            string databasePath = @"C:\Users\seteplia\AppData\Local\Temp\CloudStore\DatabaseTest\";
            var db = new RocksDbContentLocationDatabase(TestClock, new RocksDbContentLocationDatabaseConfiguration(new AbsolutePath(databasePath))
            {
                CleanOnInitialize = false
            }, () => CollectionUtilities.EmptyArray<MachineId>());

            var context = new OperationContext(new Context(Logger));
            await db.StartupAsync(context).ThrowIfFailure();

            var sw = Stopwatch.StartNew();
            var reconcile = MimicFastReconcileLogic(context, machineId: 42, db);
            sw.Stop();

            Output.WriteLine($"Reconcile by {sw.ElapsedMilliseconds}ms. Added: {reconcile.addedContent.Count}, removed: {reconcile.removedContent.Count}");
        }

        [Fact(Skip = "For manual testing only")]
        public async Task ReconciliationOverRealStorage()
        {
            _enableReconciliation = true;
            var checkpointsKey = Guid.NewGuid().ToString();
            // Copy and paste a real connection string here.
            var storageConnectionString = string.Empty;
            // Consider updating this directory if you want to keep data between invocations.
            var workingDirectory = TestRootDirectoryPath;
            var configuration = new LocalDiskCentralStoreConfiguration(
                workingDirectory,
                checkpointsKey);
            var blobStoreConfiguration = new BlobCentralStoreConfiguration(
                connectionString: storageConnectionString,
                containerName: "checkpoints",
                checkpointsKey: checkpointsKey);

            ConfigureWithOneMaster(blobStoreConfiguration);

            await RunTestAsync(
                new Context(Logger),
                2,
                async context =>
                {
                    var master = context.GetMaster();
                    var worker = context.GetFirstWorker();
                    var workerId = worker.LocalLocationStore.LocalMachineId;

                    var workerSession = context.Sessions[context.GetFirstWorkerIndex()];

                    var checkpointState = new CheckpointState(
                        Role.Worker,
                        EventSequencePoint.Parse("24382354"),
                        "MD5:8C4856EA13F6AD59B65D8F6781D2A2F9||DCS||incrementalCheckpoints/24382354.10a0ca0f-d63f-4992-a088-f67bd00abd8a.checkpointInfo.txt|Incremental",
                        DateTime.Now);
                    // Next heartbeat workers to restore checkpoint
                    await worker.LocalLocationStore.RestoreCheckpointAsync(new OperationContext(context), checkpointState, inline: true, forceRestore: true).ShouldBeSuccess();
                    var reconcileResult = await worker.LocalLocationStore.ReconcileAsync(context).ShouldBeSuccess();
                    Output.WriteLine($"Reconcile result: {reconcileResult}");
                });
        }

        private static IEnumerable<(ShortHash hash, long size)> GetSortedDatabaseEntriesWithLocalLocationOld(OperationContext context, RocksDbContentLocationDatabase db, int index)
        {
            // Originally, this was db.EnumerateSortedKeys(context), but that method is since private. This is left
            // here in case further work is required in the future.
            foreach (var hash in new ShortHash[] { })
            {
                if (db.TryGetEntry(context, hash, out var entry))
                {
                    if (entry.Locations[index])
                    {
                        // Entry is present on the local machine
                        yield return (hash, entry.ContentSize);
                    }
                }
            }
        }

        private static (List<ShortHash> removedContent, List<ShortHashWithSize> addedContent) MimicOldReconcile(OperationContext context, int machineId, RocksDbContentLocationDatabase db)
        {
            var dbContent = GetSortedDatabaseEntriesWithLocalLocationOld(context, db, machineId);

            // Diff the two views of the local machines content (left = local store, right = content location db)
            // Then send changes as events
            (ShortHash hash, long size)[] allLocalStoreContent = new (ShortHash hash, long size)[0];
            var diffedContent = NuCacheCollectionUtilities.DistinctDiffSorted(leftItems: allLocalStoreContent, rightItems: dbContent, t => t.hash);

            var addedContent = new List<ShortHashWithSize>();
            var removedContent = new List<ShortHash>();

            foreach (var diffItem in diffedContent)
            {
                if (diffItem.mode == MergeMode.LeftOnly)
                {
                    // Content is not in DB but is in the local store need to send add event
                    addedContent.Add(new ShortHashWithSize(diffItem.item.hash, diffItem.item.size));
                }
                else
                {
                    // Content is in DB but is not local store need to send remove event
                    removedContent.Add(diffItem.item.hash);
                }
            }

            return (removedContent, addedContent);
        }


        private static (List<ShortHash> removedContent, List<ShortHashWithSize> addedContent) MimicFastReconcileLogic(OperationContext context, int machineId, RocksDbContentLocationDatabase db)
        {
            var dbContent = db.EnumerateSortedHashesWithContentSizeForMachineId(context, new MachineId(machineId));

            // Diff the two views of the local machines content (left = local store, right = content location db)
            // Then send changes as events
            (ShortHash hash, long size)[] allLocalStoreContent = new (ShortHash hash, long size)[0];
            var diffedContent = NuCacheCollectionUtilities.DistinctDiffSorted(leftItems: allLocalStoreContent, rightItems: dbContent, t => t.hash);

            var addedContent = new List<ShortHashWithSize>();
            var removedContent = new List<ShortHash>();

            foreach (var diffItem in diffedContent)
            {
                if (diffItem.mode == MergeMode.LeftOnly)
                {
                    // Content is not in DB but is in the local store need to send add event
                    addedContent.Add(new ShortHashWithSize(diffItem.item.hash, diffItem.item.size));
                }
                else
                {
                    // Content is in DB but is not local store need to send remove event
                    removedContent.Add(diffItem.item.hash);
                }
            }

            return (removedContent, addedContent);
        }
    }
}
