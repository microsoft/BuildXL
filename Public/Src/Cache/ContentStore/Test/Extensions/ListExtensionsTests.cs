// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Cache.ContentStore.Extensions;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Extensions
{
    public class ListExtensionsTests : TestBase
    {
        private readonly List<int> _zero = new List<int>();
        private readonly List<int> _two = new List<int> {21, 22};
        private readonly List<int> _four = new List<int> {41, 42};
        private readonly IEqualityComparer<int> _comparer = EqualityComparer<int>.Default;

        public ListExtensionsTests()
            : base(TestGlobal.Logger)
        {
        }

        [Fact]
        public void ShuffleShuffles()
        {
            var ordered = Enumerable.Range(0, 1000).ToList();
            var shuffled = Enumerable.Range(0, 1000).ToList();
            shuffled.Shuffle();

            Assert.False(ordered.SequenceEqual(shuffled));
        }

        [Fact]
        public void MergeHandlesZeroNewItems()
        {
            _two.Merge(_zero, 10, _comparer).Should().BeEquivalentTo(_two);
        }

        [Fact]
        public void MergeTotalUnderMax()
        {
            var expected = _four.Union(_two);
            _two.Merge(_four, 10, _comparer).Should().BeEquivalentTo(expected);
        }

        [Fact]
        public void MergeMaxUnderTotalButMoreThanNew()
        {
            var expected = new List<int>(_four) {_two[0]};
            _two.Merge(_four, 3, _comparer).Should().BeEquivalentTo(expected);
        }

        [Fact]
        public void MergeMaxUnderNew()
        {
            var expected = _four.Take(1).ToList();
            _two.Merge(_four, 1, _comparer).Should().BeEquivalentTo(expected);
        }

        [Fact]
        public void MergeRemovesDuplicates()
        {
            _two.Merge(_two, 10, _comparer).Should().BeEquivalentTo(_two);
        }

        [Fact]
        public void ForEachNull()
        {
            var hits = 0;
            var list = new List<string> {"one", null, "two", null, "three"};
            list.ForEachNull(i => hits++);
            hits.Should().Be(2);
        }

        [Fact]
        public void ForEachNotNull()
        {
            var hits = 0;
            var list = new List<string> {"one", null, "two", null, "three"};
            list.ForEachNotNull(i => hits++);
            hits.Should().Be(3);
        }
    }
}
