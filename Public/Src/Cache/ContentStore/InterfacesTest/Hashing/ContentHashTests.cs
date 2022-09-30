// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
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

        public static IEnumerable<object[]> HashTypes => HashInfoLookup.All().Distinct().Select(i => new object[] { i.HashType });

        public static IEnumerable<object[]> HashTypesWithByteLengths => HashInfoLookup.All().Distinct().Select(i => new object[] { i.HashType, i.ByteLength });

        public static IEnumerable<object[]> HashTypesWithStringLengths => HashInfoLookup.All().Distinct().Select(i => new object[] { i.HashType, i.StringLength });

        [Fact]
        public void MurmurHasherShouldNotStuck()
        {
            // It used to be an issue with Murmur hash implementation
            // that was causing an infinite loop when the exact 65K bytes were processed.
            // This test hashes 1Mb one byte at a time and then compares that the hash is the same if
            // we hash the entire data in one shot.
            ValidateHashingByteAtATime(HashType.Murmur, 1 * 1024 * 1024);
        }

        private static void ValidateHashingByteAtATime(HashType hashType, int length)
        {
            var hasher = HashInfoLookup.GetContentHasher(hashType);

            using var token = hasher.CreateToken();
            var hashAlgorithm = token.Hasher;

            var data = new byte[] { 42 };
            for (int i = 0; i < length; i++)
            {
                hashAlgorithm.TransformBlock(data, 0, inputCount: data.Length, outputBuffer: null, outputOffset: 0);
            }

            hashAlgorithm.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            var hash = hashAlgorithm.Hash;
            Assert.NotNull(hash);

            var hashOfOneBlock = hasher.GetContentHash(Enumerable.Range(1, length).Select(_ => (byte)42).ToArray());
            Assert.Equal(hashOfOneBlock, new ContentHash(hashType, hash));
        }

        [Theory]
        [MemberData(nameof(HashTypes))]
        public void HashingByteAtATime(HashType hashType)
        {
            int[] inputLengths = new int[] {1, 2 << 15, (2 << 15) + 1, 2 << 20, (2 << 20) + 1};
            foreach (var length in inputLengths)
            {
                ValidateHashingByteAtATime(hashType, length);
            }
        }

        [Fact]
        public void TestGetHashCodeWithDefaultInstanceShouldNotThrow()
        {
            var hash = default(ContentHash);
            var hashCode = hash.GetHashCode();
            Assert.False(hash.IsValid);
        }

        [Theory]
        [MemberData(nameof(HashTypesWithByteLengths))]
        public void ValidByteLength(HashType hashType, int length)
        {
            var randomHash = ContentHash.Random(hashType);
            Assert.Equal(length, randomHash.ByteLength);
            Assert.True(randomHash.IsValid);
        }

        [Theory]
        [MemberData(nameof(HashTypesWithStringLengths))]
        public void ToHexWithLength(HashType hashType, int length)
        {
            var randomHash = ContentHash.Random(hashType);

            checkEquality(randomHash, length);
            checkEquality(randomHash, length / 2);
            checkEquality(randomHash, 2);
            checkEquality(randomHash, 1);
            checkEquality(randomHash, 0);

            static void checkEquality(ContentHash hash, int stringLength)
            {
                var hex = hash.ToHex(stringLength);
                var hex2 = hash.ToHex().Substring(0, stringLength);
                Assert.Equal(hex2, hex);
            }
        }

        [Theory]
        [MemberData(nameof(HashTypesWithStringLengths))]
        public void ValidStringLength(HashType hashType, int length)
        {
            Assert.Equal(length, ContentHash.Random(hashType).StringLength);
        }

        [Theory]
        [MemberData(nameof(HashTypes))]
        public void RandomValue(HashType hashType)
        {
            var v = ContentHash.Random(hashType);
            Assert.Equal(hashType, v.HashType);

            var hashInfo = HashInfoLookup.Find(hashType);
            if (hashInfo is TaggedHashInfo taggedHashInfo)
            {
                Assert.Equal(v[hashInfo.ByteLength - 1], taggedHashInfo.AlgorithmId);
            }

            var stringHash = v.Serialize();
            Assert.True(ContentHash.TryParse(stringHash, out var parsedHash));
            Assert.Equal(v, parsedHash);

            parsedHash = new ContentHash(stringHash);
            Assert.Equal(v, parsedHash);
        }

        [Theory]
        [MemberData(nameof(HashTypes))]
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
        [InlineData(HashType.Vso0, HashType.Dedup64K)]
        [InlineData(HashType.Vso0, HashType.Dedup1024K)]
        [InlineData(HashType.Dedup64K, HashType.MD5)]
        [InlineData(HashType.Dedup1024K, HashType.MD5)]
        public void EqualsOtherHashType(HashType left, HashType right)
        {
            var h1 = new ContentHash(left, Zeros);
            var h2 = new ContentHash(right, Zeros);
            Assert.False(h1.Equals(h2));
        }

        [Theory]
        [MemberData(nameof(HashTypes))]
        public void EqualsTrueReferenceType(HashType hashType)
        {
            var hash = ContentHash.Random(hashType);
            Assert.True(hash.Equals((object)hash));
        }

        [Theory]
        [MemberData(nameof(HashTypes))]
        public void EqualsFalseReferenceType(HashType hashType)
        {
            var h1 = ContentHash.Random(hashType);
            var h2 = ContentHash.Random(hashType);
            Assert.False(h1.Equals((object)h2));
        }

        [Theory]
        [MemberData(nameof(HashTypes))]
        public void GetHashCodeEqual(HashType hashType)
        {
            var h1 = new ContentHash(hashType, Zeros);
            var h2 = new ContentHash(hashType, Zeros);
            Assert.Equal(h1.GetHashCode(), h2.GetHashCode());
        }

        [Theory]
        [MemberData(nameof(HashTypes))]
        public void GetHashCodeNotEqual(HashType hashType)
        {
            var h1 = ContentHash.Random(hashType);
            var h2 = ContentHash.Random(hashType);
            Assert.NotEqual(h1.GetHashCode(), h2.GetHashCode());
        }

        [Theory]
        [MemberData(nameof(HashTypes))]
        public void CompareToEqual(HashType hashType)
        {
            var h1 = new ContentHash(hashType, Zeros);
            var h2 = new ContentHash(hashType, Zeros);
            Assert.Equal(0, h1.CompareTo(h2));
        }

        [Theory]
        [MemberData(nameof(HashTypes))]
        public void CompareToLessThan(HashType hashType)
        {
            var h1 = new ContentHash(hashType, B1);
            var h2 = new ContentHash(hashType, B2);
            Assert.Equal(-1, h1.CompareTo(h2));
        }

        [Theory]
        [MemberData(nameof(HashTypes))]
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
        [InlineData(HashType.Vso0, HashType.Dedup64K)]
        [InlineData(HashType.Dedup64K, HashType.MD5)]
        [InlineData(HashType.Vso0, HashType.Dedup1024K)]
        [InlineData(HashType.Dedup1024K, HashType.MD5)]
        public void CompareToOtherHashType(HashType left, HashType right)
        {
            var h1 = new ContentHash(left, Zeros);
            var h2 = new ContentHash(right, Zeros);
            Assert.NotEqual(0, h1.CompareTo(h2));
        }

        [Theory]
        [MemberData(nameof(HashTypes))]
        public void EqualityOperatorTrue(HashType hashType)
        {
            var hash1 = new ContentHash(hashType, B1);
            var hash2 = new ContentHash(hashType, B1);
            Assert.True(hash1 == hash2);
        }

        [Theory]
        [MemberData(nameof(HashTypes))]
        public void EqualityOperatorFalse(HashType hashType)
        {
            var h1 = new ContentHash(hashType, B1);
            var h2 = new ContentHash(hashType, B2);
            Assert.False(h1 == h2);
        }

        [Theory]
        [MemberData(nameof(HashTypes))]
        public void InequalityOperatorFalse(HashType hashType)
        {
            var h1 = new ContentHash(hashType, B1);
            var h2 = new ContentHash(hashType, B1);
            Assert.False(h1 != h2);
        }

        [Theory]
        [MemberData(nameof(HashTypes))]
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

        [Theory]
        [MemberData(nameof(HashTypes))]
        public void EqualContentHashRoundTripViaSpan(HashType hashType)
        {
            var h1 = ContentHash.Random(hashType);
            var b = new byte[h1.ByteLength];
            h1.SerializeHashBytes(b);
            var sb = new ReadOnlySpan<byte>(b);
            var h2 = new ContentHash(hashType, sb);

            Assert.Equal(h1, h2);
        }

        [Theory]
        [MemberData(nameof(HashTypes))]
        public void EqualContentHashRoundTripViaHexString(HashType hashType)
        {
            var h1 = ContentHash.Random(hashType);
            var hex = h1.ToHex();

            var hashInfo = HashInfoLookup.Find(hashType);
            var buffer = new byte[hashInfo.ByteLength];

            var sb = HexUtilities.HexToBytes(hex, buffer);
            var h2 = new ContentHash(hashType, sb);

            Assert.Equal(h1, h2);
        }
    }
}
