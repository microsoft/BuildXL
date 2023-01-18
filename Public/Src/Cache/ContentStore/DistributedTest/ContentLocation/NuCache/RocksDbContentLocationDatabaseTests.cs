using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Distributed.ContentLocation.NuCache
{
    // TODO: all fail with
    // System.ArgumentException : The reader should have at least 1 length but has 0.
    // Stack Trace:
    //  at BuildXL.Utilities.Serialization.SpanReader.ThrowArgumentException(Int32 minLength) in \.\Public\Src\Utilities\Utilities\Serialization\SpanReader.cs:line 100

    [Trait("Category", "WindowsOSOnly")]
    public class RocksDbContentLocationDatabaseTests : TestBase
    {
        protected readonly MemoryClock Clock = new MemoryClock();

        protected readonly DisposableDirectory _workingDirectory;

        protected RocksDbContentLocationDatabaseConfiguration DefaultConfiguration { get; }

        public RocksDbContentLocationDatabaseTests(ITestOutputHelper output = null)
            : base(TestGlobal.Logger, output)
        {
            // Need to use unique folder for each test instance, because more then one test may be executed simultaneously.
            var uniqueOutputFolder = TestRootDirectoryPath / Guid.NewGuid().ToString();
            _workingDirectory = new DisposableDirectory(FileSystem, uniqueOutputFolder);
        }

        private static void ValidateMergeCount(bool useSortedMerge, CounterCollection<ContentLocationDatabaseCounters> c, int expectedCount)
        {
            if (useSortedMerge)
            {
                if (expectedCount > 0)
                {
                    c[ContentLocationDatabaseCounters.MergeEntrySorted].Value.Should().Be(expectedCount);
                }
            }

            // MergeEntry is the counter used for both merging sorted and un-softed entries.
            c[ContentLocationDatabaseCounters.MergeEntry].Value.Should().Be(expectedCount);
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, false)]
        public Task TestCheckpoint(bool useMerge, bool useSortedMerge)
        {
            // Touch should update the last access time but should keep the creation time as is.
            return TestDatabase(
                (context, db) =>
                {
                    var hash = ContentHash.Random().ToShortHash();
                    var firstMachineId = new MachineId(1);
                    db.LocationAdded(context, hash, firstMachineId, size: 42).Should().BeTrue();

                    var creationTime = Clock.UtcNow;
                    var secondMachineId = new MachineId(2);
                    db.LocationAdded(context, hash, secondMachineId, size: 42).Should().BeTrue();

                    Clock.UtcNow += TimeSpan.FromSeconds(42);

                    var touchTime = Clock.UtcNow;
                    db.ContentTouched(context, hash, touchTime);

                    var checkpointPath = _workingDirectory.Path / $"Chkpt{useMerge}";

                    // No merges should happen yet.
                    db.Counters[ContentLocationDatabaseCounters.MergeEntry].Value.Should().Be(0);

                    db.SaveCheckpoint(context, checkpointPath).ShouldBeSuccess();

                    // Should be two merges: (add + add) + touch
                    // Merge is happening when the checkpoint is saved.
                    if (useMerge)
                    {
                        ValidateMergeCount(useSortedMerge, db.Counters, 2);
                    }

                    db.RestoreCheckpoint(context, checkpointPath).ShouldBeSuccess();

                    db.TryGetEntry(context, hash, out var entry).Should().BeTrue();

                    entry.Locations.Count.Should().Be(2);
                    entry.LastAccessTimeUtc.Should().Be(touchTime);
                    entry.CreationTimeUtc.Should().Be(creationTime);
                }, useMerge, useSortedMerge);
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, false)]
        public Task TestAddAndTouch(bool useMerge, bool useSortedMerge)
        {
            // Touch should update the last access time but should keep the creation time as is.
            return TestDatabase(
                (context, db) =>
                {
                    var hash = ContentHash.Random().ToShortHash();
                    var firstMachineId = new MachineId(1);
                    db.LocationAdded(context, hash, firstMachineId, size: 42).Should().BeTrue();

                    var creationTime = Clock.UtcNow;
                    var secondMachineId = new MachineId(2);
                    db.LocationAdded(context, hash, secondMachineId, size: 42).Should().BeTrue();

                    Clock.UtcNow += TimeSpan.FromSeconds(42);

                    var touchTime = Clock.UtcNow;
                    db.ContentTouched(context, hash, touchTime);

                    db.TryGetEntry(context, hash, out var entry).Should().BeTrue();

                    entry.Locations.Count.Should().Be(2);
                    entry.LastAccessTimeUtc.Should().Be(touchTime);
                    entry.CreationTimeUtc.Should().Be(creationTime);

                    // Should be two merges: (add + add) + touch
                    if (useMerge)
                    {
                        ValidateMergeCount(useSortedMerge, db.Counters, 2);
                    }

                }, useMerge, useSortedMerge);
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, false)]
        public Task TestAddAndRemove(bool useMerge, bool useSortedMerge)
        {
            return TestDatabase(
                (context, db) =>
                {
                    // Adding two locations
                    var hash = new ShortHash(ContentHash.Random());
                    var firstMachineId = new MachineId(1);
                    db.LocationAdded(context, hash, firstMachineId, size: 42).Should().BeTrue();

                    var secondMachineId = new MachineId(2);
                    db.LocationAdded(context, hash, secondMachineId, size: 42).Should().BeTrue();

                    // Should have both of them available
                    db.TryGetEntry(context, hash, out var entry).Should().BeTrue();
                    entry.Locations.Count.Should().Be(2, "Should have two locations");

                    // Removing one
                    db.LocationRemoved(context, hash, firstMachineId);

                    // Should have one now
                    db.TryGetEntry(context, hash, out entry).Should().BeTrue();
                    entry.Locations.Count.Should().Be(1, "Should have only 1 location");

                    if (useMerge)
                    {
                        ValidateMergeCount(useSortedMerge, db.Counters, 3);
                    }
                }, useMerge, useSortedMerge);
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, false)]
        public Task TestMultipleAddsAndRemovals(bool useMerge, bool useSortedMerge)
        {
            return TestDatabase(
                (context, db) =>
                {
                    // Adding two locations
                    var hash = ContentHash.Random().ToShortHash();
                    db.LocationAdded(context, hash, 1.AsMachineId(), size: 42).Should().BeTrue();
                    db.LocationAdded(context, hash, 2.AsMachineId(), size: 42).Should().BeTrue();
                    db.LocationRemoved(context, hash, 3.AsMachineId()).Should().Be(useMerge);
                    db.LocationRemoved(context, hash, 4.AsMachineId()).Should().Be(useMerge);

                    // Should have both of them available
                    db.TryGetEntry(context, hash, out var entry).Should().BeTrue();
                    entry.Locations.Count.Should().Be(2, "Should have two locations");

                    db.LocationRemoved(context, hash, 2.AsMachineId()).Should().BeTrue();

                    // Just a new location on top of the removal
                    db.LocationAdded(context, hash, 3.AsMachineId(), size: 42).Should().BeTrue();

                    // Add, Remove
                    db.LocationAdded(context, hash, 4.AsMachineId(), size: 42).Should().BeTrue();
                    db.LocationRemoved(context, hash, 4.AsMachineId()).Should().BeTrue();

                    // Add, Remove, Add
                    db.LocationAdded(context, hash, 5.AsMachineId(), size: 42).Should().BeTrue();
                    db.LocationRemoved(context, hash, 5.AsMachineId()).Should().BeTrue();
                    db.LocationAdded(context, hash, 5.AsMachineId(), size: 42).Should().BeTrue();

                    // Final state:
                    // Add(1), Add(3), Add(5)

                    // Should have one now
                    db.TryGetEntry(context, hash, out entry).Should().BeTrue();
                    entry.Locations.Count.Should().Be(3, "Should have only 1 location");

                    entry.Locations[1.AsMachineId()].Should().BeTrue();
                    entry.Locations[3.AsMachineId()].Should().BeTrue();
                    entry.Locations[5.AsMachineId()].Should().BeTrue();
                }, useMerge, useSortedMerge);
        }

        [Theory]
        [InlineData(1, true)]
        [InlineData(2, true)]
        [InlineData(10, true)]
        [InlineData(256, true)]
        [InlineData(1, false)]
        [InlineData(2, false)]
        [InlineData(10, false)]
        [InlineData(256, false)]
        public Task TestMergeAdditions(int locationCount, bool useSortedMerge)
        {
            // Checks multiple additions
            return TestDatabase(
                (context, db) =>
                {
                    var hash = ContentHash.Random().ToShortHash();
                    var locations = Enumerable.Range(1, locationCount).Select(n => new MachineId(n)).ToList();

                    foreach (var location in locations)
                    {
                        db.LocationAdded(context, hash, location, size: 42).Should().BeTrue();
                    }

                    db.TryGetEntry(context, hash, out var entry).Should().BeTrue();

                    entry.Locations.Count.Should().Be(locationCount, $"Should have {locationCount} locations");
                    foreach (var location in locations)
                    {
                        entry.Locations.Contains(location).Should().BeTrue();
                    }

                    ValidateMergeCount(useSortedMerge, db.Counters, locationCount - 1);
                },
                useSortedMerge: useSortedMerge);
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, false)]
        public Task TestAdd(bool useMerge, bool useSortedMerge)
        {
            // This test covers adding two locations in full details:
            // Checks the location count and the machine existence

            return TestDatabase(
                (context, db) =>
                {
                    var hash = ContentHash.Random().ToShortHash();
                    var location1 = 1.AsMachineId();
                    var location2 = 2.AsMachineId();

                    var creationTime = Clock.UtcNow;

                    // Adding the same location multiple times
                    db.LocationAdded(context, hash, location1, size: 42).Should().BeTrue();
                    Clock.UtcNow += TimeSpan.FromSeconds(10);

                    db.LocationAdded(context, hash, location1, size: 42).Should().BeTrue();
                    db.LocationAdded(context, hash, location1, size: 42).Should().Be(useMerge, "The operation should always return true when merge is on, and false when merge is off.");

                    Clock.UtcNow += TimeSpan.FromSeconds(100);
                    var lastAccessTime = Clock.UtcNow;

                    db.LocationAdded(context, hash, location2, size: 42).Should().BeTrue();
                    db.LocationAdded(context, hash, location2, size: 42).Should().Be(useMerge);
                    db.LocationAdded(context, hash, location2, size: 42).Should().Be(useMerge);

                    db.TryGetEntry(context, hash, out var entry).Should().BeTrue();

                    entry.CreationTimeUtc.Should().Be(creationTime.ToUnixTime());

                    entry.Locations.Count.Should().Be(2, "Should have 2 locations");
                    foreach (var location in new List<MachineId>() { location1, location2 })
                    {
                        entry.Locations.Contains(location).Should().BeTrue();
                    }

                    entry.LastAccessTimeUtc.Should().Be(lastAccessTime.ToUnixTime());

                    // We'll merge multiple times here.
                    if (useMerge)
                    {
                        ValidateMergeCount(useSortedMerge, db.Counters, 5);
                    }
                }, useMerge, useSortedMerge);
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, false)]
        public Task GarbageCollectionShouldRemoveEntry(bool useMerge, bool useSortedMerge)
        {
            // This test covers adding two locations in full details:
            // Checks the location count and the machine existence
            return TestDatabase(
                (context, db) =>
                {
                    var hash = ContentHash.Random().ToShortHash();
                    var location1 = 1.AsMachineId();

                    db.LocationAdded(context, hash, location1, size: 42).Should().BeTrue();

                    db.TryGetEntry(context, hash, out var entry).Should().BeTrue();

                    entry.Locations.Count.Should().Be(1);
                    db.LocationRemoved(context, hash, location1);

                    // if merge is used, then the entry should still be available but with 0 locations.
                    // for the normal mode TryGetEntry should return false.
                    db.TryGetEntry(context, hash, out entry).Should().Be(useMerge);
                    if (useMerge)
                    {
                        entry.Should().NotBeNull();
                        entry.Locations.Count.Should().Be(0);
                    }

                    db.GarbageCollectAsync(context).GetAwaiter().GetResult().ShouldBeSuccess();

                    // The entry should be gone for sure in both modes after the GC is done.
                    db.TryGetEntry(context, hash, out entry).Should().BeFalse();
                }, useMerge, useSortedMerge);
        }

        private async Task<CounterCollection<ContentLocationDatabaseCounters>> TestDatabase(
            Action<OperationContext, RocksDbContentLocationDatabase> test, bool useMergeOperators = true, bool useSortedMerge = true)
        {
            var path = _workingDirectory.Path;
            if (useMergeOperators)
            {
                path /= "Merge";
            }

            var configuration = new RocksDbContentLocationDatabaseConfiguration(path)
                                {
                                    CleanOnInitialize = true,
                                    UseMergeOperatorForContentLocations = useMergeOperators,
                                    SortMergeableContentLocations = useSortedMerge,
                                    TraceOperations = false,
                                };

            var tracingContext = new Context(Logger);
            var context = new OperationContext(tracingContext);

            var db = new RocksDbContentLocationDatabase(Clock, configuration, () => new MachineId[] { });

            try
            {
                await db.StartupAsync(context).ShouldBeSuccess();
                await db.SetDatabaseModeAsync(isDatabaseWriteable: true);
                test(context, db);
                return db.Counters;
            }
            finally
            {
                context.TracingContext.Debug("About to shutdown", "asdf");
                await db.ShutdownAsync(context).ShouldBeSuccess();
            }
        }
        
        [Fact]
        public async Task DoesNotAllowWritingWhenInReadOnlyMode()
        {
            var configuration = new RocksDbContentLocationDatabaseConfiguration(_workingDirectory.Path)
            {
                CleanOnInitialize = false,
            };

            var context = new Context(Logger);
            var ctx = new OperationContext(context);

            // First, we create the database
            {
                var db = new RocksDbContentLocationDatabase(Clock, configuration, () => new MachineId[] { });
                await db.StartupAsync(ctx).ShouldBeSuccess();
                db.SetGlobalEntry("test", "hello");
                await db.ShutdownAsync(ctx).ShouldBeSuccess();
            }

            configuration.OpenReadOnly = true;
            {
                var db = new RocksDbContentLocationDatabase(Clock, configuration, () => new MachineId[] { });
                await db.StartupAsync(ctx).ShouldBeSuccess();

                db.TryGetGlobalEntry("test", out var readValue);
                readValue.Should().Be("hello");

                Assert.Throws<BuildXLException>(() =>
                {
                    db.SetGlobalEntry("test", "hello2");
                });

                await db.ShutdownAsync(ctx).ShouldBeSuccess();
            }
        }

        // [Fact]
        [Fact(Skip = "For manual testing only")]
        public async Task PerformanceTest()
        {
            System.Diagnostics.Debugger.Launch();

            // This test tries to mimic the real load and the real distribution of various events.
            // Normally, roughly half of the events are adds, and 1/4-th are touches and the removes.

            int hashCount = 10_000;
            int eventCount = 1_000_000;
            var hashes = Enumerable.Range(1, hashCount).Select(h => ContentHash.Random().ToShortHash()).ToList();
            var machines = Enumerable.Range(1, 1000).Select(i => i.AsMachineId()).ToArray();
            var rnd = ThreadSafeRandom.Generator;

            Stopwatch sw = Stopwatch.StartNew();
            fullGC();
            await Task.Delay(2000);

            // Case 1

            sw = Stopwatch.StartNew();
            var c2 = await TestDatabase(test, useMergeOperators: false);
            var noMergeDuration = sw.Elapsed;

            fullGC();
            await Task.Delay(2000);

            // Case 1

            sw = Stopwatch.StartNew();
            var c1 = await TestDatabase(test, useMergeOperators: true);
            var mergeDuration = sw.Elapsed;

            string message =
                $"Done. Merge: Duration={mergeDuration}, SetExistence={c1[ContentLocationDatabaseCounters.SetMachineExistenceAndUpdateDatabase]}," +
                $"DBChanges={c1[ContentLocationDatabaseCounters.DatabaseChanges].Value}, SaveCheckpoint={c1[ContentLocationDatabaseCounters.SaveCheckpoint]}, Merge={c1[ContentLocationDatabaseCounters.MergeEntry]}" +
                $", NoMerge: Duration={noMergeDuration}, SetExistence={c2[ContentLocationDatabaseCounters.SetMachineExistenceAndUpdateDatabase]}, DBChanges={c2[ContentLocationDatabaseCounters.DatabaseChanges].Value}, SaveCheckpoint={c2[ContentLocationDatabaseCounters.SaveCheckpoint]}";
            Logger.Debug(message);

            Assert.True(false, message);

            void test(OperationContext context, RocksDbContentLocationDatabase db)
            {
                context.TracingContext.Debug($"Starting...", nameof(PerformanceTest));

                // Tracking the number of added hashes to emit removals and touches only for the known hashes.
                // In reality we can have removals and touches for GC-ed entries but its relatively uncommon.
                var added = new SortedList<ShortHash, int>();
                
                var sw = Stopwatch.StartNew();

                // Pre-populating the "database".
                for (int i = 0; i < (eventCount / 5); i++)
                {
                    var machine = machines[rnd.Next(machines.Length)];
                    var hash = hashes[rnd.Next(hashes.Count)];
                    addLocation(hash, machine);
                }

                for (int i = 0; i < eventCount; i++)
                {
                    var machine = machines[rnd.Next(machines.Length)];
                    var hash = hashes[rnd.Next(hashes.Count)];

                    Clock.UtcNow += TimeSpan.FromSeconds(1);

                    // The way the cache works, we always have add per remove (because the cache is mostly full),
                    // but because the removal of the locations won't necessarily cause the removal of the entry
                    // we should have the right distribuation of overall database operations.

                    if ((i % 4) != 0)
                    {
                        // 3/4 should be adds and removals

                        if (rnd.Next(2) == 0)
                        {
                            addLocation(hash, machine);
                        }
                        else
                        {
                            var item = rnd.Next(added.Keys.Count);
                            hash = added.Keys[item];
                            removeLocation(hash, machine);
                        }
                    }
                    else
                    {
                        // This is a touch.
                        db.ContentTouched(context, hash, Clock.UtcNow);
                    }
                }

                void addLocation(ShortHash hashToAdd, MachineId machineToAdd)
                {
                    db.LocationAdded(context, hashToAdd, machineToAdd, size: 42);
                    if (added.TryGetValue(hashToAdd, out _))
                    {
                        added[hashToAdd]++;
                    }
                    else
                    {
                        added[hashToAdd] = 1;
                    }
                }

                void removeLocation(ShortHash hashToRemove, MachineId machineToRemove)
                {
                    added[hashToRemove]--;
                    if (added[hashToRemove] <= 0)
                    {
                        added.Remove(hashToRemove);
                    }

                    db.LocationRemoved(context, hashToRemove, machineToRemove);
                }

                context.TracingContext.Debug($"Processed {eventCount} in {sw.Elapsed}", nameof(PerformanceTest));

#pragma warning disable AsyncFixer02 // Long-running or blocking operations inside an async method
                // Have to suppress the warning, because method is actually synchronous.
                Thread.Sleep(1000);
#pragma warning restore AsyncFixer02 // Long-running or blocking operations inside an async method

                db.SaveCheckpoint(context, _workingDirectory.Path / "TempCheckpoint").ThrowIfFailure();
            }

            void fullGC()
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }
    }
}
