// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.Collections;
using ContentStoreTest.Test;
using Xunit;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace ContentStoreTest.Distributed.Sessions
{
    public partial class LocalLocationStoreDistributedContentTests
    {
        [Fact(Skip = "For manual testing only")]
        public async Task TestDifferentCounts()
        {
            string databasePath = @"C:\Temp\Checkpoint\";

            var countWithSetTotalOrderSeek = await getContentOnMachine(new MachineId(121), setTotalOrderSeek: true);
            var countWithoutSetTotalOrderSeek = await getContentOnMachine(new MachineId(121), setTotalOrderSeek: false);
            // Content count with SetTotalOrderSeek: 4193451, without: 4231080
            Output.WriteLine($"Content count with SetTotalOrderSeek: {countWithSetTotalOrderSeek}, without: {countWithoutSetTotalOrderSeek}");

            async Task<int> getContentOnMachine(MachineId machineId, bool setTotalOrderSeek)
            {
                var db = new RocksDbContentLocationDatabase(TestClock, new RocksDbContentLocationDatabaseConfiguration(new AbsolutePath(databasePath))
                                                                       {
                                                                           CleanOnInitialize = false,
                                                                           Epoch = "DM_S220201001ReconcileTest.03312020.0",
                                                                           UseReadOptionsWithSetTotalOrderSeekInDbEnumeration = setTotalOrderSeek,
                }, () => CollectionUtilities.EmptyArray<MachineId>());

                var context = new OperationContext(new Context(Logger));
                await db.StartupAsync(context).ThrowIfFailure();

                var count = db.EnumerateSortedHashesWithContentSizeForMachineId(context, currentMachineId: new MachineId(121)).Count();

                await db.ShutdownAsync(context).ThrowIfFailure();
                return count;
            }
        }

        [Fact(Skip = "For manual testing only")]
        public async Task TestMissingHashes()
        {
            // This test demonstrates that some hashes are missing when we enumerate without setting SetTotalOrderSeek.
            var missingHashes = new string[]{
                                         "VSO0:004206D0C9111C8494CBAC",
                                         "VSO0:004214AD89057251C75AB4",
                                         "VSO0:0042213A319603AEADDACA",
                                         "VSO0:00422E765196DDCEC5DDCB",
                                         "VSO0:00424E130937109ACC9FE1",
                                         "VSO0:00425A5011BC79F9A7B051",
                                         "VSO0:0042674A7B2782F0F4BEE4",
                                         "VSO0:004268BF4380D7CEA4786E",
                                         "VSO0:00426D1DAA182A6D265461",
                                         "VSO0:0042866D19EB33B8E11C8E",
                                     }.Select(str => ParseShortHash(str)).ToHashSet();

            var maxHash = missingHashes.Max();
            string databasePath = @"C:\Temp\Checkpoint\";

            var hashesWithoutSetTotalOrderSeek = await getContentOnMachine(new MachineId(121), setTotalOrderSeek: false);
            var hashesWithSetTotalOrderSeek = await getContentOnMachine(new MachineId(121), setTotalOrderSeek: true);

            foreach (var h in missingHashes)
            {
                // Hash: VSO0: 0042866D19EB33B8E11C, Contains(TotalOrderSeek = True): True, Contains(TotalOrderSeek = false): False
                bool db1Contains = hashesWithSetTotalOrderSeek.Contains(h);
                bool db2Contains = hashesWithoutSetTotalOrderSeek.Contains(h);
                Output.WriteLine($"Hash: {h}, Contains (TotalOrderSeek=True): {db1Contains}, Contains (TotalOrderSeek=false): {db2Contains}");
            }
            
            Output.WriteLine($"Content count with SetTotalOrderSeek: {hashesWithSetTotalOrderSeek.Count}, without: {hashesWithoutSetTotalOrderSeek.Count}");

            async Task<HashSet<ShortHash>> getContentOnMachine(MachineId machineId, bool setTotalOrderSeek)
            {
                var db = new RocksDbContentLocationDatabase(TestClock, new RocksDbContentLocationDatabaseConfiguration(new AbsolutePath(databasePath))
                                                                       {
                                                                           CleanOnInitialize = false,
                                                                           Epoch = "DM_S220201001ReconcileTest.03312020.0",
                                                                           UseReadOptionsWithSetTotalOrderSeekInDbEnumeration = setTotalOrderSeek,
                                                                       }, () => CollectionUtilities.EmptyArray<MachineId>());

                var context = new OperationContext(new Context(Logger));
                await db.StartupAsync(context).ThrowIfFailure();

                var hashes = db.EnumerateSortedHashesWithContentSizeForMachineId(context, currentMachineId: new MachineId(121))
                    .TakeWhile(h => h.hash < maxHash || h.hash == maxHash).Select(h => h.hash).ToHashSet();

                await db.ShutdownAsync(context).ThrowIfFailure();
                return hashes;
            }
        }

        [Fact(Skip = "For manual testing only")]
        public async Task MimicReconciliationWithFullDatabaseEnumerationByKeys()
        {

            // This is an original and slower version of reconciliation.
            string databasePath = @"C:\Temp\Checkpoint\";
            var db = new RocksDbContentLocationDatabase(TestClock, new RocksDbContentLocationDatabaseConfiguration(new AbsolutePath(databasePath))
            {
                CleanOnInitialize = false
            }, () => CollectionUtilities.EmptyArray<MachineId>());


            var context = new OperationContext(new Context(Logger));
            await db.StartupAsync(context).ThrowIfFailure();

            var sw = Stopwatch.StartNew();
            var reconcile = MimicOldReconcile(context, machineId: 121, db);
            sw.Stop();

            Output.WriteLine($"Reconcile by {sw.ElapsedMilliseconds}ms. Added: {reconcile.addedContent.Count}, removed: {reconcile.removedContent.Count}");
        }

        [Fact(Skip = "For manual testing only")]
        public async Task MimicReconcileWithMachineIdFilteringWithNoDeserialization()
        {
            string databasePath = @"C:\Temp\Checkpoint\";
            var db = new RocksDbContentLocationDatabase(TestClock, new RocksDbContentLocationDatabaseConfiguration(new AbsolutePath(databasePath))
            {
                CleanOnInitialize = false,
                Epoch = "DM_S220201001ReconcileTest.03312020.0"
            }, () => CollectionUtilities.EmptyArray<MachineId>());

            var context = new OperationContext(new Context(Logger));
            await db.StartupAsync(context).ThrowIfFailure();

            var sw = Stopwatch.StartNew();
            var reconcile = MimicFastReconcileLogic(context, machineId: 121, db);
            sw.Stop();

            Output.WriteLine($"Reconcile by {sw.ElapsedMilliseconds}ms. Added: {reconcile.addedContent.Count}, removed: {reconcile.removedContent.Count}");
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

        private static ShortHash ParseShortHash(string str)
        {
            var longHash = str.PadRight(ContentHash.SerializedLength * 2 + 3, '0');
            if (ContentHash.TryParse(longHash, out var result))
            {
                return new ShortHash(result);
            }

            throw new InvalidOperationException($"Can't create a hash from " + str);
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
