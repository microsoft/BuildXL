// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities;
using ContentStoreTest.Test;
using FluentAssertions;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.ContentStore.Distributed.Test.Stores
{
    public class EffectiveLastAccessTimeProviderTests : TestBase
    {
        private LocalLocationStoreConfiguration Configuration { get; } = new LocalLocationStoreConfiguration()
        {
            UseTieredDistributedEviction = true,
        };

        [Fact]
        public void ProtectedReplicasAreLastResort()
        {
            var protectedAge = EffectiveLastAccessTimeProvider.GetEffectiveLastAccessTime(
                Configuration,
                age: TimeSpan.FromHours(2),
                replicaCount: 100,
                42,
                rank: ReplicaRank.Protected,
                logInverseMachineRisk: 0);

            protectedAge.Should().Be(TimeSpan.Zero, "Protected age should be zero so that is falls last in eviction ordering");
        }

        [Fact]
        public void ImportantReplicaMovesTheAgeBucket()
        {
            var nonImportant = EffectiveLastAccessTimeProvider.GetEffectiveLastAccessTime(
                Configuration,
                age: TimeSpan.FromHours(2),
                replicaCount: 100,
                42,
                rank: ReplicaRank.None,
                logInverseMachineRisk: 0);

            var important = EffectiveLastAccessTimeProvider.GetEffectiveLastAccessTime(
                Configuration,
                age: TimeSpan.FromHours(2),
                replicaCount: 100,
                42,
                rank: ReplicaRank.Important,
                logInverseMachineRisk: 0);

            important.Should().BeLessThan(nonImportant, "Important replica should be consider to be younger, then non-important one.");
        }

        [Fact]
        public void TestContentEviction()
        {
            var clock = new MemoryClock();
            var entries = new List<ContentLocationEntry>();

            entries.Add(
                ContentLocationEntry.Create(
                    locations: CreateWithLocationCount(1), // Should be important
                    contentSize: 42,
                    lastAccessTimeUtc: clock.UtcNow - TimeSpan.FromHours(2),
                    creationTimeUtc: null));

            entries.Add(
                ContentLocationEntry.Create(
                    locations: CreateWithLocationCount(100), // Maybe important with 3% chance
                    contentSize: 42,
                    lastAccessTimeUtc: clock.UtcNow - TimeSpan.FromHours(2),
                    creationTimeUtc: null));

            entries.Add(
                ContentLocationEntry.Create(
                    locations: CreateWithLocationCount(100), // Maybe important with 3% chance
                    contentSize: 42,
                    lastAccessTimeUtc: clock.UtcNow - TimeSpan.FromHours(2),
                    creationTimeUtc: null));

            var mock = new EffectiveLastAccessTimeProviderMock(localMachineId: new MachineId(1024));
            var hashes = new[] { ContentHash.Random(), ContentHash.Random(), ContentHash.Random() };
            mock.Map = new Dictionary<ContentHash, ContentLocationEntry>()
            {
                [hashes[0]] = entries[0],
                [hashes[1]] = entries[1],
                [hashes[2]] = entries[2],
            };

            var provider = new EffectiveLastAccessTimeProvider(Configuration, clock, mock);

            var context = new OperationContext(new Context(Logger));

            // A given machine id index is higher then the max number of locations used in this test.
            // This will prevent the provider to consider non-important locations randomly important
            var input = hashes.Select(hash => new ContentHashWithLastAccessTime(hash, mock.Map[hash].LastAccessTimeUtc.ToDateTime())).ToList();

            var result = provider.GetEffectiveLastAccessTimes(context, input).ShouldBeSuccess();

            var output = result.Value.ToList();
            output.Sort(ContentEvictionInfo.AgeBucketingPrecedenceComparer.Instance);

            // We know that the first hash should be the last one, because this is only important hash in the list.
            output[output.Count - 1].ContentHash.Should().Be(hashes[0]);
        }

        [Fact]
        public void TestContentEvictionWithDesignatedLocation()
        {
            var clock = new MemoryClock();
            var entries = new List<ContentLocationEntry>();

            entries.Add(
                ContentLocationEntry.Create(
                    locations: CreateWithLocationCount(100),
                    contentSize: 42,
                    lastAccessTimeUtc: clock.UtcNow - TimeSpan.FromHours(2),
                    creationTimeUtc: null));

            entries.Add(
                ContentLocationEntry.Create(
                    locations: CreateWithLocationCount(100),
                    contentSize: 42,
                    lastAccessTimeUtc: clock.UtcNow - TimeSpan.FromHours(2),
                    creationTimeUtc: null));

            entries.Add(
                ContentLocationEntry.Create(
                    locations: CreateWithLocationCount(100),
                    contentSize: 42,
                    lastAccessTimeUtc: clock.UtcNow - TimeSpan.FromHours(2),
                    creationTimeUtc: null));

            var hashes = new[] { ContentHash.Random(), ContentHash.Random(), ContentHash.Random() };

            var mock = new EffectiveLastAccessTimeProviderMock(
                localMachineId: new MachineId(1024),
                isDesignatedLocation: hash => hash == hashes[0]); // The first hash will be designated, and thus important

            mock.Map = new Dictionary<ContentHash, ContentLocationEntry>()
            {
                [hashes[0]] = entries[0],
                [hashes[1]] = entries[1],
                [hashes[2]] = entries[2],
            };

            var provider = new EffectiveLastAccessTimeProvider(Configuration, clock, mock);

            var context = new OperationContext(new Context(Logger));

            // A given machine id index is higher then the max number of locations used in this test.
            // This will prevent the provider to consider non-important locations randomly important
            var input = hashes.Select(hash => new ContentHashWithLastAccessTime(hash, mock.Map[hash].LastAccessTimeUtc.ToDateTime())).ToList();

            var result = provider.GetEffectiveLastAccessTimes(context, input).ShouldBeSuccess();

            var output = result.Value.ToList();
            output.Sort(ContentEvictionInfo.AgeBucketingPrecedenceComparer.Instance);

            // We know that the first hash should be the last one, because this is only important hash in the list.
            output[output.Count - 1].ContentHash.Should().Be(hashes[0]);
        }

        private MachineIdSet CreateWithLocationCount(int locationCount)
        {
            var machineIds = Enumerable.Range(1, locationCount).Select(v => (ushort)v).ToArray();
            return new ArrayMachineIdSet(machineIds);
        }

        public class EffectiveLastAccessTimeProviderMock : IContentResolver
        {
            private readonly Func<ContentHash, bool> _isDesignatedLocation;

            public Dictionary<ContentHash, ContentLocationEntry> Map { get; set; }

            public MachineId LocalMachineId { get; }

            public EffectiveLastAccessTimeProviderMock(MachineId localMachineId, Func<ContentHash, bool> isDesignatedLocation = null)
            {
                LocalMachineId = localMachineId;
                _isDesignatedLocation = isDesignatedLocation;
            }

            /// <inheritdoc />
            public (ContentInfo localInfo, ContentLocationEntry distributedEntry, bool isDesignatedLocation) GetContentInfo(OperationContext context, ContentHash hash)
            {
                return (default, Map[hash], _isDesignatedLocation?.Invoke(hash) == true);
            }
        }

        /// <inheritdoc />
        public EffectiveLastAccessTimeProviderTests(ITestOutputHelper output = null)
            : base(TestGlobal.Logger, output)
        {
        }
    }
}
