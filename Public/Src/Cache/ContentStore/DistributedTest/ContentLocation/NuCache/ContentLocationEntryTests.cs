// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities;
using BuildXL.Utilities.Serialization;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static ContentStoreTest.Distributed.ContentLocation.NuCache.ContentLocationEntryTestHelpers;

namespace ContentStoreTest.Distributed.ContentLocation.NuCache
{
    public class ContentLocationEntryTests
    {
        private readonly ITestOutputHelper _testOutputHelper;

        public ContentLocationEntryTests(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(500)]
        public void SerializationDeserializationTest(int locationCount)
        {
            SerializationDeserializationTestCore(locationCount);
        }

        [Fact]
        public void SerializationDeserializationTest42()
        {
            int locationCount = 42;

            SerializationDeserializationTestCore(locationCount);
        }

        public void SerializationDeserializationTestCore(int locationCount)
        {
            var entry = CreateEntry(CreateMachineIdSet(Enumerable.Range(1, locationCount).Select(l => l.AsMachineId())));
            var copy = entry.CloneWithSpan();

            copy.Should().Be(entry, $"Cloning with span should give equivalent {nameof(ContentLocationEntry)}");

            copy = entry.CloneWithStream();
            copy.Should().Be(entry, $"Cloning with stream should give equivalent {nameof(ContentLocationEntry)}");
        }

