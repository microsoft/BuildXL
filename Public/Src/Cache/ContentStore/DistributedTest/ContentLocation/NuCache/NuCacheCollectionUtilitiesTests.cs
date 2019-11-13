// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Hashing;
using FluentAssertions;
using Xunit;
using System.Collections;

namespace ContentStoreTest.Distributed.ContentLocation.NuCache
{
    public class NuCacheCollectionUtilitiesTests
    {
        [Fact]
        public void TestRandomSplitInterleave()
        {
            for (int i = 0; i < 10; i++)
            {
                TestRandomSplitInterleaveHelper();
            }
        }

        private void TestRandomSplitInterleaveHelper()
        {
            var list = Enumerable.Range(0, 100).ToList();

            var interleavedItems = list.RandomSplitInterleave().ToList();

            list.Should().BeEquivalentTo(interleavedItems);

            interleavedItems.Take(30).Should().ContainInOrder(Enumerable.Range(0, 10), "Interleaved list should contain first items of original list among its first items");

            interleavedItems.Should().NotBeAscendingInOrder("Interleaved list should not contain elements in same order as original list");
        }

        [Fact]
        public void FailureCase()
        {
            var dbContent = ParseHashes("VSO0:FB44D8C99280FFFCD6EC, VSO0:FB6011BC323115781A81, VSO0:FCCB064D560546A696B8, VSO0:FD55A681BBD094EE89E5, VSO0:FD6B504B5C87D0897CF3, VSO0:FF01B6D6974216D95256, VSO0:FF63CAE2F503EFE65EE4, VSO0:FFACE149FE72F0E13C43, VSO0:FFBF66FD3A982FE6FDA1");

            var localContent = ParseHashes("VSO0:FE963E1333065A834D00");

            var diff = NuCacheCollectionUtilities.DistinctDiffSorted(localContent, dbContent, item => item);

            var left = localContent;
            var right = dbContent;

            var leftSet = left.ToHashSet();
            var rightSet = right.ToHashSet();

            foreach (var diffValue in diff)
            {
                switch (diffValue.mode)
                {
                    case MergeMode.LeftOnly:
                        leftSet.Should().Contain(diffValue.item);
                        leftSet.Remove(diffValue.item);
                        rightSet.Should().NotContain(diffValue.item);
                        break;
                    case MergeMode.RightOnly:
                        leftSet.Contains(diffValue.item).Should().BeFalse();
                        rightSet.Contains(diffValue.item).Should().BeTrue();
                        rightSet.Remove(diffValue.item);
                        break;
                    case MergeMode.Both:
                        Assert.True(false, "DistinctDiffSorted should never return an entry which existed in both sequences");
                        break;
                }
            }

            leftSet.Should().BeEquivalentTo(rightSet, "Diff operation should select out all elements which are different in sets");
        }

        public IReadOnlyList<ShortHash> ParseHashes(string hashesString)
        {
            List<ShortHash> hashes = new List<ShortHash>();
            var hashStrings = hashesString.Split(new[] { ", " }, StringSplitOptions.None);

            foreach (var hashString in hashStrings)
            {
                var result = ContentHash.TryParse(hashString + "7EDA1A01E8C646750D9C2F9B426335A047710D556D2D00", out var hash);
                Contract.Assert(result);
                hashes.Add(new ShortHash(hash));
            }

            return hashes;
        }


        [Fact]
        public void TestDiffSorted()
        {
            var random = new Random(42);
            var left = Enumerable.Range(1, 10000).Select(_ => random.Next(1000)).OrderBy(v => v).ToList();
            var right = Enumerable.Range(1, 10000).Select(_ => random.Next(1000)).OrderBy(v => v).ToList();

            var leftSet = left.ToHashSet();
            var rightSet = right.ToHashSet();

            var diff = NuCacheCollectionUtilities.DistinctDiffSorted(left, right, item => item);
            foreach (var diffValue in diff)
            {
                switch (diffValue.mode)
                {
                    case MergeMode.LeftOnly:
                        leftSet.Should().Contain(diffValue.item);
                        leftSet.Remove(diffValue.item);
                        rightSet.Should().NotContain(diffValue.item);
                        break;
                    case MergeMode.RightOnly:
                        leftSet.Contains(diffValue.item).Should().BeFalse();
                        rightSet.Contains(diffValue.item).Should().BeTrue();
                        rightSet.Remove(diffValue.item);
                        break;
                    case MergeMode.Both:
                        Assert.True(false, "DistinctDiffSorted should never return an entry which existed in both sequences");
                        break;
                }
            }

            leftSet.Should().BeEquivalentTo(rightSet, "Diff operation should select out all elements which are different in sets");
        }

