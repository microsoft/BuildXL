// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Utilities
{
    public sealed class ContentHashKeyTests : XunitBuildXLTest
    {
        public ContentHashKeyTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void EqualKeysFromIdenticalBytes()
        {
            byte[] hash1 = CreateSequentialHash(32);
            byte[] hash2 = CreateSequentialHash(32);
            var key1 = new BinaryLogger.ContentHashKey(hash1, 0, 32);
            var key2 = new BinaryLogger.ContentHashKey(hash2, 0, 32);
            XAssert.IsTrue(key1.Equals(key2));
            XAssert.AreEqual(key1.GetHashCode(), key2.GetHashCode());
        }

        [Fact]
        public void DifferenceInLastByteDetected()
        {
            byte[] hash1 = CreateSequentialHash(32);
            byte[] hash2 = CreateSequentialHash(32);
            hash2[31] ^= 0xFF;
            var key1 = new BinaryLogger.ContentHashKey(hash1, 0, 32);
            var key2 = new BinaryLogger.ContentHashKey(hash2, 0, 32);
            XAssert.IsFalse(key1.Equals(key2));
        }

        [Theory]
        [InlineData(16)]  // MD5
        [InlineData(20)]  // SHA1
        [InlineData(32)]  // SHA256
        [InlineData(33)]  // VSO0 (only first 32 bytes are compared)
        public void SupportsAllHashLengths(int length)
        {
            byte[] hash1 = CreateSequentialHash(length);
            byte[] hash2 = CreateSequentialHash(length);
            var key1 = new BinaryLogger.ContentHashKey(hash1, 0, length);
            var key2 = new BinaryLogger.ContentHashKey(hash2, 0, length);
            XAssert.IsTrue(key1.Equals(key2));

            // Flip a byte within the first 32 — must detect
            int flipIndex = Math.Min(length, 32) - 1;
            hash2[flipIndex] ^= 0xFF;
            var key3 = new BinaryLogger.ContentHashKey(hash2, 0, length);
            XAssert.IsFalse(key1.Equals(key3));
        }

        [Theory]
        [InlineData(16)]  // MD5: 2 full longs, 0 partial bytes
        [InlineData(20)]  // SHA1: 2 full longs, 4 partial bytes
        [InlineData(25)]  // 3 full longs, 1 partial byte
        public void ShorterHashDetectsDifferenceInEveryByte(int length)
        {
            byte[] baseline = CreateSequentialHash(length);
            var baselineKey = new BinaryLogger.ContentHashKey(baseline, 0, length);

            // Flip each byte one at a time and verify inequality
            for (int i = 0; i < length; i++)
            {
                byte[] modified = CreateSequentialHash(length);
                modified[i] ^= 0xFF;
                var modifiedKey = new BinaryLogger.ContentHashKey(modified, 0, length);
                XAssert.IsFalse(baselineKey.Equals(modifiedKey), $"Failed to detect difference at byte {i} for hash length {length}");
            }
        }

        [Fact]
        public void RejectsUnsupportedLength()
        {
            byte[] data = new byte[34];
            Assert.ThrowsAny<Exception>(() => new BinaryLogger.ContentHashKey(data, 0, 34));
            Assert.ThrowsAny<Exception>(() => new BinaryLogger.ContentHashKey(data, 0, 0));
        }

        private static byte[] CreateSequentialHash(int length)
        {
            byte[] hash = new byte[length];
            for (int i = 0; i < length; i++)
            {
                hash[i] = (byte)(i + 1);
            }

            return hash;
        }
    }
}