        [Fact]
        public void MergeWithRemovals()
        {
            var machineId = 42.AsMachineId();
            var machineId2 = 43.AsMachineId();

            // Using stable time to improve debuggability of this test.
            var leftEntryCreationTime = new DateTime(2022, 10, 1, hour: 1, minute: 2, second: 3).ToUniversalTime();
            var rightEntryCreationTime = new DateTime(2022, 10, 1, hour: 1, minute: 2, second: 4).ToUniversalTime();

            var leftEntryLastAccessTime = leftEntryCreationTime + TimeSpan.FromSeconds(1);
            var rightEntryLastAccessTime = rightEntryCreationTime + TimeSpan.FromSeconds(5);

            var left = CreateEntry(CreateChangeSet(exists: true, machineId), lastAccessTimeUtc: leftEntryLastAccessTime, leftEntryCreationTime);

            left.Locations.Contains(machineId).Should().BeTrue();

            var right = CreateEntry(CreateChangeSet(exists: true, machineId2), lastAccessTimeUtc: rightEntryLastAccessTime, rightEntryCreationTime);

            var merge = left.MergeTest(right);

            merge.Locations.Count.Should().Be(2);

            merge.Locations.Contains(machineId).Should().BeTrue();
            merge.Locations.Contains(machineId2).Should().BeTrue();

            var removalLastAccessTime = rightEntryLastAccessTime + TimeSpan.FromSeconds(3);
            var removal = CreateEntry(CreateChangeSet(exists: false, machineId2), lastAccessTimeUtc: removalLastAccessTime, rightEntryCreationTime);
            merge = merge.MergeTest(removal);

            merge.Locations.Count.Should().Be(1);

            merge.Locations.Contains(machineId).Should().BeTrue();
            merge.Locations.Contains(machineId2).Should().BeFalse();
            merge.CreationTimeUtc.Should().Be(leftEntryCreationTime);
            merge.LastAccessTimeUtc.Should().Be(removalLastAccessTime);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TestAssociativity(bool useMergeableTouch)
        {
            // Making sure that the merge works even if the left hand side of the Merge call
            // is not a state change instance.

            // If the touch event is crated with an ArrayMachineIdSet instance, the merge won't work.
            var touch = CreateEntry(useMergeableTouch ? MachineIdSet.EmptyChangeSet : MachineIdSet.Empty);
            var remove = CreateEntry(CreateChangeSet(exists: false, 1.AsMachineId()));
            var add = CreateEntry(CreateChangeSet(exists: true, 1.AsMachineId()));

            // add + (remove + touch)
            var merged = add.MergeTest(remove.MergeTest(touch));
            Assert.Equal(0, merged.Locations.Count);

            // add + (touch + remove)
            var temp = touch.MergeTest(remove);
            merged = add.MergeTest(temp);
            Assert.Equal(0, merged.Locations.Count);

            // (add + remove) + touch
            merged = add.MergeTest(remove).MergeTest(touch);

            Assert.Equal(0, merged.Locations.Count);

            // This is not the right associativity, but still worth checking
            // (add + remove) + touch
            merged = add.MergeTest(touch).MergeTest(remove);

            Assert.Equal(0, merged.Locations.Count);
            
            // (add + (touch + add)) + remove
            merged = add.MergeTest(touch.MergeTest(add).MergeTest(remove));
            Assert.Equal(0, merged.Locations.Count);

            // add + ((touch + add) + remove)
            merged = add.MergeTest(touch.MergeTest(add).MergeTest(remove));
            Assert.Equal(0, merged.Locations.Count);
        }

        [Fact]
        public void TestAssociativityWithNonMergeableSet()
        {
            // Its important that all the merge operations are associative and
            // regardless of the combination of them we should never loose any state changes.

            // If the touch event is crated with an ArrayMachineIdSet instance, the merge won't work.
            var touch = CreateEntry(MachineIdSet.EmptyChangeSet);
            var remove = CreateEntry(CreateChangeSet(exists: false, 1.AsMachineId()));
            var add = CreateEntry(CreateChangeSet(exists: true, 1.AsMachineId()));

            // add + (remove + touch)
            var merged = add.MergeTest(remove.MergeTest(touch));
            Assert.Equal(0, merged.Locations.Count);

            // add + (touch + remove)
            var temp = touch.MergeTest(remove);
            merged = add.MergeTest(temp);
            Assert.Equal(0, merged.Locations.Count);

            // (add + remove) + touch
            merged = add.MergeTest(remove).MergeTest(touch);

            Assert.Equal(0, merged.Locations.Count);

            // This is not the right associativity, but still worth checking
            // (add + remove) + touch
            merged = add.MergeTest(touch).MergeTest(remove);

            Assert.Equal(0, merged.Locations.Count);
            
            // (add + (touch + add)) + remove
            merged = add.MergeTest(touch.MergeTest(add).MergeTest(remove));
            Assert.Equal(0, merged.Locations.Count);

            // add + ((touch + add) + remove)
            merged = add.MergeTest(touch.MergeTest(add).MergeTest(remove));
            Assert.Equal(0, merged.Locations.Count);
        }

        [Fact]
        public void MergeWithLastTouch()
        {
            var machineId = new MachineId(42);
            var machineId2 = new MachineId(43);

            var left = CreateEntry(CreateChangeSet(exists: true, machineId));
            var right = CreateEntry(CreateChangeSet(exists: true, machineId2));

            var merge = left.MergeTest(right);

            var removal = CreateEntry(CreateChangeSet(exists: false, machineId2));
            merge = merge.MergeTest(removal);

            var touch = CreateEntry(MachineIdSet.Empty);
            merge = merge.MergeTest(touch);

            merge.Locations.Contains(machineId).Should().BeTrue();
            merge.Locations.Contains(machineId2).Should().BeFalse();
        }

        private static ContentLocationEntry CreateEntry(MachineIdSet machineIdSet, UnixTime? lastAccessTimeUtc = null, UnixTime? creationTimeUtc = null) => ContentLocationEntry.Create(
            machineIdSet,
            contentSize: 42,
            lastAccessTimeUtc: lastAccessTimeUtc ?? (DateTime.UtcNow - TimeSpan.FromMinutes(5)).ToUnixTime(),
            creationTimeUtc: creationTimeUtc ?? UnixTime.UtcNow);

        [Fact(Skip = "For profiling purposes only")]
        public void PerformanceTestComparison()
        {
            System.Diagnostics.Debugger.Launch();
            var arrayMachineIdSet = MachineIdSet.Empty.Add(Enumerable.Range(1, 8).Select(i => new MachineId(i)).ToArray());
            var arrayMachineIdSetEntry = ContentLocationEntry.Create(
                arrayMachineIdSet,
                contentSize: long.MaxValue / 2,
                new UnixTime(42),
                new UnixTime(int.MaxValue));

            var bitSetMachineIdSet = MachineIdSet.Empty.Add(Enumerable.Range(1, 128).Select(i => new MachineId(i)).ToArray());
            var bitSetMachineIdSetEntry = ContentLocationEntry.Create(
                bitSetMachineIdSet,
                contentSize: long.MaxValue / 4,
                new UnixTime(142),
                new UnixTime(int.MaxValue/8));

            using var arrayMachineIdMemoryStream = UnmanagedMemoryWrapper.FromContentLocationEntry(arrayMachineIdSetEntry);
            using var bitSetMachineIdMemoryStream = UnmanagedMemoryWrapper.FromContentLocationEntry(bitSetMachineIdSetEntry);

            // warm up
            for (int i = 0; i < 1000; i++)
            {
                DeserializeWithBxlReader(arrayMachineIdMemoryStream);
                DeserializeWithBxlReader(bitSetMachineIdMemoryStream);

                DeserializeWithSpanReader(arrayMachineIdMemoryStream);
                DeserializeWithSpanReader(bitSetMachineIdMemoryStream);
            }

            Thread.Sleep(2_000); // to see the start in the profiler.
            int iterationCount = 3_000_000;

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterationCount; i++)
            {
                DeserializeWithBxlReader(arrayMachineIdMemoryStream);
            }
            var arrayMachineIdDeserializationDurationFromBxlReader = sw.ElapsedMilliseconds;

            sw.Restart();
            for (int i = 0; i < iterationCount; i++)
            {
                DeserializeWithBxlReader(bitSetMachineIdMemoryStream);
            }

            var bitSetMachineIdDeserializationDurationFromBxlReader = sw.ElapsedMilliseconds;

            Thread.Sleep(2_000); // to see the start in the profiler.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            sw.Restart();

            for (int i = 0; i < iterationCount; i++)
            {
                DeserializeWithSpanReader(arrayMachineIdMemoryStream);
            }
            var arrayMachineIdDeserializationDurationFromSpanReader = sw.ElapsedMilliseconds;

            sw.Restart();

            for (int i = 0; i < iterationCount; i++)
            {
                DeserializeWithSpanReader(bitSetMachineIdMemoryStream);
            }
            var bitSetMachineIdDeserializationDurationFromSpanReader = sw.ElapsedMilliseconds;

            _testOutputHelper.WriteLine($"Perf: ArrayMachineId with bxl - {arrayMachineIdDeserializationDurationFromBxlReader}ms, BitMachineId with bxl - {bitSetMachineIdDeserializationDurationFromBxlReader}ms, "
            + $"ArrayMachineId with span - {arrayMachineIdDeserializationDurationFromSpanReader}ms, BitMachineId with span - {bitSetMachineIdDeserializationDurationFromSpanReader}ms");

            System.Diagnostics.Debugger.Launch();
        }

