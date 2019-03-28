// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using ContentStoreTest.Test;
using Xunit;

namespace ContentStoreTest.Utils
{
    public class HexUtilitiesTests : TestBase
    {
        public HexUtilitiesTests()
            : base(TestGlobal.Logger)
        {
        }

        [Fact]
        public void HexToBytesBasics()
        {
            byte[] bytes = HexUtilities.HexToBytes("0123456789ABCDEFabcdef");
            AssertBytes(new byte[] {0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF, 0xAB, 0xCD, 0xEF}, bytes);

            bytes = HexUtilities.HexToBytes(string.Empty);
            AssertBytes(new byte[0], bytes);

            // 0x prefix.
            bytes = HexUtilities.HexToBytes("0x1234");
            AssertBytes(new byte[] {0x12, 0x34}, bytes);

            // Odd number of hex digits - last should be ignored.
            bytes = HexUtilities.HexToBytes("fAbCd");
            AssertBytes(new byte[] {0xFA, 0xBC}, bytes);

            // Whitespace - should be trimmed.
            bytes = HexUtilities.HexToBytes(" ABCD\t\t");
            AssertBytes(new byte[] {0xAB, 0xCD}, bytes);

            // Whitespace + 0x
            bytes = HexUtilities.HexToBytes("    0x9876");
            AssertBytes(new byte[] {0x98, 0x76}, bytes);
        }

        [Fact]
        public void BytesToHexBasics()
        {
            string hex = HexUtilities.BytesToHex(new byte[0]);
            Assert.Equal(string.Empty, hex);

            hex =
                HexUtilities.BytesToHex(new byte[]
                {0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F});
            Assert.Equal("0102030405060708090A0B0C0D0E0F", hex);

            hex = HexUtilities.BytesToHex(new byte[] {0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF});
            Assert.Equal("0123456789ABCDEF", hex);
        }

        [Fact]
        public void HexToBytesBadChars()
        {
            const string goodChars = "0123456789ABCDEFabcdef";

            var badCharactersMistakenlyAllowed = new List<char>();

            for (char c = '!'; c <= '~'; c++)
            {
                if (goodChars.IndexOf(c) == -1)
                {
                    try
                    {
                        HexUtilities.HexToBytes(new string(new[] {c, c}));

                        // Should not get here.
                        badCharactersMistakenlyAllowed.Add(c);
                    }
                    catch (ArgumentException)
                    {
                    }
                }
            }

            Assert.Equal(0, badCharactersMistakenlyAllowed.Count);
        }

        [Fact]
        public void HexToBytesNull()
        {
            string result = HexUtilities.BytesToHex(null);
            Assert.Equal("(null)", result);
        }

        [Fact]
        public void BytesToHexNull()
        {
            byte[] result = HexUtilities.HexToBytes(null);
            Assert.NotNull(result);
            Assert.Equal(0, result.Length);
        }

        // ReSharper disable once UnusedParameter.Local
        private static void AssertBytes(byte[] expected, byte[] actual)
        {
            Assert.NotNull(actual);
            Assert.Equal(expected.Length, actual.Length);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], actual[i]);
            }
        }
    }
}
