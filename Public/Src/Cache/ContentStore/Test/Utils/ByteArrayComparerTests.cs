// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using ContentStoreTest.Test;
using Xunit;

namespace ContentStoreTest.Utils
{
    public class ByteArrayComparerTests : TestBase
    {
        private readonly ByteArrayComparer _comparer = new ByteArrayComparer();

        public ByteArrayComparerTests()
            : base(TestGlobal.Logger)
        {
        }

        [Fact]
        public void NullsEqual()
        {
            Assert.Equal(0, _comparer.Compare(null, null));
            Assert.True(ByteArrayComparer.ArraysEqual(null, null));
            Assert.True(_comparer.Equals(null, null));
        }

        [Fact]
        public void NullsLessThanNonNulls()
        {
            var b = new byte[0];
            Assert.True(_comparer.Compare(null, b) < 0);
            Assert.True(_comparer.Compare(b, null) > 0);
            Assert.NotEqual(_comparer.GetHashCode(null), _comparer.GetHashCode(b));
        }

        [Fact]
        public void ZeroLengthArraysEqual()
        {
            Assert.Equal(0, _comparer.Compare(new byte[0], new byte[0]));
            Assert.True(_comparer.Equals(new byte[0], new byte[0]));
            Assert.Equal(_comparer.GetHashCode(new byte[0]), _comparer.GetHashCode(new byte[0]));
        }

        [Fact]
        public void UnequalLengthsNotEqual()
        {
            Assert.NotEqual(0, _comparer.Compare(new byte[] {0}, new byte[0]));
            Assert.NotEqual(0, _comparer.Compare(new byte[0], new byte[] {0}));
            Assert.False(ByteArrayComparer.ArraysEqual(new byte[0], new byte[] {0}));
            Assert.False(_comparer.Equals(new byte[0], new byte[] {0}));
            Assert.NotEqual(_comparer.GetHashCode(new byte[0]), _comparer.GetHashCode(new byte[] {0}));
        }

        [Fact]
        public void ArrayEqualToItself()
        {
            byte[] b = {0, 1, 2, 3, 4, 5, 6};
            Assert.Equal(0, _comparer.Compare(b, b));
            Assert.True(ByteArrayComparer.ArraysEqual(b, b));
            Assert.True(_comparer.Equals(b, b));
        }

        [Fact]
        public void HashCodeDifferent()
        {
            byte[] b1 = {0, 1, 2, 3, 4, 5, 6};
            byte[] b2 = {0, 1, 2, 3, 4, 5, 99};
            byte[] b3 = {255, 200, 192, 87, 101, 55};
            byte[] b4 = {0, 1, 2, 3, 4, 5, 6, 7};
            byte[] b5 = {11};

            Assert.NotEqual(_comparer.GetHashCode(b1), _comparer.GetHashCode(b2));
            Assert.NotEqual(_comparer.GetHashCode(b1), _comparer.GetHashCode(b3));
            Assert.NotEqual(_comparer.GetHashCode(b1), _comparer.GetHashCode(b4));
            Assert.NotEqual(_comparer.GetHashCode(b1), _comparer.GetHashCode(b5));

            Assert.NotEqual(_comparer.GetHashCode(b2), _comparer.GetHashCode(b3));
            Assert.NotEqual(_comparer.GetHashCode(b2), _comparer.GetHashCode(b4));
            Assert.NotEqual(_comparer.GetHashCode(b2), _comparer.GetHashCode(b5));

            Assert.NotEqual(_comparer.GetHashCode(b3), _comparer.GetHashCode(b4));
            Assert.NotEqual(_comparer.GetHashCode(b3), _comparer.GetHashCode(b5));

            Assert.NotEqual(_comparer.GetHashCode(b4), _comparer.GetHashCode(b5));
        }

        [Fact]
        public void HashCodeSame()
        {
            byte[] b1 = {1};
            byte[] b2 = {1};

            Assert.Equal(_comparer.GetHashCode(b1), _comparer.GetHashCode(b2));
        }
    }
}
