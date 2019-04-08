// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Hashing;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Hashing
{
    public partial class ContentHashTests
    {
        [Theory]
        [InlineData("MD5:000102030405060708090A0B0C0D0E0F")]
        [InlineData("000102030405060708090A0B0C0D0E0F:MD5")]
        [InlineData("SHA1:000102030405060708090A0B0C0D0E0F10111213")]
        [InlineData("000102030405060708090A0B0C0D0E0F10111213:SHA1")]
        [InlineData("SHA256:000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F")]
        [InlineData("000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F:SHA256")]
        [InlineData("VSO0:0102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F2000")]
        [InlineData("0102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F2000:VSO0")]
        [InlineData("DEDUPNODEORCHUNK:0102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F2001")]
        [InlineData("0102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F2001:DEDUPNODEORCHUNK")]
        [InlineData("DEDUPNODEORCHUNK:0102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F2002")]
        [InlineData("0102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F2002:DEDUPNODEORCHUNK")]
        public void TryParseWithoutTypeSuccess(string value)
        {
            ContentHash hash;
            Assert.True(ContentHash.TryParse(value, out hash));
        }

        [Theory]
        [InlineData("")]
        [InlineData(":MD5")]
        [InlineData("MD5:")]
        [InlineData("invalid:invalid")]
        public void TryParseWithoutTypeFail(string value)
        {
            ContentHash hash;
            Assert.False(ContentHash.TryParse(value, out hash));
        }

        [Theory]
        [InlineData(HashType.MD5, "000102030405060708090A0B0C0D0E0F")]
        [InlineData(HashType.SHA1, "000102030405060708090A0B0C0D0E0F10111213")]
        [InlineData(HashType.SHA256, "000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F")]
        [InlineData(HashType.Vso0, "0102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F2000")]
        [InlineData(HashType.DedupNodeOrChunk, "0102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F2001")]
        [InlineData(HashType.DedupNodeOrChunk, "0102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F2002")]
        public void TryParseWithTypeSuccess(HashType hashType, string value)
        {
            ContentHash hash;
            Assert.True(ContentHash.TryParse(hashType, value, out hash));
            Assert.Equal(value, hash.ToHex());
        }

        [Theory]
        [InlineData(HashType.MD5, ":")]
        [InlineData(HashType.MD5, "no delimeter")]
        [InlineData(HashType.MD5, "000102030405060708090A0B0C0D0E0")]
        [InlineData(HashType.MD5, "000102030405060708090A0B0C0D0E000")]
        [InlineData(HashType.SHA1, "000102030405060708090A0B0C0D0E0F1011121")]
        [InlineData(HashType.SHA1, "000102030405060708090A0B0C0D0E0F101112100")]
        [InlineData(HashType.SHA256, "000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1")]
        [InlineData(HashType.SHA256, "000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E100")]
        [InlineData(HashType.Vso0, "000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1")]
        [InlineData(HashType.Vso0, "000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E100")]
        [InlineData(HashType.MD5, "000102030405060708090A0B0C0D0E0G")]
        [InlineData(HashType.SHA1, "000102030405060708090A0B0C0D0E0F1011121H")]
        [InlineData(HashType.SHA256, "000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1Y")]
        [InlineData(HashType.Vso0, "000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1Z")]
        [InlineData(HashType.DedupNodeOrChunk, "000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1")]
        [InlineData(HashType.DedupNodeOrChunk, "000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E103")]
        public void TryParseWithTypeFail(HashType hashType, string value)
        {
            ContentHash hash;
            Assert.False(ContentHash.TryParse(hashType, value, out hash));
        }
    }
}
