using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Engine.Cache.KeyValueStores;
using BuildXL.Utilities;
using BuildXL.Utilities.Serialization;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Cache.ContentStore.Distributed.MetadataService.RocksDbContentMetadataDatabase;
using static BuildXL.Cache.ContentStore.Distributed.MetadataService.RocksDbOperations;

namespace ContentStoreTest.Distributed.ContentLocation.NuCache
{
    public class RocksDbContentMetadataDatabaseTests : TestBase
    {
        protected readonly MemoryClock Clock = new MemoryClock();

        protected readonly DisposableDirectory _workingDirectory;

        protected RocksDbContentLocationDatabaseConfiguration DefaultConfiguration { get; }

        public RocksDbContentMetadataDatabaseTests(ITestOutputHelper output = null)
            : base(TestGlobal.Logger, output)
        {
            // Need to use unique folder for each test instance, because more then one test may be executed simultaneously.
            var uniqueOutputFolder = TestRootDirectoryPath / Guid.NewGuid().ToString();
            _workingDirectory = new DisposableDirectory(FileSystem, uniqueOutputFolder);
        }

        private enum KeyCheckResult
        {
            Missing,
            Different,
            Valid
        }

        [Fact]
        public async Task TestMetadataSizeGarbageCollect()
        {
            var configuration = new RocksDbContentMetadataDatabaseConfiguration(_workingDirectory.Path)
            {
                CleanOnInitialize = false,
                MetadataSizeRotationThreshold = "1gb"
            };

            var context = new Context(Logger);
            var ctx = new OperationContext(context);
            var db = new TestDatabase(Clock, configuration);

            var sf = StrongFingerprint.Random();
            var entry = RandomMetadata();
            await db.StartupAsync(ctx).ShouldBeSuccess();
            db.CompareExchange(ctx, sf, entry, null, lastAccessTimeUtc: default).Result.Should().BeTrue();

            db.CompareExchange(ctx, sf, entry, null, lastAccessTimeUtc: default).Result.Should().BeFalse();

            // Force a garbage collection so the next rotation would trigger the column with the data to be collected
            await db.GarbageCollectAsync(ctx, force: true).ShouldBeSuccess();

            await db.GarbageCollectAsync(ctx).ShouldBeSuccess();

            // Garbage collection should not collect since the size is under threshold 
            db.CompareExchange(ctx, sf, entry, null, lastAccessTimeUtc: default).Result.Should().BeFalse();

            db.SizeInfo[db.NameOf(Columns.Metadata, ColumnGroup.One)] = new ColumnSizeInfo("", 300_000_000, 0);
            await db.GarbageCollectAsync(ctx).ShouldBeSuccess();

            // Garbage collection should not collect since the size is under threshold 
            db.CompareExchange(ctx, sf, entry, null, lastAccessTimeUtc: default).Result.Should().BeFalse();

            db.SizeInfo[db.NameOf(Columns.Metadata, ColumnGroup.Two)] = new ColumnSizeInfo("", 300_000_000, 0);
            await db.GarbageCollectAsync(ctx).ShouldBeSuccess();

            // Garbage collection should not collect since the size is under threshold 
            db.CompareExchange(ctx, sf, entry, null, lastAccessTimeUtc: default).Result.Should().BeFalse();

            db.SizeInfo[db.NameOf(Columns.MetadataHeaders, ColumnGroup.One)] = new ColumnSizeInfo("", 300_000_000, 0);
            await db.GarbageCollectAsync(ctx).ShouldBeSuccess();

            // Garbage collection should not collect since the size is under threshold 
            db.CompareExchange(ctx, sf, entry, null, lastAccessTimeUtc: default).Result.Should().BeFalse();

            db.SizeInfo[db.NameOf(Columns.MetadataHeaders, ColumnGroup.Two)] = new ColumnSizeInfo("", 300_000_000, 0);
            await db.GarbageCollectAsync(ctx).ShouldBeSuccess();

            // Garbage collection SHOULD collect since the size is over threshold 
            db.CompareExchange(ctx, sf, entry, null, lastAccessTimeUtc: default).Result.Should().BeTrue();

            await db.ShutdownAsync(ctx).ShouldBeSuccess();
        }

