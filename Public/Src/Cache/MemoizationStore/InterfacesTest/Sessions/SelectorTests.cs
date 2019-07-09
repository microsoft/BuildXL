// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.InterfacesTest.Sessions
{
    public class SelectorTests
    {
        [Fact]
        public void EqualsObjectTrue()
        {
            var contentHash = ContentHash.Random();
            var s1 = new Selector(contentHash);
            var s2 = new Selector(contentHash) as object;
            Assert.True(s1.Equals(s2));
        }

        [Fact]
        public void EqualsTrueNullOutput()
        {
            var contentHash = ContentHash.Random();
            var s1 = new Selector(contentHash);
            var s2 = new Selector(contentHash);
            Assert.True(s1.Equals(s2));
            Assert.True(s1 == s2);
            Assert.False(s1 != s2);
        }

        [Fact]
        public void EqualsFalseNullOutput()
        {
            var s1 = Selector.Random();
            var s2 = Selector.Random();
            Assert.False(s1.Equals(s2));
            Assert.False(s1 == s2);
            Assert.True(s1 != s2);
        }

        [Fact]
        public void EqualsTrueWithOutput()
        {
            var contentHash = ContentHash.Random();
            var s1 = new Selector(contentHash, new byte[] {1, 2, 3});
            var s2 = new Selector(contentHash, new byte[] {1, 2, 3});
            Assert.True(s1.Equals(s2));
            Assert.True(s1 == s2);
            Assert.False(s1 != s2);
        }

        [Fact]
        public void EqualsFalseWithOutput()
        {
            var contentHash = ContentHash.Random();
            var s1 = new Selector(contentHash, new byte[] {1, 2, 3});
            var s2 = new Selector(contentHash, new byte[] {1, 1, 3});
            Assert.False(s1.Equals(s2));
            Assert.False(s1 == s2);
            Assert.True(s1 != s2);
        }

        [Fact]
        public void GetHashCodeSameWhenEqual()
        {
            var contentHash = ContentHash.Random();
            var s1 = new Selector(contentHash);
            var s2 = new Selector(contentHash);
            Assert.Equal(s1.GetHashCode(), s2.GetHashCode());
        }

        [Fact]
        public void GetHashCodeNotSameWhenNotEqual()
        {
            var s1 = Selector.Random();
            var s2 = Selector.Random();
            Assert.NotEqual(s1.GetHashCode(), s2.GetHashCode());
        }

        [Fact]
        public void RandomNullOutput()
        {
            Assert.Null(Selector.Random(HashType.SHA1, 0).Output);
        }

        [Fact]
        public void RandomWithOutput()
        {
            Assert.NotNull(Selector.Random().Output);
        }

        [Fact]
        public void SerializeRoundtrip()
        {
            var v = Selector.Random();
            using (var ms = new MemoryStream())
            {
                using (var bw = new BinaryWriter(ms))
                {
                    v.Serialize(bw);

                    ms.Position = 0;

                    using (var reader = new BinaryReader(ms))
                    {
                        var v2 = new Selector(reader);
                        Assert.Equal(v, v2);
                    }
                }
            }
        }

        [Fact]
        public void NullOutputSerializeRoundtrip()
        {
            var v = Selector.Random(outputLength: 0);
            using (var ms = new MemoryStream())
            {
                using (var bw = new BinaryWriter(ms))
                {
                    v.Serialize(bw);

                    ms.Position = 0;

                    using (var reader = new BinaryReader(ms))
                    {
                        var v2 = new Selector(reader);
                        Assert.Equal(v, v2);
                    }
                }
            }
        }
    }
}
