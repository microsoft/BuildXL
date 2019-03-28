// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Hashing
{
    public partial class ContentHashTests
    {
        [Theory]
        [InlineData(HashType.MD5)]
        [InlineData(HashType.SHA1)]
        [InlineData(HashType.SHA256)]
        [InlineData(HashType.Vso0)]
        [InlineData(HashType.DedupNodeOrChunk)]
        public void Indexer(HashType hashType)
        {
            var length = HashInfoLookup.Find(hashType).ByteLength;
            var bytes = Enumerable.Range(0, length).Select(i => (byte)i).ToArray();
            var contentHash = new ContentHash(hashType, bytes);
            foreach (var i in Enumerable.Range(0, length))
            {
                var b = contentHash[i];
                Assert.Equal((byte)i, b);
            }
        }

        [Theory]
        [InlineData(HashType.MD5)]
        [InlineData(HashType.SHA1)]
        [InlineData(HashType.SHA256)]
        [InlineData(HashType.Vso0)]
        [InlineData(HashType.DedupNodeOrChunk)]
        public void ToHashBytesArray(HashType hashType)
        {
            var length = HashInfoLookup.Find(hashType).ByteLength;
            var bytes = Enumerable.Range(0, length).Select(i => (byte)i).ToArray();
            var contentHash = new ContentHash(hashType, bytes);
            var exported = contentHash.ToHashByteArray();
            Assert.Equal(bytes, exported);
        }

        [Theory]
        [InlineData(HashType.MD5)]
        [InlineData(HashType.SHA1)]
        [InlineData(HashType.SHA256)]
        [InlineData(HashType.Vso0)]
        [InlineData(HashType.DedupNodeOrChunk)]
        public void RoundtripFullBuffer(HashType hashType)
        {
            var buffer = new byte[ContentHash.SerializedLength];
            var h1 = ContentHash.Random(hashType);
            h1.Serialize(buffer);
            var h2 = new ContentHash(buffer);
            Assert.Equal(hashType, h2.HashType);
            Assert.Equal(h1.ToString(), h2.ToString());
        }

        [Theory]
        [InlineData(HashType.MD5)]
        [InlineData(HashType.SHA1)]
        [InlineData(HashType.SHA256)]
        [InlineData(HashType.Vso0)]
        [InlineData(HashType.DedupNodeOrChunk)]
        public void RoundtripFullBufferPositiveOffset(HashType hashType)
        {
            const int offset = 3;
            var buffer = new byte[ContentHash.SerializedLength + offset];
            var h1 = ContentHash.Random(hashType);
            h1.Serialize(buffer, offset);
            var h2 = new ContentHash(buffer, offset);
            Assert.Equal(hashType, h2.HashType);
            Assert.Equal(h1.ToString(), h2.ToString());
        }

        [Theory]
        [InlineData(HashType.MD5)]
        [InlineData(HashType.SHA1)]
        [InlineData(HashType.SHA256)]
        [InlineData(HashType.Vso0)]
        [InlineData(HashType.DedupNodeOrChunk)]
        public void RoundtripPartialBuffer(HashType hashType)
        {
            var buffer = new byte[ContentHash.SerializedLength];
            var h1 = ContentHash.Random(hashType);
            h1.SerializeHashBytes(buffer);
            var h2 = new ContentHash(hashType, buffer);
            Assert.Equal(hashType, h2.HashType);
            Assert.Equal(h1.ToHex(), h2.ToHex());
        }

        [Theory]
        [InlineData(HashType.MD5)]
        [InlineData(HashType.SHA1)]
        [InlineData(HashType.SHA256)]
        [InlineData(HashType.Vso0)]
        [InlineData(HashType.DedupNodeOrChunk)]
        public void RoundtripPartialBufferPositiveOffset(HashType hashType)
        {
            const int offset = 5;
            var buffer = new byte[ContentHash.SerializedLength + offset];
            var h1 = ContentHash.Random(hashType);
            h1.SerializeHashBytes(buffer, offset);
            var h2 = new ContentHash(hashType, buffer, offset);
            Assert.Equal(hashType, h2.HashType);
            Assert.Equal(h1.ToHex(), h2.ToHex());
        }

        [Theory]
        [InlineData(HashType.MD5)]
        [InlineData(HashType.SHA1)]
        [InlineData(HashType.SHA256)]
        [InlineData(HashType.Vso0)]
        [InlineData(HashType.DedupNodeOrChunk)]
        public void ToFixedBytes(HashType hashType)
        {
            var length = HashInfoLookup.Find(hashType).ByteLength;
            var bytes = Enumerable.Range(0, length).Select(i => (byte)i).ToArray();
            var contentHash = new ContentHash(hashType, bytes);
            var exported = contentHash.ToFixedBytes();
            Assert.Equal(new FixedBytes(bytes), exported);
        }
    }
}
