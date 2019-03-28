// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Hashing
{
    public class FixedBytesTests
    {
        [Fact]
        public void LengthConstantIsExpectedValue()
        {
            Assert.Equal(33, FixedBytes.MaxLength);
            Assert.Equal(66, FixedBytes.MaxHexLength);
        }

        [Fact]
        public void DefaultOperatorCreatesAllZeros()
        {
            var v = default(FixedBytes);
            var bytes = v.ToByteArray();
            Assert.True(bytes.All(b => b == 0));
        }

        [Fact]
        public void DefaultConstructorCreatesAllZeros()
        {
#pragma warning disable SA1129 // Do not use default value type constructor
            var v = new FixedBytes();
#pragma warning restore SA1129 // Do not use default value type constructor
            var bytes = v.ToByteArray();
            Assert.True(bytes.All(b => b == 0));
        }

        [Fact]
        public void IndexerGet()
        {
            var bytes = Enumerable.Range(0, FixedBytes.MaxLength).Select(i => (byte)i).ToArray();
            var fixedBytes = new FixedBytes(bytes);
            foreach (var i in Enumerable.Range(0, FixedBytes.MaxLength))
            {
                var b = fixedBytes[i];
                Assert.Equal((byte)i, b);
            }
        }

        [Fact]
        public void IndexerSet()
        {
            var bytes = Enumerable.Range(0, FixedBytes.MaxLength).Select(i => (byte)i).ToArray();
            var fixedBytes = new FixedBytes(bytes);
            var newBytes = Enumerable.Range(FixedBytes.MaxLength, FixedBytes.MaxLength).Select(i => (byte)i).ToArray();
            foreach (var i in Enumerable.Range(0, FixedBytes.MaxLength))
            {
                fixedBytes[i] = newBytes[i];
            }

            Assert.Equal(fixedBytes, new FixedBytes(newBytes));
        }

        [Theory]
        [InlineData("A")]
        [InlineData("A8")]
        [InlineData("00112233445566778899aaBBccDDeeFF00112233445566778899aaBBccDDeeFF")]
        [InlineData("00112233445566778899aaBBccDDeeFF00112233445566778899aaBBccDDeeFF00112233")]
        public void TryParseSuccess(string value)
        {
            FixedBytes v;
            int length;
            Assert.True(FixedBytes.TryParse(value, out v, out length));
            Assert.Equal(value.Length / 2, length);
        }

        [Theory]
        [InlineData("zz")]
        public void TryParseFail(string value)
        {
            FixedBytes v;
            int length;
            Assert.False(FixedBytes.TryParse(value, out v, out length));
            Assert.Equal(0, length);
        }

        [Theory]
        [InlineData("A")]
        [InlineData("A8")]
        public void ConstructFromStringZeroPads(string value)
        {
            var v = new FixedBytes(value);
            Assert.Equal(FixedBytes.MaxHexLength - 2, v.ToHex().LastIndexOf("00", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData("00112233445566778899aaBBccDDeeFF00112233445566778899aaBBccDDeeFF00112233")]
        public void ConstructFromStringThrowsOnTooLong(string value)
        {
            Action action = () => Assert.Null(new FixedBytes(value));
            ArgumentException e = Assert.Throws<ArgumentException>(action);
            Assert.Contains("too long", e.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("zz")]
        public void ConstructFromStringThrowsOnInvalid(string value)
        {
            Action action = () => Assert.Null(new FixedBytes(value));
            ArgumentException e = Assert.Throws<ArgumentException>(action);
            Assert.Contains("is not a recognized hex string", e.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void EqualsOther()
        {
            var v1 = new FixedBytes("AA");
            var v2 = new FixedBytes("AA");
            Assert.True(v1.Equals(v2));
        }

        [Fact]
        public void EqualsObject()
        {
            var v1 = new FixedBytes("AA");
            var v2 = new FixedBytes("AA");
            Assert.True(v1.Equals((object)v2));
        }

        [Fact]
        public void GetHashCodeEqual()
        {
            var v1 = new FixedBytes("AA");
            var v2 = new FixedBytes("AA");
            Assert.Equal(v1.GetHashCode(), v2.GetHashCode());
        }

        [Fact]
        public void GetHashCodeNotEqual()
        {
            var v1 = new FixedBytes("AA");
            var v2 = new FixedBytes("AB");
            Assert.NotEqual(v1.GetHashCode(), v2.GetHashCode());
        }

        [Fact]
        public void CompareToEqual()
        {
            var v1 = new FixedBytes("AA");
            var v2 = new FixedBytes("AA");
            Assert.Equal(0, v1.CompareTo(v2));
        }

        [Fact]
        public void CompareToLessThan()
        {
            var v1 = new FixedBytes("AA");
            var v2 = new FixedBytes("AB");
            Assert.Equal(-1, v1.CompareTo(v2));
        }

        [Fact]
        public void CompareToGreaterThan()
        {
            var v1 = new FixedBytes("AB");
            var v2 = new FixedBytes("AA");
            Assert.Equal(1, v1.CompareTo(v2));
        }

        [Fact]
        public void EqualityOperatorTrue()
        {
            var v1 = new FixedBytes("AA");
            var v2 = new FixedBytes("AA");
            Assert.True(v1 == v2);
        }

        [Fact]
        public void EqualityOperatorFalse()
        {
            var v1 = new FixedBytes("AA");
            var v2 = new FixedBytes("AB");
            Assert.False(v1 == v2);
        }

        [Fact]
        public void InequalityOperatorTrue()
        {
            var v1 = new FixedBytes("AA");
            var v2 = new FixedBytes("AB");
            Assert.True(v1 != v2);
            Assert.True(v1 < v2);
            Assert.False(v1 > v2);
            Assert.False(v2 < v1);
            Assert.True(v2 > v1);
        }

        [Fact]
        public void InequalityOperatorFalse()
        {
            var v1 = new FixedBytes("AA");
            var v2 = new FixedBytes("AA");
            Assert.False(v1 != v2);
        }

        [Fact]
        public void ToHexZeroPads()
        {
            Assert.Equal("BB0000000000000000000000000000000000000000000000000000000000000000", new FixedBytes("BB").ToHex());
        }

        [Fact]
        public void ToHexLimit()
        {
            Assert.Equal("BB00", new FixedBytes("BB").ToHex(2));
        }

        [Fact]
        public void ToHexIsUppercase()
        {
            Assert.Equal("BB00", new FixedBytes("bb").ToHex(2));
        }

        [Fact]
        public void ToStringIsToHex()
        {
            var v = new FixedBytes("ABCD");
            Assert.Equal(v.ToHex(), v.ToString());
        }

        [Fact]
        public void SerializeToString()
        {
            Assert.Equal("770000000000000000000000000000000000000000000000000000000000000000", new FixedBytes("77").Serialize());
        }

        [Fact]
        public void StringRoundtrip()
        {
            var v1 = FixedBytes.Random();
            var serialized = v1.Serialize();
            var v2 = new FixedBytes(serialized);
            Assert.Equal(v1, v2);
        }

        [Fact]
        public void BufferRoundtrip()
        {
            var buffer = new byte[FixedBytes.MaxLength];
            var v1 = FixedBytes.Random();
            v1.Serialize(buffer);
            var v2 = new FixedBytes(buffer);
            Assert.Equal(v1, v2);
        }

        [Fact]
        public void BufferPositiveOffsetRoundtrip()
        {
            const int offset = 3;
            var buffer = new byte[FixedBytes.MaxLength + offset];
            var v1 = FixedBytes.Random();
            v1.Serialize(buffer, FixedBytes.MaxLength, offset);
            var v2 = new FixedBytes(buffer, FixedBytes.MaxLength, offset);
            Assert.Equal(v1, v2);
        }

        [Fact]
        public void BinaryRoundtrip()
        {
            using (var ms = new MemoryStream())
            {
                using (var writer = new BinaryWriter(ms))
                {
                    var v1 = FixedBytes.Random();
                    v1.Serialize(writer);
                    ms.Position = 0;

                    using (var reader = new BinaryReader(ms))
                    {
                        var v2 = new FixedBytes(reader);
                        Assert.Equal(v1, v2);
                    }
                }
            }
        }

        [Fact]
        public void FixedBytesShouldNotFailOnEmptyStream()
        {
            // The following code should not fail.
            using (var ms = new MemoryStream(new byte[0]))
            {
                using (var reader = new BinaryReader(ms))
                {
                    var fb = new FixedBytes(reader);
                    Assert.Equal(fb[0], 0);
                }
            }
        }
    }
}
