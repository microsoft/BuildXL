// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Hashing
{
    public partial class ContentHashTests
    {
        [Theory]
        [InlineData(HashType.MD5, "MD5:000102030405060708090A0B0C0D0E0F")]
        [InlineData(HashType.SHA1, "SHA1:000102030405060708090A0B0C0D0E0F10111213")]
        [InlineData(HashType.SHA256, "SHA256:000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F")]
        [InlineData(HashType.Vso0, "VSO0:000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F20")]
        [InlineData(HashType.DedupNodeOrChunk, "DEDUPNODEORCHUNK:000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F20")]
        public void ToStringCorrect(HashType hashType, string expected)
        {
            var hashLength = HashInfoLookup.Find(hashType).ByteLength;
            var hash = new ContentHash(hashType, Enumerable.Range(0, hashLength).Select(i => (byte)i).ToArray());
            Assert.Equal(expected, hash.ToString());
        }

        [Theory]
        [InlineData(HashType.MD5, "000102030405060708090A0B0C0D0E0F")]
        [InlineData(HashType.SHA1, "000102030405060708090A0B0C0D0E0F10111213")]
        [InlineData(HashType.SHA256, "000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F")]
        [InlineData(HashType.Vso0, "000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F20")]
        [InlineData(HashType.DedupNodeOrChunk, "000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F20")]
        public void ToHexCorrect(HashType hashType, string expected)
        {
            var hashLength = HashInfoLookup.Find(hashType).ByteLength;
            var hash = new ContentHash(hashType, Enumerable.Range(0, hashLength).Select(i => (byte)i).ToArray());
            Assert.Equal(expected, hash.ToHex());
        }

        [Theory]
        [InlineData(HashType.MD5, "MD5:000102030405060708090A0B0C0D0E0F")]
        [InlineData(HashType.SHA1, "SHA1:000102030405060708090A0B0C0D0E0F10111213")]
        [InlineData(HashType.SHA256, "SHA256:000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F")]
        [InlineData(HashType.Vso0, "VSO0:000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F20")]
        [InlineData(HashType.DedupNodeOrChunk, "DEDUPNODEORCHUNK:000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F20")]
        public void SerializeToString(HashType hashType, string expected)
        {
            var hashLength = HashInfoLookup.Find(hashType).ByteLength;
            var hash = new ContentHash(hashType, Enumerable.Range(0, hashLength).Select(i => (byte)i).ToArray());
            Assert.Equal(expected, hash.Serialize());
        }

        [Theory]
        [InlineData(HashType.MD5, "000102030405060708090A0B0C0D0E0F:MD5")]
        [InlineData(HashType.SHA1, "000102030405060708090A0B0C0D0E0F10111213:SHA1")]
        [InlineData(HashType.SHA256, "000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F:SHA256")]
        [InlineData(HashType.Vso0, "000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F20:VSO0")]
        [InlineData(HashType.DedupNodeOrChunk, "000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F20:DEDUPNODEORCHUNK")]
        public void SerializeToStringReverse(HashType hashType, string expected)
        {
            var hashLength = HashInfoLookup.Find(hashType).ByteLength;
            var hash = new ContentHash(hashType, Enumerable.Range(0, hashLength).Select(i => (byte)i).ToArray());
            Assert.Equal(expected, hash.SerializeReverse());
        }

        [Theory]
        [InlineData(HashType.MD5, "MD5:000102030405060708090A0B0C0D0E0F")]
        [InlineData(HashType.SHA1, "SHA1:000102030405060708090A0B0C0D0E0F10111213")]
        [InlineData(HashType.SHA256, "SHA256:000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F")]
        [InlineData(HashType.Vso0, "VSO0:000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F00")]
        [InlineData(HashType.DedupNodeOrChunk, "DEDUPNODEORCHUNK:000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F01")]
        public void CreateFromString(HashType hashType, string value)
        {
            var hash = new ContentHash(value);
            Assert.Equal(hashType, hash.HashType);
            Assert.Equal(value.Split(':')[1], hash.ToHex());
        }

        [Theory]
        [InlineData("MD5")]
        [InlineData("MD5:")]
        [InlineData("MD5:0")]
        [InlineData("MD5:000102030405060708090A0B0C0D0E0F0")]
        [InlineData("VSO0:0102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F2001")]
        [InlineData("DEDUPNODEORCHUNK:000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F00")]
        public void CreateFromStringThrows(string value)
        {
            Action a = () => Assert.Null(new ContentHash(value));
            ArgumentException e = Assert.Throws<ArgumentException>(a);
            Assert.Contains("is not a recognized content hash", e.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
