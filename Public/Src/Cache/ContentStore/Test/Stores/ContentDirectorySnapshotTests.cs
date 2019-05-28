using System.Collections.Generic;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Stores;
using Xunit;
using FluentAssertions;

namespace BuildXL.Cache.ContentStore.Test.Stores
{
    // TODO: fix bug 1541363
    public class ContentDirectorySnapshotTests
    {
        [Fact]
        public void SnapshotShouldNotFailWithOutOfRange()
        {
            var snapshot = new ContentDirectorySnapshot<int>();

            for (short b = 0; b <= byte.MaxValue; b++)
            {
                var data = new byte[33];
                data[0] = (byte)b;
                var hash = new ContentHash(HashType.Vso0, data);
                snapshot.Add(new PayloadFromDisk<int>(hash, 42));
            }
        }

        [Theory]
        [InlineData(100)]
        public void OrderedEnumerationIsCorrect(int snapshotSize)
        {
            var snapshot = Enumerable.Range(0, snapshotSize).Select(i => new PayloadFromDisk<int>(ContentHash.Random(), i)).OrderBy(x => x.Hash).ToList();
            var store = new ContentDirectorySnapshot<int>();
            store.Add(snapshot);

            store.Count.Should().Be(snapshotSize);
            var hashesFromStore = store.ListOrderedByHash().Select(x => x.Hash).ToList();
            hashesFromStore.SequenceEqual(snapshot.Select(x => x.Hash)).Should().BeTrue();
        }

        [Theory]
        [InlineData(100)]
        public void GroupsByHashProperly(int snapshotSize)
        {
            var snapshot = Enumerable.Range(0, snapshotSize).Select(i => new PayloadFromDisk<int>(ContentHash.Random(), i)).ToList();
            var store = new ContentDirectorySnapshot<int>();
            store.Add(snapshot);

            // We have to add these into a Dictionary because the ordering of the groups is not guaranteed to be equivalent to
            // GroupBy, nor the ordering inside the groups.
            var groups = new Dictionary<ContentHash, List<PayloadFromDisk<int>>>();
            foreach (var group in store.GroupByHash())
            {
                groups.Add(group.Key, group.OrderBy(x => x.Payload).ToList());
            }

            foreach (var group in snapshot.GroupBy(p => p.Hash))
            {
                groups.ContainsKey(group.Key).Should().BeTrue();

                // The ordering within the group may not be the same either
                var sortedPayloads = groups[group.Key].Select(x => x.Payload);
                sortedPayloads.SequenceEqual(group.OrderBy(x => x.Payload).Select(x => x.Payload));
            }
        }

    }
}
