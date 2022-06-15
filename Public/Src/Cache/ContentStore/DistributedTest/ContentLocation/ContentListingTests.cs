// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Collections;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Test.ContentLocation
{
    [Collection("Redis-based tests")]
    [Trait("Category", "WindowsOSOnly")] // 'redis-server' executable no longer exists
    public class ContentListingTests : TestWithOutput
    {
        private readonly static MachineLocation M1 = new MachineLocation("M1");
        private readonly LocalRedisFixture _fixture;

        public ContentListingTests(LocalRedisFixture fixture, ITestOutputHelper output)
            : base(output)
        {
            _fixture = fixture;
        }

        [Fact]
        public void TestEntryFormat()
        {
            Unsafe.SizeOf<ShortReadOnlyFixedBytes>()
                .Should()
                .Be(ShortReadOnlyFixedBytes.MaxLength);

            Unsafe.SizeOf<CompactTime>()
                .Should()
                .Be(4);

            Unsafe.SizeOf<CompactSize>()
                .Should()
                .Be(6);

            Span<MachineContentEntry> span = stackalloc MachineContentEntry[1];
            var bytes = MemoryMarshal.AsBytes(span);
            bytes.Length.Should().Be(MachineContentEntry.ByteLength);

            bytes.Length.Should().Be(24);

            var expectedBytes = new byte[] { 82, 38, 48, 114, 220, 31, 164, 244, 155, 159, 66, 4, 102, 128, 67, 8, 251, 113, 31, 0, 49, 159, 99, 0 };

            var actualEntry = MemoryMarshal.Read<MachineContentEntry>(expectedBytes);
            actualEntry.Hash.Should().Be(new ShortHash("VSO0:52263072DC1FA4F49B9F42"));
            actualEntry.Size.Value.Should().Be(135_056_263_235L);
            actualEntry.AccessTime.ToDateTime().Should().Be(DateTime.Parse("5/31/2022 9:37:00 PM"));
            actualEntry.PartitionId.Should().Be(82);

            span[0] = actualEntry;

            bytes.ToArray().Should().BeEquivalentTo(expectedBytes);
        }

        [Fact]
        public void TestRoundtripShardHash()
        {
            ShortHash startShortHash = ContentHash.Random();
            var shardHash = new ShardHash(startShortHash);
            var roundTripShortHash = shardHash.ToShortHash();
            Assert.Equal(startShortHash, roundTripShortHash);
        }

        [Theory]
        [InlineData(1_000_000)]
        [InlineData(10_000_000)]
        public void TestSort(int count)
        {
            var byteLength = MachineContentEntry.ByteLength;

            Stopwatch watch = Stopwatch.StartNew();
            var entries = new MachineContentEntry[count];
            DateTime time = DateTime.UtcNow;
            for (int i = 0; i < count; i++)
            {
                entries[i] = MachineContentEntry.Create(ContentHash.Random(), new LocationChange((ushort)(i % 2000)), 1000, time);
            }

            Output.WriteLine($"Generated in {watch.Elapsed}");
            watch.Restart();

            var bucketCopy = new MachineContentEntry[count];
            entries.CopyTo(bucketCopy.AsMemory());
            Output.WriteLine($"Copied in {watch.Elapsed}");
            watch.Restart();

            SpanSortHelper.BucketSort(() => bucketCopy.AsSpan(),
                e => e.PartitionId,
                1 << 8,
                parallelism: 16);
            Output.WriteLine($"Bucket sorted in {watch.Elapsed}");

            watch.Restart();

            if (count > 10_000_000)
            {
                return;
            }

            var listing = new ContentListing(entries);
            listing.SortAndDeduplicate();
            Output.WriteLine($"Sorted listing in {watch.Elapsed}");
            watch.Restart();

            var copy = new MachineContentEntry[count];
            entries.CopyTo(copy.AsMemory());
            Output.WriteLine($"Copied in {watch.Elapsed}");
            watch.Restart();

            SpanSortHelper.Sort(entries.AsSpan());
            Output.WriteLine($"Sorted in {watch.Elapsed}");
            watch.Restart();

            Array.Sort(copy);
            Output.WriteLine($"Sorted copy in {watch.Elapsed}");

            watch.Restart();
            for (int i = 0; i < copy.Length; i++)
            {
                Assert.Equal(copy[i], bucketCopy[i]);
            }

            for (int i = 0; i < copy.Length; i++)
            {
                Assert.Equal(copy[i], listing.EntrySpan[i]);
            }
            Output.WriteLine($"Verified in in {watch.Elapsed}");
        }
    }
}
