using System;
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
        protected readonly MemoryClock _clock = new MemoryClock();

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

        protected readonly ContentLocationDatabase _database;

        public ContentLocationDatabaseCacheTests(ITestOutputHelper output)
            : base(output)
        {
            _database = ContentLocationDatabase.Create(_clock, DefaultConfiguration, () => new MachineId[] { });
        }

        private async Task WithContext(Action<OperationContext> action)
        {
            var tracingContext = new Context(TestGlobal.Logger);
            var operationContext = new OperationContext(tracingContext);

            await _database.StartupAsync(operationContext).ShouldBeSuccess();
            _database.SetDatabaseMode(isDatabaseWritable: true);

            action(operationContext);

            await _database.ShutdownAsync(operationContext).ShouldBeSuccess();
        }

        [Fact]
        public Task ReadMyWrites()
        {
            return WithContext(context =>
             {
                 var machine = new MachineId(1);
                 var hash = new ShortHash(ContentHash.Random());

                 _database.LocationAdded(context, hash, machine, 200);

                 _database.TryGetEntry(context, hash, out var entry).Should().BeTrue();
                 entry.ContentSize.Should().Be(200);
                 entry.Locations.Count.Should().Be(1);
                 entry.Locations[machine].Should().BeTrue();

                 _database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheMiss].Should().Be(0);
                 _database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheHit].Should().Be(1);
             });
        }

        [Fact]
        public Task ReadMyDeletes()
        {
            return WithContext(context =>
            {
                var machine = new MachineId(1);
                var hash = new ShortHash(ContentHash.Random());

                _database.LocationAdded(context, hash, machine, 200);
                _database.LocationRemoved(context, hash, machine);

                _database.TryGetEntry(context, hash, out var entry).Should().BeFalse();

                _database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheMiss].Should().Be(0);
                _database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheHit].Should().Be(1);
            });
        }

        [Fact]
        public Task SubsequentWrites()
        {
            return WithContext(context =>
            {
                var machine = new MachineId(1);
                var machine2 = new MachineId(2);
                var hash = new ShortHash(ContentHash.Random());

                _database.LocationAdded(context, hash, machine, 200);
                _database.LocationAdded(context, hash, machine2, 200);

                _database.TryGetEntry(context, hash, out var entry).Should().BeTrue();
                entry.ContentSize.Should().Be(200);
                entry.Locations.Count.Should().Be(2);
                entry.Locations[machine].Should().BeTrue();
                entry.Locations[machine2].Should().BeTrue();

                _database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheMiss].Should().Be(0);
                _database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheHit].Should().Be(1);
            });
        }

        [Fact]
        public Task SizeChangeOverwrites()
        {
            return WithContext(context =>
            {
                var machine = new MachineId(1);
                var machine2 = new MachineId(2);
                var hash = new ShortHash(ContentHash.Random());

                _database.LocationAdded(context, hash, machine, 200);
                _database.LocationAdded(context, hash, machine2, 400);

                _database.TryGetEntry(context, hash, out var entry).Should().BeTrue();
                entry.ContentSize.Should().Be(400);
                entry.Locations.Count.Should().Be(2);
                entry.Locations[machine].Should().BeTrue();
                entry.Locations[machine2].Should().BeTrue();

                _database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheMiss].Should().Be(0);
                _database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheHit].Should().Be(1);
            });
        }

        [Fact]
        public Task DeleteUnknownDoesNotFailOrModify()
        {
            return WithContext(context =>
            {
                var machine = new MachineId(1);
                var hash = new ShortHash(ContentHash.Random());

                _database.LocationRemoved(context, hash, machine);
                _database.TryGetEntry(context, hash, out var entry).Should().BeFalse();

                _database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheMiss].Should().Be(1);
                _database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheHit].Should().Be(0);
            });
        }

        [Fact]
        public Task FlushSyncsToStorage()
        {
            return WithContext(context =>
            {
                var machine = new MachineId(1);
                var hash = new ShortHash(ContentHash.Random());

                _database.LocationAdded(context, hash, machine, 200);

                _database.FlushIfEnabled(context);
                _database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheFlushes].Should().Be(1);

                _database.TryGetEntry(context, hash, out var entry).Should().BeTrue();
                _database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheMiss].Should().Be(1);
                _database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheHit].Should().Be(0);
            });
        }
    }
}
