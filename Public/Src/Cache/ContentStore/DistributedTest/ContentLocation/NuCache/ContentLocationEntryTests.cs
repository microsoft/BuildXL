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

namespace ContentStoreTest.Distributed.ContentLocation.NuCache
{
    public class ContentLocationEntryTests
    {
        private readonly ITestOutputHelper _testOutputHelper;

        public ContentLocationEntryTests(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
        }

        [Fact]
        public void MergeWithRemovals()
        {
            var machineId = 42.AsMachineId();
            var machineId2 = 43.AsMachineId();

            var left = CreateEntry(MachineIdSet.CreateChangeSet(exists: true, machineId));

            left.Locations.Contains(machineId).Should().BeTrue();

            var right = CreateEntry(MachineIdSet.CreateChangeSet(exists: true, machineId2));

            var merge = left.Merge(right);
            merge.Locations.Count.Should().Be(2);

            merge.Locations.Contains(machineId).Should().BeTrue();
            merge.Locations.Contains(machineId2).Should().BeTrue();

            var removal = CreateEntry(MachineIdSet.CreateChangeSet(exists: false, machineId2));
            merge = merge.Merge(removal);
            merge.Locations.Count.Should().Be(1);

            merge.Locations.Contains(machineId).Should().BeTrue();
            merge.Locations.Contains(machineId2).Should().BeFalse();
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
            var remove = CreateEntry(MachineIdSet.CreateChangeSet(exists: false, 1.AsMachineId()));
            var add = CreateEntry(MachineIdSet.CreateChangeSet(exists: true, 1.AsMachineId()));

            // add + (remove + touch)
            var merged = add.Merge(remove.Merge(touch));
            Assert.Equal(0, merged.Locations.Count);

            // add + (touch + remove)
            var temp = touch.Merge(remove);
            merged = add.Merge(temp);
            Assert.Equal(0, merged.Locations.Count);

            // (add + remove) + touch
            merged = add.Merge(remove).Merge(touch);

            Assert.Equal(0, merged.Locations.Count);

            // This is not the right associativity, but still worth checking
            // (add + remove) + touch
            merged = add.Merge(touch).Merge(remove);

            Assert.Equal(0, merged.Locations.Count);
            
            // (add + (touch + add)) + remove
            merged = add.Merge(touch.Merge(add).Merge(remove));
            Assert.Equal(0, merged.Locations.Count);

            // add + ((touch + add) + remove)
            merged = add.Merge(touch.Merge(add).Merge(remove));
            Assert.Equal(0, merged.Locations.Count);
        }

        [Fact]
        public void TestAssociativityWithNonMergeableSet()
        {
            // Its important that all the merge operations are associative and
            // regardless of the combination of them we should never loose any state changes.

            // If the touch event is crated with an ArrayMachineIdSet instance, the merge won't work.
            var touch = CreateEntry(MachineIdSet.EmptyChangeSet);
            var remove = CreateEntry(MachineIdSet.CreateChangeSet(exists: false, 1.AsMachineId()));
            var add = CreateEntry(MachineIdSet.CreateChangeSet(exists: true, 1.AsMachineId()));

            // add + (remove + touch)
            var merged = add.Merge(remove.Merge(touch));
            Assert.Equal(0, merged.Locations.Count);

            // add + (touch + remove)
            var temp = touch.Merge(remove);
            merged = add.Merge(temp);
            Assert.Equal(0, merged.Locations.Count);

            // (add + remove) + touch
            merged = add.Merge(remove).Merge(touch);

            Assert.Equal(0, merged.Locations.Count);

            // This is not the right associativity, but still worth checking
            // (add + remove) + touch
            merged = add.Merge(touch).Merge(remove);

            Assert.Equal(0, merged.Locations.Count);
            
            // (add + (touch + add)) + remove
            merged = add.Merge(touch.Merge(add).Merge(remove));
            Assert.Equal(0, merged.Locations.Count);

            // add + ((touch + add) + remove)
            merged = add.Merge(touch.Merge(add).Merge(remove));
            Assert.Equal(0, merged.Locations.Count);
        }

        [Fact]
        public void MergeWithLastTouch()
        {
            var machineId = new MachineId(42);
            var machineId2 = new MachineId(43);

            var left = CreateEntry(MachineIdSet.CreateChangeSet(exists: true, machineId));
            var right = CreateEntry(MachineIdSet.CreateChangeSet(exists: true, machineId2));

            var merge = left.Merge(right);

            var removal = CreateEntry(MachineIdSet.CreateChangeSet(exists: false, machineId2));
            merge = merge.Merge(removal);

            var touch = CreateEntry(MachineIdSet.Empty);
            merge = merge.Merge(touch);

            merge.Locations.Contains(machineId).Should().BeTrue();
            merge.Locations.Contains(machineId2).Should().BeFalse();
        }

        private static ContentLocationEntry CreateEntry(MachineIdSet machineIdSet) => ContentLocationEntry.Create(
            machineIdSet,
            contentSize: 42,
            lastAccessTimeUtc: UnixTime.UtcNow,
            UnixTime.UtcNow);

        [Fact]
        public void TestRoundtripRedisValue()
        {
            Random r = new Random();
            for (int machineIdIndex = 0; machineIdIndex < 2048; machineIdIndex++)
            {
                long randomSize = (long)Math.Pow(2, 63 * r.NextDouble());
                byte[] entryBytes = ContentLocationEntry.ConvertSizeAndMachineIdToRedisValue(randomSize, new MachineId(machineIdIndex));

                var deserializedEntry = ContentLocationEntry.FromRedisValue(entryBytes, DateTime.UtcNow, missingSizeHandling: true);
                deserializedEntry.ContentSize.Should().Be(randomSize);
                deserializedEntry.Locations[machineIdIndex].Should().BeTrue();
            }
        }

        [Fact]
        public void TestSerializationRoundtripRedisValue()
        {
            // The test shows 3-x perf improvement (in release mode) for span-based implementation
            // as well as 2-x memory traffic reduction.
            Random r = new Random();
            for (int machineIdIndex = 0; machineIdIndex < 2048; machineIdIndex++)
            {
                long randomSize = (long)Math.Pow(2, 63 * r.NextDouble());
                byte[] entryBytes = ContentLocationEntry.ConvertSizeAndMachineIdToRedisValue(randomSize, new MachineId(machineIdIndex));

                ContentLocationEntry fromRedisValue = ContentLocationEntry.FromRedisValue(entryBytes, DateTime.UtcNow, missingSizeHandling: true);
                ContentLocationEntry copy = Copy(fromRedisValue);

                // The type might change during serialization/deserialization
                copy.Locations.EnumerateMachineIds().Should().BeEquivalentTo(fromRedisValue.Locations.EnumerateMachineIds());

                copy.ContentSize.Should().Be(fromRedisValue.ContentSize);
                copy.CreationTimeUtc.Should().Be(fromRedisValue.CreationTimeUtc);
                copy.IsMissing.Should().Be(fromRedisValue.IsMissing);
                copy.LastAccessTimeUtc.Should().Be(fromRedisValue.LastAccessTimeUtc);
            }
        }

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

        private static ContentLocationEntry Copy(ContentLocationEntry source)
        {
            using (var memoryStream = new MemoryStream())
            {
                using (var writer = BuildXLWriter.Create(memoryStream, leaveOpen: true))
                {
                    source.Serialize(writer);
                }

                memoryStream.Position = 0;
                var reader = memoryStream.ToArray().AsSpan().AsReader();

                return ContentLocationEntry.Deserialize(ref reader);
            }
        }
    }
}
