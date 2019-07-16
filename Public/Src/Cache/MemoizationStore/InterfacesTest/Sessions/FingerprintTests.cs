// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.InterfacesTest.Sessions
{
    public class FingerprintTests
    {
        [Fact]
        public void LengthConstantIsExpectedValue()
        {
            Assert.Equal(33, Fingerprint.MaxLength);
            Assert.Equal(66, Fingerprint.MaxHexLength);
        }

        [Fact]
        public void Indexer()
        {
            var bytes = Enumerable.Range(0, Fingerprint.MaxLength).Select(i => (byte)i).ToArray();
            var fingerprint = new Fingerprint(bytes);
            foreach (var i in Enumerable.Range(0, Fingerprint.MaxLength))
            {
                var b = fingerprint[i];
                Assert.Equal((byte)i, b);
            }
        }

        [Theory]
        [InlineData("A8")]
        [InlineData("00112233445566778899aaBBccDDeeFF00112233445566778899aaBBccDDeeFF")]
        [InlineData("00112233445566778899aaBBccDDeeFF00112233445566778899aaBBccDDeeFF00")]
        public void TryParseSuccess(string value)
        {
            Fingerprint v;
            Assert.True(Fingerprint.TryParse(value, out v));
            Assert.Equal(value.Length / 2, v.Length);
        }

        [Theory]
        [InlineData("zz")]
        public void TryParseFail(string value)
        {
            Fingerprint v;
            Assert.False(Fingerprint.TryParse(value, out v));
            Assert.Equal(0, v.Length);
        }

        [Theory]
        [InlineData("A")]
        [InlineData("zz")]
        [InlineData("00112233445566778899aaBBccDDeeFF00112233445566778899aaBBccDDeeFF00112233")]
        public void ConstructFromStringThrowsOnInvalid(string value)
        {
            Action action = () => Assert.Null(new Fingerprint(value));
            ArgumentException ex = Assert.Throws<ArgumentException>(action);
            Assert.Contains("is not a recognized fingerprint", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void EqualsOther()
        {
            var v1 = new Fingerprint("AA");
            var v2 = new Fingerprint("AA");
            Assert.True(v1.Equals(v2));
        }

        [Fact]
        public void EqualsObject()
        {
            var v1 = new Fingerprint("AA");
            var v2 = new Fingerprint("AA") as object;
            Assert.True(v1.Equals(v2));
        }

        [Fact]
        public void GetHashCodeEqual()
        {
            var v1 = new Fingerprint("AA");
            var v2 = new Fingerprint("AA");
            Assert.Equal(v1.GetHashCode(), v2.GetHashCode());
        }

        [Fact]
        public void GetHashCodeNotEqual()
        {
            var v1 = new Fingerprint("AA");
            var v2 = new Fingerprint("AB");
            Assert.NotEqual(v1.GetHashCode(), v2.GetHashCode());
        }

        [Fact]
        public void CompareToEqual()
        {
            var v1 = new Fingerprint("AA");
            var v2 = new Fingerprint("AA");
            Assert.Equal(0, v1.CompareTo(v2));
        }

        [Fact]
        public void CompareToLessThan()
        {
            var v1 = new Fingerprint("AA");
            var v2 = new Fingerprint("AB");
            Assert.Equal(-1, v1.CompareTo(v2));
        }

        [Fact]
        public void CompareToGreaterThan()
        {
            var v1 = new Fingerprint("AB");
            var v2 = new Fingerprint("AA");
            Assert.Equal(1, v1.CompareTo(v2));
        }

        [Fact]
        public void EqualityOperatorTrue()
        {
            var v1 = new Fingerprint("AA");
            var v2 = new Fingerprint("AA");
            Assert.True(v1 == v2);
        }

        [Fact]
        public void EqualityOperatorFalse()
        {
            var v1 = new Fingerprint("AA");
            var v2 = new Fingerprint("AB");
            Assert.False(v1 == v2);
        }

        [Fact]
        public void InequalityOperatorTrue()
        {
            var v1 = new Fingerprint("AA");
            var v2 = new Fingerprint("AB");
            Assert.True(v1 != v2);
            Assert.True(v1 != v2);
            Assert.True(v1 < v2);
            Assert.False(v1 > v2);
            Assert.False(v2 < v1);
            Assert.True(v2 > v1);
        }

        [Fact]
        public void InequalityOperatorFalse()
        {
            var v1 = new Fingerprint("AA");
            var v2 = new Fingerprint("AA");
            Assert.False(v1 != v2);
        }

        [Fact]
        public void ToHexLimit()
        {
            Assert.Equal("BB", new Fingerprint("BB").ToHex());
        }

        [Fact]
        public void ToHexIsUppercase()
        {
            Assert.Equal("BB", new Fingerprint("bb").ToHex());
        }

        [Fact]
        public void ToStringIsToHex()
        {
            var v = new Fingerprint("ABCD");
            Assert.Equal(v.ToHex(), v.ToString());
        }

        [Fact]
        public void SerializeToString()
        {
            Assert.Equal("77", new Fingerprint("77").Serialize());
        }

        [Fact]
        public void StringRoundtrip()
        {
            var v1 = Fingerprint.Random();
            var serialized = v1.Serialize();
            var v2 = new Fingerprint(serialized);
            Assert.Equal(v1, v2);
        }

        [Fact]
        public void ToByteArray()
        {
            var bytes = Enumerable.Range(0, 7).Select(i => (byte)i).ToArray();
            var fingerprint = new Fingerprint(bytes);
            var exported = fingerprint.ToByteArray();
            Assert.Equal(bytes, exported);
        }

        [Fact]
        public void ToFixedBytes()
        {
            var bytes = Enumerable.Range(0, 7).Select(i => (byte)i).ToArray();
            var fingerprint = new Fingerprint(bytes);
            var exported = fingerprint.ToFixedBytes();
            Assert.Equal(new FixedBytes(bytes), exported);
        }

        [Fact]
        public void FixedBytesRoundtrip()
        {
            var v1 = Fingerprint.Random();
            var fixedBytes = v1.ToFixedBytes();
            var v2 = new Fingerprint(fixedBytes, v1.Length);
            Assert.Equal(v1, v2);
        }

        [Fact]
        public void BufferDeterminesLength()
        {
            var buffer = new byte[4];
            var fingerprint = new Fingerprint(buffer);
            Assert.Equal(buffer.Length, fingerprint.Length);
        }

        [Fact]
        public void BufferDoesNotDetermineLength()
        {
            var buffer = new byte[256];
            var fingerprint = new Fingerprint(buffer);
            Assert.Equal(Fingerprint.MaxLength, fingerprint.Length);
        }

        [Fact]
        public void BufferRoundtrip()
        {
            var buffer = new byte[Fingerprint.MaxLength];
            var v1 = Fingerprint.Random();
            v1.Serialize(buffer);
            var v2 = new Fingerprint(buffer);
            Assert.Equal(v1, v2);
        }

        [Fact]
        public void BufferPositiveOffsetRoundtrip()
        {
            const int offset = 3;
            var buffer = new byte[Fingerprint.MaxLength + offset];
            var v1 = Fingerprint.Random();
            v1.Serialize(buffer, offset);
            var v2 = new Fingerprint(buffer, Fingerprint.MaxLength, offset);
            Assert.Equal(v1, v2);
        }

        [Fact]
        public void BinaryRoundtrip()
        {
            var value = Fingerprint.Random();
            Utilities.TestSerializationRoundtrip(value, value.Serialize, Fingerprint.Deserialize);
        }

        [Fact]
        public void PartialBinaryRoundtrip()
        {
            var value = Fingerprint.Random();
            Utilities.TestSerializationRoundtrip(value, value.SerializeBytes,
                reader => new Fingerprint(value.Length, reader));
        }
    }
}
