// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Storage.Fingerprints;
using Xunit;

namespace Test.BuildXL.Storage
{
    public class FingerprintUtilitiesTests
    {
        private void Test(string expected, params byte[] bytes)
        {
            var actual = FingerprintUtilities.FingerprintToFileName(bytes);
            Assert.Equal(expected, actual);
        }

        private void Test(string expected, string sampleContent)
        {
            var fingerprint = FingerprintUtilities.Hash(sampleContent);
            var actual = FingerprintUtilities.FingerprintToFileName(fingerprint.ToByteArray());
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void TestSingleByte()
        {
            Test("00", 0);
        }

        [Fact]
        public void TestFiveZero()
        {
            Test("00000000", 0, 0, 0, 0, 0);
        }

        [Fact]
        public void TestAlternatingBits()
        {
            byte alternatingBits = 1 | 1 << 2 | 1 << 4 | 1 << 6;
            Test("anananan", alternatingBits, alternatingBits, alternatingBits, alternatingBits, alternatingBits);
        }

        [Fact]
        public void TestFiveMax()
        {
            Test("zzzzzzzz", 255, 255, 255, 255, 255);
        }

        [Fact]
        public void TestPadFour()
        {
            Test("zw", 255);
        }

        [Fact]
        public void TestPadThree()
        {
            Test("zzzh", 255, 255);
        }

        [Fact]
        public void TestPadTwo()
        {
            Test("zzzzy", 255, 255, 255);
        }

        [Fact]
        public void TestPadOne()
        {
            Test("zzzzzzq", 255, 255, 255, 255);
        }

        [Fact]
        public void TestLargeSingleByte()
        {
            Test("041061050q3hh5zzzw004j0", 1, 2, 3, 4, 5, 6, 7, 8, 23, 255, 255, 0, 2, 68);
        }

        [Fact]
        public void TestRealHash()
        {
            Test("krowdnr1bev4bdr92x69dgkm9rhz3oto", "ComputeSha1OfThisOne");
        }

        [Theory]
        [InlineData((uint)uint.MinValue)]
        [InlineData((uint)uint.MaxValue)]
        public void TestFirstBitsAlwaysALetter(uint value)
        {
            var identifier = FingerprintUtilities.ToIdentifier(value);
            Assert.True(Char.IsLetter(identifier[0]));

            var identifierWithExtraBits = FingerprintUtilities.ToIdentifier(value, 7);
            Assert.True(Char.IsLetter(identifierWithExtraBits[0]));
        }

        [Theory]
        [InlineData(uint.MinValue, "aaaaaa")]
        [InlineData(uint.MaxValue, "d_____")]
        [InlineData((uint)(uint.MaxValue >> 2), "a_____")]
        [InlineData(42u, "aaaaaQ")]
        [InlineData(123442u, "aaaEiY")]
        public void TestSomeValues(uint value, string expected)
        {
            Assert.Equal(expected, FingerprintUtilities.ToIdentifier(value));
        }
    }
}