        protected readonly SerializationPool SerializationPool = new SerializationPool();

        private ContentLocationEntry DeserializeWithBxlReader(UnmanagedMemoryWrapper span)
        {
            unsafe
            {
                using var stream = new UnmanagedMemoryStream((byte*)span.ValuePtr.ToPointer(), span.Length);
                return SerializationPool.Deserialize(stream, static reader => ContentLocationEntry.Deserialize(reader));
            }
        }

        private ContentLocationEntry DeserializeWithSpanReader(UnmanagedMemoryWrapper span)
        {
            var reader = span.AsSpan().AsReader();
            return ContentLocationEntry.Deserialize(ref reader);
        }

        private ContentLocationEntry DeserializeContentLocationEntry(UnmanagedMemoryWrapper span)
        {
            var reader = span.AsSpan().AsReader();
            return ContentLocationEntry.Deserialize(ref reader);
        }

        public class UnmanagedMemoryWrapper : IDisposable
        {
            public IntPtr ValuePtr { get; }
            public long Length { get; }

            public ReadOnlySpan<byte> AsSpan()
            {
                unsafe
                {
                    return new ReadOnlySpan<byte>((byte*)ValuePtr.ToPointer(), (int)Length);
                }
            }

            private UnmanagedMemoryWrapper(IntPtr ptr, long length)
            {
                ValuePtr = ptr;
                Length = length;
            }

            public void Dispose()
            {
                Marshal.FreeHGlobal(ValuePtr);
            }

            public static UnmanagedMemoryWrapper FromContentLocationEntry(ContentLocationEntry entry)
            {
                using var memoryStream = new MemoryStream();
                using var writer = BuildXLWriter.Create(memoryStream, leaveOpen: true);
                entry.Serialize(writer);

                memoryStream.Position = 0;

                var bytes = memoryStream.ToArray();
                IntPtr unmanagedPointer = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, unmanagedPointer, bytes.Length);

                return new UnmanagedMemoryWrapper(unmanagedPointer, bytes.Length);
            }
        }

        private static MemoryStream WriteTo(ContentLocationEntry entry)
        {
            var memoryStream = new MemoryStream();
            using (var writer = BuildXLWriter.Create(memoryStream, leaveOpen: true))
            {
                entry.Serialize(writer);
            }

            memoryStream.Position = 0;
            return memoryStream;
        }
    }
}
