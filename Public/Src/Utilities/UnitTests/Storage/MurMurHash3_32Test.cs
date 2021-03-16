// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Xunit;
using BuildXL.Cache.ContentStore.Hashing;

namespace Test.BuildXL.Storage
{
    public class MurMurHash3_32Test
    {
        [Theory]
        [InlineData(0, new byte[] { 0b00000000, 0b00000000, 0b00000000, 0b00000000 })]
        [InlineData(uint.MaxValue, new byte[] { 0b11111111, 0b11111111, 0b11111111, 0b11111111 })]
        public void GetBytesTest(uint data, byte[] expectedValue)
        {
            Assert.Equal(expectedValue, new MurmurHash3_32(data).ToByteArray());
        }

        [Fact]
        public void EqualityTest()
        {
            Assert.True(MurmurHash3_32.Zero.Equals(new MurmurHash3_32(0)));
        }

        [Fact]
        public void EmptyInputIsZero()
        {
            Assert.Equal(0u, MurmurHash3_32.Create(new byte[0] {}).Hash);
        }

        [Fact]
        public void TestSomeHashes()
        {
            var bytes = new byte[1024];
            var random = new Random(Seed: 1);
            random.NextBytes(bytes);

            var hash1 = MurmurHash3_32.Create(bytes);
            Assert.Equal(new byte[] {195, 253, 92, 171} , hash1.ToByteArray());

            // Flip a byte
            bytes[8] = 0xff;
            var hash2 = MurmurHash3_32.Create(bytes);
            Assert.Equal(new byte[] {193, 111, 203, 1} , hash2.ToByteArray());
        }

    }
}