        [Fact]
        public void TestDiffSortedSimple()
        {
            var left = new[] {1, 1, 3};
            var right = new[] { 1, 2 }; ;

            var leftSet = left.ToHashSet();
            var rightSet = right.ToHashSet();

            var diff = NuCacheCollectionUtilities.DistinctDiffSorted(left, right, item => item);
            foreach (var diffValue in diff)
            {
                switch (diffValue.mode)
                {
                    case MergeMode.LeftOnly:
                        leftSet.Should().Contain(diffValue.item);
                        leftSet.Remove(diffValue.item);
                        rightSet.Should().NotContain(diffValue.item);
                        break;
                    case MergeMode.RightOnly:
                        leftSet.Contains(diffValue.item).Should().BeFalse();
                        rightSet.Contains(diffValue.item).Should().BeTrue();
                        rightSet.Remove(diffValue.item);
                        break;
                    case MergeMode.Both:
                        Assert.True(false, "DistinctDiffSorted should never return an entry which existed in both sequences");
                        break;
                }
            }

            leftSet.Should().BeEquivalentTo(rightSet, "Diff operation should select out all elements which are different in sets");
        }

        [Fact]
        public void TestSplit()
        {
            var original = Enumerable.Range(1, 10).ToList();
            var pages = original.Split(batchSize: 4).ToList();

            pages.Count().Should().Be(3);
            pages[0].Count().Should().Be(4);
            pages[1].Count().Should().Be(4);
            pages[2].Count().Should().Be(2);
        }

        [Fact]
        public void TestMergeOrdered()
        {
            var arr1 = new int[] { 1, 3, 4, 6, 7, 8 };
            var arr2 = new int[] { 2, 5, 9, 10, 11 };

            var merged = NuCacheCollectionUtilities.MergeOrdered(arr1, arr2, Comparer<int>.Default).ToList();
            var i = 1;
            merged.Count.Should().Be(11);
            foreach (var element in merged)
            {
                element.Should().Be(i++);
            }
        }

        [Fact]
        public void TestApproximateSort1()
        {
            // Since the page size is 1, we will do one query per element and return, which means the order won't be
            // changed. We also return 1 element every time.
            TestApproximateSort(
                elements: new int[] { 2, 4, 3, 5, 1, 6 },
                poolSize: 2,
                pageSize: 1,
                removalFraction: 1.0f / 10.0f,
                expectedEnumerator: new int[] { 2, 4, 3, 5, 1, 6 },
                expectedQueries: new int[] { 1, 2, 3, 4, 5, 6 });
        }

        [Fact]
        public void TestApproximateSort2()
        {
            // We always fill the pool, but it is really small, so we are only able to partially sort.
            TestApproximateSort(
                elements: new int[] { 2, 4, 3, 5, 1, 6 },
                poolSize: 2,
                pageSize: 2,
                removalFraction: 1.0f / 10.0f,
                expectedEnumerator: new int[] { 2, 3, 4, 1, 5, 6 },
                expectedQueries: new int[] { 1, 2, 3, 4, 5, 5 });
        }

        [Fact]
        public void TestApproximateSort3()
        {
            // With a larger pool and page size, we sort through it a little bit better.
            TestApproximateSort(
                elements: new int[] { 2, 4, 3, 5, 1, 6 },
                poolSize: 4,
                pageSize: 3,
                removalFraction: 1.0f / 10.0f,
                expectedEnumerator: new int[] { 2, 3, 1, 4, 5, 6 },
                expectedQueries: new int[] { 1, 2, 3, 3, 3, 3 });
        }

        [Fact]
        public void TestApproximateSort4()
        {
            // If the page size is greater or equal to the maximum displacement of an element from its true position,
            // then we obtain the true order.
            TestApproximateSort(
                elements: new int[] { 2, 4, 3, 5, 1, 6 },
                poolSize: 6,
                pageSize: 5,
                removalFraction: 1.0f / 10.0f,
                expectedEnumerator: new int[] { 1, 2, 3, 4, 5, 6 },
                expectedQueries: new int[] { 1, 2, 2, 2, 2, 2 });
        }

        private static void TestApproximateSort(int[] elements, int poolSize, int pageSize, float removalFraction, int[] expectedEnumerator, int[] expectedQueries)
        {
            var comparer = Comparer<int>.Default;
            var numQueries = 0;
            Func<IEnumerable<int>, IEnumerable<int>> query = page => { numQueries++; return page; };
            var enumerable = elements.ApproximateSort(comparer, query, poolSize, pageSize, removalFraction);
            var enumerator = enumerable.GetEnumerator();

            numQueries.Should().Be(0);

            var actualEnumerator = new int[elements.Length];
            var actualQueries = new int[elements.Length];
            for (int i = 0; i < elements.Length; ++i)
            {
                enumerator.MoveNext();
                actualQueries[i] = numQueries;
                actualEnumerator[i] = enumerator.Current;
            }

            enumerator.MoveNext().Should().Be(false);
            actualEnumerator.Should().BeEquivalentTo(expectedEnumerator);
            actualQueries.Should().BeEquivalentTo(expectedQueries);
        }
    }
}
