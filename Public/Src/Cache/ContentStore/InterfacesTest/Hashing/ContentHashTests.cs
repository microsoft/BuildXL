// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Hashing
{
    public partial class ContentHashTests
    {
        private static readonly byte[] Zeros = Enumerable.Repeat((byte)0, ContentHash.MaxHashByteLength).ToArray();

        private static readonly byte[] B1 =
            new List<byte> {1}.Concat(Enumerable.Repeat((byte)0, ContentHash.MaxHashByteLength - 1)).ToArray();

        private static readonly byte[] B2 =
            new List<byte> {2}.Concat(Enumerable.Repeat((byte)0, ContentHash.MaxHashByteLength - 1)).ToArray();

        [Fact]
        public void TestGetHashCodeWithDefaultInstanceShouldNotThrow()
        {
            var hash = default(ContentHash);
            var hashCode = hash.GetHashCode();
            Assert.False(hash.IsValid);
        }

        [Theory]
        [InlineData(HashType.MD5, 16)]
        [InlineData(HashType.SHA1, 20)]
        [InlineData(HashType.SHA256, 32)]
        [InlineData(HashType.Vso0, 33)]
        [InlineData(HashType.DedupNodeOrChunk, 33)]
        public void ValidByteLength(HashType hashType, int length)
        {
            var randomHash = ContentHash.Random(hashType);
            Assert.Equal(length, randomHash.ByteLength);
            Assert.True(randomHash.IsValid);
        }

        [Theory]
        [InlineData(HashType.MD5, 32)]
        [InlineData(HashType.SHA1, 40)]
        [InlineData(HashType.SHA256, 64)]
        [InlineData(HashType.Vso0, 66)]
        [InlineData(HashType.DedupNodeOrChunk, 66)]
        public void ValidStringLength(HashType hashType, int length)
        {
            Assert.Equal(length, ContentHash.Random(hashType).StringLength);
        }

        [Theory]
        [InlineData(HashType.MD5)]
        [InlineData(HashType.SHA1)]
        [InlineData(HashType.SHA256)]
        [InlineData(HashType.Vso0)]
        [InlineData(HashType.DedupNodeOrChunk)]
        public void RandomValue(HashType hashType)
        {
            var v = ContentHash.Random(hashType);
            Assert.Equal(hashType, v.HashType);

            var hashInfo = HashInfoLookup.Find(hashType);
            if (hashInfo is TaggedHashInfo taggedHashInfo)
            {
                Assert.Equal(v[hashInfo.ByteLength - 1], taggedHashInfo.AlgorithmId);
            }
        }

        [Theory]
        [InlineData(HashType.MD5)]
        [InlineData(HashType.SHA1)]
        [InlineData(HashType.SHA256)]
        [InlineData(HashType.Vso0)]
        [InlineData(HashType.DedupNodeOrChunk)]
        public void MismatchLengthThrows(HashType hashType)
        {
            var b = Enumerable.Repeat((byte)0, 15).ToArray();
            var hash = new ContentHash(hashType);
            Assert.Throws<ArgumentException>((Action)(() => hash = new ContentHash(hashType, b)));
            Assert.Equal(hashType, hash.HashType);
        }

        [Theory]
        [InlineData(HashType.MD5, HashType.SHA1)]
        [InlineData(HashType.SHA1, HashType.SHA256)]
        [InlineData(HashType.SHA256, HashType.Vso0)]
        [InlineData(HashType.Vso0, HashType.DedupNodeOrChunk)]
        [InlineData(HashType.DedupNodeOrChunk, HashType.MD5)]
        public void EqualsOtherHashType(HashType left, HashType right)
        {
            var h1 = new ContentHash(left, Zeros);
            var h2 = new ContentHash(right, Zeros);
            Assert.False(h1.Equals(h2));
        }

        [Theory]
        [InlineData(HashType.MD5)]
        [InlineData(HashType.SHA1)]
        [InlineData(HashType.SHA256)]
        [InlineData(HashType.Vso0)]
        [InlineData(HashType.DedupNodeOrChunk)]
        public void EqualsTrueReferenceType(HashType hashType)
        {
            var hash = ContentHash.Random(hashType);
            Assert.True(hash.Equals((object)hash));
        }

        [Theory]
        [InlineData(HashType.MD5)]
        [InlineData(HashType.SHA1)]
        [InlineData(HashType.SHA256)]
        [InlineData(HashType.Vso0)]
        [InlineData(HashType.DedupNodeOrChunk)]
        public void EqualsFalseReferenceType(HashType hashType)
        {
            var h1 = ContentHash.Random(hashType);
            var h2 = ContentHash.Random(hashType);
            Assert.False(h1.Equals((object)h2));
        }

        [Theory]
        [InlineData(HashType.MD5)]
        [InlineData(HashType.SHA1)]
        [InlineData(HashType.SHA256)]
        [InlineData(HashType.Vso0)]
        [InlineData(HashType.DedupNodeOrChunk)]
        public void GetHashCodeEqual(HashType hashType)
        {
            var h1 = new ContentHash(hashType, Zeros);
            var h2 = new ContentHash(hashType, Zeros);
            Assert.Equal(h1.GetHashCode(), h2.GetHashCode());
        }

        [Theory]
        [InlineData(HashType.MD5)]
        [InlineData(HashType.SHA1)]
        [InlineData(HashType.SHA256)]
        [InlineData(HashType.Vso0)]
        [InlineData(HashType.DedupNodeOrChunk)]
        public void GetHashCodeNotEqual(HashType hashType)
        {
            var h1 = ContentHash.Random(hashType);
            var h2 = ContentHash.Random(hashType);
            Assert.NotEqual(h1.GetHashCode(), h2.GetHashCode());
        }

        [Theory]
        [InlineData(HashType.MD5)]
        [InlineData(HashType.SHA1)]
        [InlineData(HashType.SHA256)]
        [InlineData(HashType.Vso0)]
        [InlineData(HashType.DedupNodeOrChunk)]
        public void CompareToEqual(HashType hashType)
        {
            var h1 = new ContentHash(hashType, Zeros);
            var h2 = new ContentHash(hashType, Zeros);
            Assert.Equal(0, h1.CompareTo(h2));
        }

        [Theory]
        [InlineData(HashType.MD5)]
        [InlineData(HashType.SHA1)]
        [InlineData(HashType.SHA256)]
        [InlineData(HashType.Vso0)]
        [InlineData(HashType.DedupNodeOrChunk)]
        public void CompareToLessThan(HashType hashType)
        {
            var h1 = new ContentHash(hashType, B1);
            var h2 = new ContentHash(hashType, B2);
            Assert.Equal(-1, h1.CompareTo(h2));
        }

        [Theory]
        [InlineData(HashType.MD5)]
        [InlineData(HashType.SHA1)]
        [InlineData(HashType.SHA256)]
        [InlineData(HashType.Vso0)]
        [InlineData(HashType.DedupNodeOrChunk)]
        public void CompareToGreaterThan(HashType hashType)
        {
            var h1 = new ContentHash(hashType, B1);
            var h2 = new ContentHash(hashType, B2);
            Assert.Equal(1, h2.CompareTo(h1));
        }

        [Theory]
        [InlineData(HashType.MD5, HashType.SHA1)]
        [InlineData(HashType.SHA1, HashType.SHA256)]
        [InlineData(HashType.SHA256, HashType.Vso0)]
        [InlineData(HashType.Vso0, HashType.DedupNodeOrChunk)]
        [InlineData(HashType.DedupNodeOrChunk, HashType.MD5)]
        public void CompareToOtherHashType(HashType left, HashType right)
        {
            var h1 = new ContentHash(left, Zeros);
            var h2 = new ContentHash(right, Zeros);
            Assert.NotEqual(0, h1.CompareTo(h2));
        }

        [Theory]
        [InlineData(HashType.MD5)]
        [InlineData(HashType.SHA1)]
        [InlineData(HashType.SHA256)]
        [InlineData(HashType.Vso0)]
        [InlineData(HashType.DedupNodeOrChunk)]
        public void EqualityOperatorTrue(HashType hashType)
        {
            var hash1 = new ContentHash(hashType, B1);
            var hash2 = new ContentHash(hashType, B1);
            Assert.True(hash1 == hash2);
        }

        [Theory]
        [InlineData(HashType.MD5)]
        [InlineData(HashType.SHA1)]
        [InlineData(HashType.SHA256)]
        [InlineData(HashType.Vso0)]
        [InlineData(HashType.DedupNodeOrChunk)]
        public void EqualityOperatorFalse(HashType hashType)
        {
            var h1 = new ContentHash(hashType, B1);
            var h2 = new ContentHash(hashType, B2);
            Assert.False(h1 == h2);
        }

        [Theory]
        [InlineData(HashType.MD5)]
        [InlineData(HashType.SHA1)]
        [InlineData(HashType.SHA256)]
        [InlineData(HashType.Vso0)]
        [InlineData(HashType.DedupNodeOrChunk)]
        public void InequalityOperatorFalse(HashType hashType)
        {
            var h1 = new ContentHash(hashType, B1);
            var h2 = new ContentHash(hashType, B1);
            Assert.False(h1 != h2);
        }

        [Theory]
        [InlineData(HashType.MD5)]
        [InlineData(HashType.SHA1)]
        [InlineData(HashType.SHA256)]
        [InlineData(HashType.Vso0)]
        [InlineData(HashType.DedupNodeOrChunk)]
        public void InequalityOperatorTrue(HashType hashType)
        {
            var h1 = new ContentHash(hashType, B1);
            var h2 = new ContentHash(hashType, B2);
            Assert.True(h1 != h2);
            Assert.True(h1 < h2);
            Assert.False(h1 > h2);
            Assert.False(h2 < h1);
            Assert.True(h2 > h1);
        }
    }
}
