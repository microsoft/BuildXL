// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Storage;
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
    }
}
