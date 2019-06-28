using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Distributed.ContentLocation.NuCache
{
    /// <summary>
    /// These tests are meant to test very basic functionality of the cache. The reason no further testing is done is
    /// because it would be intruding into the private implementation, instead of just unit testing.
    /// </summary>
    public class ContentLocationDatabaseCacheTests : TestWithOutput
    {
        protected readonly MemoryClock Clock = new MemoryClock();

        /// <summary>
        /// Notice that a <see cref="MemoryContentLocationDatabaseConfiguration"/> is used. This is on purpose, to
        /// avoid dealing with RocksDB.
        /// </summary>
        protected ContentLocationDatabaseConfiguration DefaultConfiguration { get; } = new MemoryContentLocationDatabaseConfiguration
        {
            CacheEnabled = true,
            // These ensure no flushing happens unless explicitly directed
            CacheFlushingMaximumInterval = Timeout.InfiniteTimeSpan,
            CacheMaximumUpdatesPerFlush = -1
        };

        public ContentLocationDatabaseCacheTests(ITestOutputHelper output)
            : base(output)
        {
            
        }

        private async Task RunTest(Action<OperationContext, ContentLocationDatabase> action) => await RunCustomTest(DefaultConfiguration, action);

        private async Task RunCustomTest(ContentLocationDatabaseConfiguration configuration, Action<OperationContext, ContentLocationDatabase> action)
        {
            var tracingContext = new Context(TestGlobal.Logger);
            var operationContext = new OperationContext(tracingContext);

            var database = ContentLocationDatabase.Create(Clock, configuration, () => new MachineId[] { });
            await database.StartupAsync(operationContext).ShouldBeSuccess();
            database.SetDatabaseMode(isDatabaseWritable: true);

            action(operationContext, database);

            await database.ShutdownAsync(operationContext).ShouldBeSuccess();
        }

        [Fact]
        public Task ReadMyWrites()
        {
            return RunTest((context, database) =>
             {
                 var machine = new MachineId(1);
                 var hash = new ShortHash(ContentHash.Random());

                 database.LocationAdded(context, hash, machine, 200);

                 database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheMiss].Value.Should().Be(1);
                 database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheHit].Value.Should().Be(0);

                 database.TryGetEntry(context, hash, out var entry).Should().BeTrue();
                 entry.ContentSize.Should().Be(200);
                 entry.Locations.Count.Should().Be(1);
                 entry.Locations[machine].Should().BeTrue();

                 database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheMiss].Value.Should().Be(1);
                 database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheHit].Value.Should().Be(1);
             });
        }

        [Fact]
        public Task ReadMyDeletes()
        {
            return RunTest((context, database) =>
            {
                var machine = new MachineId(1);
                var hash = new ShortHash(ContentHash.Random());

                database.LocationAdded(context, hash, machine, 200);

                database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheMiss].Value.Should().Be(1);
                database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheHit].Value.Should().Be(0);

                database.LocationRemoved(context, hash, machine);

                database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheMiss].Value.Should().Be(1);
                database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheHit].Value.Should().Be(1);

                database.TryGetEntry(context, hash, out var entry).Should().BeFalse();

                database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheMiss].Value.Should().Be(1);
                database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheHit].Value.Should().Be(2);
            });
        }

        [Fact]
        public Task SubsequentWrites()
        {
            return RunTest((context, database) =>
            {
                var machine = new MachineId(1);
                var machine2 = new MachineId(2);
                var hash = new ShortHash(ContentHash.Random());

                database.LocationAdded(context, hash, machine, 200);
                database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheMiss].Value.Should().Be(1);
                database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheHit].Value.Should().Be(0);

                database.LocationAdded(context, hash, machine2, 200);
                database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheMiss].Value.Should().Be(1);
                database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheHit].Value.Should().Be(1);

                database.TryGetEntry(context, hash, out var entry).Should().BeTrue();
                entry.ContentSize.Should().Be(200);
                entry.Locations.Count.Should().Be(2);
                entry.Locations[machine].Should().BeTrue();
                entry.Locations[machine2].Should().BeTrue();

                database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheMiss].Value.Should().Be(1);
                database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheHit].Value.Should().Be(2);
            });
        }

        [Fact]
        public Task SizeChangeOverwrites()
        {
            return RunTest((context, database) =>
            {
                var machine = new MachineId(1);
                var machine2 = new MachineId(2);
                var hash = new ShortHash(ContentHash.Random());

                database.LocationAdded(context, hash, machine, 200);
                database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheMiss].Value.Should().Be(1);
                database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheHit].Value.Should().Be(0);

                database.LocationAdded(context, hash, machine2, 400);
                database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheMiss].Value.Should().Be(1);
                database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheHit].Value.Should().Be(1);

                database.TryGetEntry(context, hash, out var entry).Should().BeTrue();
                entry.ContentSize.Should().Be(400);
                entry.Locations.Count.Should().Be(2);
                entry.Locations[machine].Should().BeTrue();
                entry.Locations[machine2].Should().BeTrue();

                database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheMiss].Value.Should().Be(1);
                database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheHit].Value.Should().Be(2);
            });
        }

        [Fact]
        public Task DeleteUnknownDoesNotFailOrModify()
        {
            return RunTest((context, database) =>
            {
                var machine = new MachineId(1);
                var hash = new ShortHash(ContentHash.Random());

                database.LocationRemoved(context, hash, machine);
                database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheMiss].Value.Should().Be(1);
                database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheHit].Value.Should().Be(0);

                database.TryGetEntry(context, hash, out var entry).Should().BeFalse();
                database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheMiss].Value.Should().Be(2);
                database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheHit].Value.Should().Be(0);
            });
        }

        [Fact]
        public Task FlushSyncsToStorage()
        {
            return RunTest((context, database) =>
            {
                var machine = new MachineId(1);
                var hash = new ShortHash(ContentHash.Random());

                database.LocationAdded(context, hash, machine, 200);
                database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheMiss].Value.Should().Be(1);
                database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheHit].Value.Should().Be(0);

                database.ForceCacheFlush(context);
                database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheFlushes].Value.Should().Be(1);

                database.TryGetEntry(context, hash, out var entry).Should().BeTrue();
                database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheMiss].Value.Should().Be(2);
                database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheHit].Value.Should().Be(0);
            });
        }

        [Fact]
        public Task PartialFlushingWorks()
        {
            ContentLocationDatabaseConfiguration configuration = new MemoryContentLocationDatabaseConfiguration
            {
                CacheEnabled = true,
                // These ensure no flushing happens unless explicitly directed
                CacheFlushingMaximumInterval = Timeout.InfiniteTimeSpan,
                CacheMaximumUpdatesPerFlush = -1,
                FlushPreservePercentInMemory = 0.5,
            };

            return RunCustomTest(configuration, (context, database) =>
            {
                // Setup small test DB
                foreach (var _ in Enumerable.Range(0, 100))
                {
                    database.LocationAdded(context, new ShortHash(ContentHash.Random()), new MachineId(1), 200);
                }

                database.ForceCacheFlush(context);
                database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheFlushes].Value.Should().Be(1);
                database.Counters[ContentLocationDatabaseCounters.NumberOfPersistedEntries].Value.Should().Be(100);

                // The second flush will discard the flushing cache, and we haven't added anything in-between
                database.ForceCacheFlush(context);
                database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheFlushes].Value.Should().Be(2);
                database.Counters[ContentLocationDatabaseCounters.NumberOfPersistedEntries].Value.Should().Be(100);
            });
        }
    }
}