        private SerializedMetadataEntry RandomMetadata()
        {
            return new SerializedMetadataEntry()
            {
                Data = ThreadSafeRandom.GetBytes(40),
                SequenceNumber = 1,
                ReplacementToken = Guid.NewGuid().ToString()
            };
        }

        [Fact]
        public async Task TestGarbageCollect()
        {
            var configuration = new RocksDbContentMetadataDatabaseConfiguration(_workingDirectory.Path)
            {
                CleanOnInitialize = false,
            };

            var context = new Context(Logger);
            var ctx = new OperationContext(context);

            var keys = Enumerable.Range(0, 10).Select(i => (ShortHash)ContentHash.Random()).ToArray();

            void setBlob(RocksDbContentMetadataDatabase db, ShortHash key)
            {
                db.PutBlob(key, key.ToByteArray());
            }

            KeyCheckResult checkBlob(RocksDbContentMetadataDatabase db, ShortHash key)
            {
                if (db.TryGetBlob(key, out var blob))
                {
                    if (ByteArrayComparer.ArraysEqual(blob, key.ToByteArray()))
                    {
                        return KeyCheckResult.Valid;
                    }
                    else
                    {
                        return KeyCheckResult.Different;
                    }
                }
                else
                {
                    return KeyCheckResult.Missing;
                }
            }

            {
                var db = new RocksDbContentMetadataDatabase(Clock, configuration);
                await db.StartupAsync(ctx).ShouldBeSuccess();
                db.SetGlobalEntry("test", "hello");
                setBlob(db, keys[0]);
                checkBlob(db, keys[0]).Should().Be(KeyCheckResult.Valid);

                await db.GarbageCollectAsync(ctx, force: true).ShouldBeSuccess();
                setBlob(db, keys[1]);
                checkBlob(db, keys[0]).Should().Be(KeyCheckResult.Valid);
                checkBlob(db, keys[1]).Should().Be(KeyCheckResult.Valid);

                await db.GarbageCollectAsync(ctx, force: true).ShouldBeSuccess();
                checkBlob(db, keys[0]).Should().Be(KeyCheckResult.Missing);
                checkBlob(db, keys[1]).Should().Be(KeyCheckResult.Valid);

                await db.ShutdownAsync(ctx).ShouldBeSuccess();
            }

            {
                var db = new RocksDbContentMetadataDatabase(Clock, configuration);
                await db.StartupAsync(ctx).ShouldBeSuccess();

                db.TryGetGlobalEntry("test", out var readValue);
                readValue.Should().Be("hello");

                setBlob(db, keys[2]);

                checkBlob(db, keys[0]).Should().Be(KeyCheckResult.Missing);
                checkBlob(db, keys[1]).Should().Be(KeyCheckResult.Valid);
                checkBlob(db, keys[2]).Should().Be(KeyCheckResult.Valid);
                await db.GarbageCollectAsync(ctx, force: true).ShouldBeSuccess();

                checkBlob(db, keys[0]).Should().Be(KeyCheckResult.Missing);
                checkBlob(db, keys[1]).Should().Be(KeyCheckResult.Missing);
                checkBlob(db, keys[2]).Should().Be(KeyCheckResult.Valid);

                await db.ShutdownAsync(ctx).ShouldBeSuccess();
            }

            // Testing preservation of LastGcTime on reload. Namely,
            // this ensures that column metadata is correctly deserialized
            // on startup and used during GC.
            {
                // Increase time to ensure columns are collected during GC
                Clock.UtcNow += TimeSpan.FromDays(30);
                var db = new RocksDbContentMetadataDatabase(Clock, configuration);
                await db.StartupAsync(ctx).ShouldBeSuccess();

                checkBlob(db, keys[2]).Should().Be(KeyCheckResult.Valid);

                await db.GarbageCollectAsync(ctx, force: false).ShouldBeSuccess();

                checkBlob(db, keys[2]).Should().Be(KeyCheckResult.Missing);

                await db.ShutdownAsync(ctx).ShouldBeSuccess();
            }
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")] // TODO: investigate why?
        public Task TestMergeOperators()
        {
            var configuration = new RocksDbContentMetadataDatabaseConfiguration(_workingDirectory.Path)
            {
                CleanOnInitialize = false,
                UseMergeOperators = true,
            };

            var keys = Enumerable.Range(0, 1000)
                .Select(i => new ShortHashWithSize(
                    ContentHash.Random(),
                    RandomSize()))
                .ToArray();

            var machines = Enumerable.Range(1, 1000).Select(i => new MachineId(i)).ToArray();

            return UseDatabaseAsync(configuration,
                (context, db) =>
                {
                    var now = Clock.UtcNow;
                    db.LocationAdded(context, machines[0], keys.Take(1).ToArray(), touch: true);

                    ExpectEntry(context, db, keys[0], now, machines[0]);

                    db.LocationAdded(context, machines[1], keys.Take(2).ToArray(), touch: true);

                    ExpectEntry(context, db, keys[0], now, machines[0], machines[1]);
                    ExpectEntry(context, db, keys[1], now, machines[1]);

                    db.LocationRemoved(context, keys[0].Hash, machines[1]);

                    ExpectEntry(context, db, keys[0], now, machines[0]);

                    db.LocationAdded(context, machines[1], keys.Take(2).ToArray(), touch: true);

                    ExpectEntry(context, db, keys[0], now, machines[0], machines[1]);
                });
        }

        private ContentLocationEntry ExpectEntry(
            OperationContext context,
            RocksDbContentMetadataDatabase db,
            ShortHashWithSize content,
            CompactTime lastAccessTime,
            params MachineId[] machines)
        {
            db.TryGetEntry(context, content.Hash, out var entry).Should().BeTrue();

            entry.ContentSize.Should().Be(content.Size);
            entry.Locations.Should().Contain(machines);
            entry.Locations.Count.Should().Be(machines.Length);

            return entry;
        }

        private async Task UseDatabaseAsync(
            RocksDbContentMetadataDatabaseConfiguration configuration,
            Action<OperationContext, RocksDbContentMetadataDatabase> use)
        {
            var context = new Context(Logger);
            var ctx = new OperationContext(context);

            var db = new RocksDbContentMetadataDatabase(Clock, configuration);
            await db.StartupAsync(ctx).ShouldBeSuccess();

            use(ctx, db);

            await db.ShutdownAsync(ctx).ShouldBeSuccess();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TestMergeOperations(bool fullMerge)
        {
            var change = LocationChange.CreateRemove(new MachineId(1));

            ThreadSafeRandom.SetSeed(100);

            var expected = TestOperation.Create(1, 2, 3, 4, -5, -6, 7, 8, 9, -10, 1023, -1024);

            var op = TestOperation.Create(1);
            op = op.Merge(1);
            op = op.Merge(2, 3, 1023, 1024);
            op = op.Merge(-3);
            op = op.Merge(4, 6);
            op = op.Remove(1, 6);
            op = op.Merge(-10);
            op = op.Merge(1, 3, 5, 7, 8);
            op = op.Merge(full: fullMerge, 2, -5, 9, -1024);

            if (fullMerge)
            {
                op.Locations.Select(l => l.IsRemove.Should().BeFalse()).LastOrDefault();
            }

            op.Locations.Should().BeEquivalentTo(expected.Locations.Where(l => !fullMerge || l.IsAdd));
        }

        private static long RandomSize() => (long)(ThreadSafeRandom.Generator.NextDouble() * (1L << 48));

        private record struct TestOperation(MachineContentInfo Info, LocationChange[] Locations, byte[] SerializedData = null)
        {
            public MachineIdSet MachineIdSet { get; set; } = new LocationChangeMachineIdSet(Locations.ToImmutableArray());

            public static CompactTime DefaultLastAccessTime = DateTime.UtcNow;

            public static TestOperation Create(params int[] machines)
            {
                return CreateCore(RandomSize(), DefaultLastAccessTime, machines);
            }

            private static TestOperation CreateCore(long? size, CompactTime lastAccessTime, params int[] machines)
            {
                var changes = machines.Select(m => LocationChange.Create(new MachineId(Math.Abs(m)), isRemove: m < 0)).ToArray();
                var result = new TestOperation
                (
                    Info: new MachineContentInfo(size, lastAccessTime),
                    Locations: machines.Select(m => LocationChange.Create(new MachineId(Math.Abs(m)), isRemove: m < 0)).ToArray()
                );

                return FinalizeAndValidate(result);
            }

            private static TestOperation FinalizeAndValidate(TestOperation result)
            {
                result.SerializedData = result.ToArray();

                SpanWriter infoSpan = stackalloc byte[100];
                result.Info.WriteTo(ref infoSpan);
                SpanReader reader = infoSpan.WrittenBytes;
                var readInfo = MachineContentInfo.Read(ref reader);

                ValidateInfo(readInfo, expectedInfo: result.Info);

                result.Validate();

                return result;
            }

            public TestOperation Merge(params int[] machines)
            {
                return Merge(DefaultLastAccessTime, machines, full: false);
            }

            public TestOperation Merge(bool full, params int[] machines)
            {
                return Merge(DefaultLastAccessTime, machines, full: full);
            }

            public TestOperation Merge(CompactTime lastAccessTime, int[] machines, bool full)
            {
                var op = CreateCore(Info.Size.Value, lastAccessTime, machines);
                return Merge(op, full);
            }

            public TestOperation Remove(params ushort[] machines)
            {
                return Merge(CreateRemove(machines), full: false);
            }

            public TestOperation CreateRemove(params ushort[] machines)
            {
                var result = new TestOperation
                (
                    Info: new MachineContentInfo(),
                    Locations: machines.Select(m => LocationChange.CreateRemove(new MachineId(m))).ToArray()
                );

                return FinalizeAndValidate(result);
            }

            public TestOperation Merge(TestOperation other, bool full)
            {
                if (other.Info.Size != null)
                {
                    other.Info.Size.Should().Be(Info.Size);
                }

                SpanWriter mergedData = stackalloc byte[1000];
                if (full)
                {

                }

                mergedData.WriteMergeLocations(SerializedData, other.SerializedData, keepRemoves: !full);

                var merge = new TestOperation
                (
                    Info: MachineContentInfo.Merge(Info, other.Info),
                    Locations: MergeLocations(Locations, other.Locations, full),
                    SerializedData: mergedData.WrittenBytes.ToArray()
                )
                {
                    MachineIdSet = MachineIdSet.Merge(other.MachineIdSet)
                };

                merge.Validate();

                return merge;
            }

            private static LocationChange[] MergeLocations(LocationChange[] locations1, LocationChange[] locations2, bool full)
            {
                return locations2.Concat(locations1)
                    .GroupBy(l => l.Index)
                    .OrderBy(g => g.Key)
                    // Last location change wins unless in full merge case where only adds are retained
                    .SelectMany(g => g.Take(1).Where(l => !full || l.IsAdd))
                    .ToArray();
            }

            private byte[] ToArray()
            {
                SpanWriter writer = stackalloc byte[100];
                writer.WriteLocationEntry<LocationChange>(Locations, l => l, Info);
                return writer.WrittenBytes.ToArray();
            }

            private void Validate()
            {
                RocksDbOperations.ReadMergedContentLocationEntry(SerializedData, out var machines, out var info);

                if (machines.Length == 0 || Locations.Length == 0)
                {
                    machines.ToArray().Should().BeEquivalentTo(Locations);
                }
                else
                {
                    machines.ToArray().Should().Contain(Locations);
                    Locations.Should().Contain(machines.ToArray());
                }

                machines.Length.Should().Be(Locations.Length);
                var expectedCount = machines.ToArray().Where(m => m.IsAdd).Count();
                MachineIdSet.Count.Should().Be(expectedCount);
                var expectedInfo = Info;
                ValidateInfo(info, expectedInfo);
            }

            private static void ValidateInfo(MachineContentInfo actualInfo, MachineContentInfo expectedInfo)
            {
                actualInfo.Size.Should().Be(expectedInfo.Size);
                actualInfo.LatestAccessTime.Should().Be(expectedInfo.LatestAccessTime);
                actualInfo.EarliestAccessTime.Should().Be(expectedInfo.EarliestAccessTime);
            }
        }

        private class TestDatabase : RocksDbContentMetadataDatabase
        {
            public Dictionary<string, ColumnSizeInfo> SizeInfo { get; } = new();

            public TestDatabase(IClock clock, RocksDbContentMetadataDatabaseConfiguration configuration)
                : base(clock, configuration)
            {
            }

            protected override ColumnSizeInfo GetColumnSizeInfo(OperationContext context, RocksDbStore store, string columnFamilyName)
            {
                if (SizeInfo.TryGetValue(columnFamilyName, out var sizeInfo))
                {
                    return sizeInfo;
                }

                return base.GetColumnSizeInfo(context, store, columnFamilyName);
            }
        }
    }
}
