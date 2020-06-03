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
            DesiredReplicaRetention = 3
        };

        [Fact]
        public void RareContentShouldBeImportant()
        {
            var hash = ContentHash.Random();
            var clock = new MemoryClock();
            var entry = ContentLocationEntry.Create(
                locations: new ArrayMachineIdSet(new ushort[1]),
                contentSize: 42,
                lastAccessTimeUtc: clock.UtcNow,
                creationTimeUtc: clock.UtcNow);
            bool isImportant = EffectiveLastAccessTimeProvider.GetReplicaRank(hash, entry, new MachineId(1), Configuration, clock.UtcNow) == ReplicaRank.Important;
            isImportant.Should().BeTrue();
        }

        [Theory]
        [InlineData(new ushort[] { 1 })]
        [InlineData(new ushort[] { 1, 2 })]
        [InlineData(new ushort[] { 1, 2, 3 })]
        public void RareContentShouldBeImportantOrProtectedWithThrottling(ushort[] machineIds)
        {
            Configuration.ThrottledEvictionInterval = TimeSpan.FromMinutes(20);

            var hash = ContentHash.Random();
            var clock = new MemoryClock();

            var entry = ContentLocationEntry.Create(
                locations: new ArrayMachineIdSet(machineIds),
                contentSize: 42,
                lastAccessTimeUtc: clock.UtcNow,
                creationTimeUtc: clock.UtcNow);

            int totalImportantFound = 0;

            foreach (var machineId in machineIds)
            {
                int protectedCount = 0;
                int importantCount = 0;

                for (int i = 0; i < Configuration.DesiredReplicaRetention; i++)
                {
                    var rank = EffectiveLastAccessTimeProvider.GetReplicaRank(hash, entry, new MachineId(machineId), Configuration, clock.UtcNow);
                    clock.UtcNow += Configuration.ThrottledEvictionInterval;

                    switch (rank)
                    {
                        case ReplicaRank.Important:
                            importantCount++;
                            totalImportantFound++;
                            break;
                        case ReplicaRank.Protected:
                            protectedCount++;
                            break;
                        default:
                            XAssert.Fail($"Rank is '{rank}' but should be Important or Protected since content is rare");
                            break;
                    }
                }

                protectedCount.Should().BeInRange(Configuration.DesiredReplicaRetention - 1, Configuration.DesiredReplicaRetention, "At least (DesiredReplicaCount - 1) out of DesiredReplicaCount of the time content should be protected");
                importantCount.Should().BeInRange(0, 1, "At most 1 out of DesiredReplicaCount of the time content should just be important allowing eviction");
            }

            totalImportantFound.Should().Be(1, "Within the time interval (DesiredReplicaCount * ThrottledEvictionInterval), " +
                "during exactly one ThrottledEvictionInterval exactly one machine should have content unproteced");
        }

        [Fact]
        public void OnlyOneImportantReplicaIsUnprotected()
        {
            Configuration.ThrottledEvictionInterval = TimeSpan.FromMinutes(20);

            var hash = ContentHash.Random();
            var clock = new MemoryClock();

            var machineIds = new ushort[] { 1, 2, 3 };
            var entry = ContentLocationEntry.Create(
                locations: new ArrayMachineIdSet(machineIds),
                contentSize: 42,
                lastAccessTimeUtc: clock.UtcNow,
                creationTimeUtc: clock.UtcNow);

            int rangesWithUnprotected = 0;

            for (int i = 0; i < Configuration.DesiredReplicaRetention; i++)
            {
                clock.UtcNow += Configuration.ThrottledEvictionInterval;

                int protectedCount = 0;
                int importantCount = 0;

                foreach (var machineId in machineIds)
                {
                    var rank = EffectiveLastAccessTimeProvider.GetReplicaRank(hash, entry, new MachineId(machineId), Configuration, clock.UtcNow);

                    switch (rank)
                    {
                        case ReplicaRank.Important:
                            importantCount++;
                            break;
                        case ReplicaRank.Protected:
                            protectedCount++;
                            break;
                        default:
                            XAssert.Fail($"Rank is '{rank}' but should be Important or Protected since content is rare");
                            break;
                    }
                }

                var importantOrProtectedCount = protectedCount + importantCount;

                importantCount.Should().BeLessOrEqualTo(1, "At most 1 out of DesiredReplicaCount of the time content should just be important allowing eviction");
                importantOrProtectedCount.Should().Be(Configuration.DesiredReplicaRetention, "All replicas should be important or protected");

                if (importantCount == 1)
                {
                    rangesWithUnprotected++;
                    protectedCount.Should().Be(Configuration.DesiredReplicaRetention - 1, "(DesiredReplicaCount - 1) out of DesiredReplicaCount of the time content should be protected");
                }
            }

            rangesWithUnprotected.Should().Be(1, "There should be one time range out of DesiredReplicaCount ranges where one of replicas is unprotected");
        }

        [InlineData(5)]
        [InlineData(10)]
        [InlineData(50)]
        [InlineData(100)]
        [InlineData(501)]
        [Theory]
        public void NonRareContentShouldBeImportantInSomeCases(int machineCount)
        {
            // Using non random hash to make the test deterministic
            var hash = VsoHashInfo.Instance.EmptyHash;
            var machineIds = Enumerable.Range(1, machineCount).Select(v => (ushort)v).ToArray();

            var clock = new MemoryClock();
            // Creating an entry with 'machineCount' locations.
            // But instead of using instance as is, we call Copy to serialize/deserialize the instance.
            // In this case, we'll create different types at runtime instead of using just a specific type like ArrayMachineIdSet.
            var machineIdSet = Copy(new ArrayMachineIdSet(machineIds));
            var entry = ContentLocationEntry.Create(
                locations: machineIdSet,
                contentSize: 42,
                lastAccessTimeUtc: clock.UtcNow,
                creationTimeUtc: clock.UtcNow);

            // Then checking all the "machines" (by creating MachineId for each id in machineIds)
            // to figure out if the replica is important.
            var ranks = machineIds.Select(
                machineId => EffectiveLastAccessTimeProvider.GetReplicaRank(
                    hash,
                    entry,
                    new MachineId(machineId),
                    Configuration,
                    clock.UtcNow)).ToArray();
            var importantMachineCount = ranks.Count(rank => rank == ReplicaRank.Important);

            // We should get roughly 'configuration.DesiredReplicaRetention' important replicas.
            // The number is not exact, because when we're computing the importance we're computing a hash of the first bytes of the hash
            // plus the machine id. So it is possible that we can be slightly off here.
            var desiredReplicaCount = Configuration.DesiredReplicaRetention;
            importantMachineCount.Should().BeInRange(desiredReplicaCount - 2, desiredReplicaCount + 3);
        }

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

        private static MachineIdSet Copy(MachineIdSet source)
        {
            using (var memoryStream = new MemoryStream())
            {
                using (var writer = BuildXLWriter.Create(memoryStream, leaveOpen: true))
                {
                    source.Serialize(writer);
                }

                memoryStream.Position = 0;

                using (var reader = BuildXLReader.Create(memoryStream))
                {
                    return MachineIdSet.Deserialize(reader);
                }
            }
        }

        /// <inheritdoc />
        public EffectiveLastAccessTimeProviderTests(ITestOutputHelper output = null)
            : base(TestGlobal.Logger, output)
        {
        }
    }
}
