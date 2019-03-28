// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Hashing;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Hashing
{
    public class HashInfoTests
    {
        [Fact]
        public void ConstLengths()
        {
            Assert.Equal(16, MD5HashInfo.Length);
            Assert.Equal(20, SHA1HashInfo.Length);
            Assert.Equal(32, SHA256HashInfo.Length);
            Assert.Equal(33, VsoHashInfo.Length);
            Assert.Equal(32, DedupChunkHashInfo.Length);
        }

        [Fact]
        public void ByteLengths()
        {
            Assert.Equal(16, MD5HashInfo.Instance.ByteLength);
            Assert.Equal(20, SHA1HashInfo.Instance.ByteLength);
            Assert.Equal(32, SHA256HashInfo.Instance.ByteLength);
            Assert.Equal(33, VsoHashInfo.Instance.ByteLength);
            Assert.Equal(32, DedupChunkHashInfo.Instance.ByteLength);
        }

        [Fact]
        public void EmptyHashes()
        {
            Assert.Equal("MD5:D41D8CD98F00B204E9800998ECF8427E", MD5HashInfo.Instance.EmptyHash.Serialize());
            Assert.Equal("SHA1:DA39A3EE5E6B4B0D3255BFEF95601890AFD80709", SHA1HashInfo.Instance.EmptyHash.Serialize());
            Assert.Equal(
                "SHA256:E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855",
                SHA256HashInfo.Instance.EmptyHash.Serialize());
            Assert.Equal(
                "VSO0:1E57CF2792A900D06C1CDFB3C453F35BC86F72788AA9724C96C929D1CC6B456A00",
                VsoHashInfo.Instance.EmptyHash.Serialize());
            Assert.Equal(
                "DEDUPCHUNK:CF83E1357EEFB8BDF1542850D66D8007D620E4050B5715DC83F4A921D36CE9CE",
                DedupChunkHashInfo.Instance.EmptyHash.Serialize());
        }
    }
}
