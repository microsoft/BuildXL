// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BuildXL.Cache.ContentStore.Hashing;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Hashing
{
    public class ReadOnlyFixedBytesTests
    {
        [Fact]
        public void FromSpanForMaxLength()
        {
            var bytes = Enumerable.Range(0, ReadOnlyFixedBytes.MaxLength).Select(i => (byte)i).ToArray();
            var fixedBytes = ReadOnlyFixedBytes.FromSpan(bytes.AsSpan());

            var fixedBytes2 = new ReadOnlyFixedBytes(bytes.AsSpan());
            Assert.Equal(fixedBytes, fixedBytes2);
        }

        [Fact]
        public void FromSpanForMaxLengthPlus()
        {
            var bytes = Enumerable.Range(0, ReadOnlyFixedBytes.MaxLength + 1).Select(i => (byte)i).ToArray();
            var fixedBytes = ReadOnlyFixedBytes.FromSpan(bytes.AsSpan());

            var fixedBytes2 = new ReadOnlyFixedBytes(bytes.AsSpan(0, ReadOnlyFixedBytes.MaxLength));
            Assert.Equal(fixedBytes, fixedBytes2);
        }

        [Fact]
        public void FromSpanForSmallLength()
        {
            var bytes = Enumerable.Range(0, ReadOnlyFixedBytes.MaxLength - 1).Select(i => (byte)i).ToArray();
            var fixedBytes = ReadOnlyFixedBytes.FromSpan(bytes.AsSpan());

            var fixedBytes2 = new ReadOnlyFixedBytes(bytes.AsSpan());
            Assert.Equal(fixedBytes, fixedBytes2);
        }

        [Fact]
        public void ToHexTest()
        {
            var bytes = Enumerable.Range(0, ReadOnlyFixedBytes.MaxLength).Select(i => (byte)i).ToArray();
            var fixedBytes = new ReadOnlyFixedBytes(bytes);

            var string1 = fixedBytes.ToHex();

            var sb = new StringBuilder();
            fixedBytes.ToHex(sb, 0, fixedBytes.Length);
            var string2 = sb.ToString();

            Assert.Equal(string1, string2);
        }

        [Fact]
        public void LengthConstantIsExpectedValue()
        {
            Assert.Equal(33, ReadOnlyFixedBytes.MaxLength);
            Assert.Equal(66, ReadOnlyFixedBytes.MaxHexLength);
        }

        [Fact]
        public void DefaultOperatorCreatesAllZeros()
        {
            var v = default(ReadOnlyFixedBytes);
            var bytes = v.ToByteArray();
            Assert.True(bytes.All(b => b == 0));
        }

        [Fact]
        public void DefaultConstructorCreatesAllZeros()
        {
#pragma warning disable SA1129 // Do not use default value type constructor
            var v = new ReadOnlyFixedBytes();
#pragma warning restore SA1129 // Do not use default value type constructor
            var bytes = v.ToByteArray();
            Assert.True(bytes.All(b => b == 0));
        }

        [Fact]
        public void IndexerGet()
        {
            var bytes = Enumerable.Range(0, ReadOnlyFixedBytes.MaxLength).Select(i => (byte)i).ToArray();
            var fixedBytes = new ReadOnlyFixedBytes(bytes);
            foreach (var i in Enumerable.Range(0, ReadOnlyFixedBytes.MaxLength))
            {
                var b = fixedBytes[i];
                Assert.Equal((byte)i, b);
            }
        }

        [Theory]
        [InlineData("A8")]
        [InlineData("00112233445566778899aaBBccDDeeFF00112233445566778899aaBBccDDeeFF")]
        [InlineData("00112233445566778899aaBBccDDeeFF00112233445566778899aaBBccDDeeFF00112233")]
        public void TryParseSuccess(string value)
        {
            ReadOnlyFixedBytes v;
            int length;
            Assert.True(ReadOnlyFixedBytes.TryParse(value, out v, out length), value);
            Assert.Equal(value.Length / 2, length);
        }

        [Theory]
        [InlineData("zz")]
        public void TryParseFail(string value)
        {
            ReadOnlyFixedBytes v;
            int length;
            Assert.False(ReadOnlyFixedBytes.TryParse(value, out v, out length));
            Assert.Equal(0, length);
        }

        [Theory]
        [InlineData("A")]
        [InlineData("A8")]
        public void ConstructFromStringZeroPads(string value)
        {
            var v = ReadOnlyFixedBytes.Parse(value);
            Assert.Equal(ReadOnlyFixedBytes.MaxHexLength - 2, v.ToHex().LastIndexOf("00", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData("00112233445566778899aaBBccDDeeFF00112233445566778899aaBBccDDeeFF00112233")]
        public void ConstructFromStringThrowsOnTooLong(string value)
        {
            Action action = () => Assert.Null(ReadOnlyFixedBytes.Parse(value));
            ArgumentException e = Assert.Throws<ArgumentException>(action);
            Assert.Contains("too long", e.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("zz")]
        public void ConstructFromStringThrowsOnInvalid(string value)
        {
            Action action = () => Assert.Null(ReadOnlyFixedBytes.Parse(value));
            ArgumentException e = Assert.Throws<ArgumentException>(action);
            Assert.Contains("is not a recognized hex string", e.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void EqualsOther()
        {
            var v1 = ReadOnlyFixedBytes.Parse("AA");
            var v2 = ReadOnlyFixedBytes.Parse("AA");
            Assert.True(v1.Equals(v2));
        }

        [Fact]
        public void EqualsObject()
        {
            var v1 = ReadOnlyFixedBytes.Parse("AA");
            var v2 = ReadOnlyFixedBytes.Parse("AA");
            Assert.True(v1.Equals((object)v2));
        }

        [Fact]
        public void GetHashCodeEqual()
        {
            var v1 = ReadOnlyFixedBytes.Parse("AA");
            var v2 = ReadOnlyFixedBytes.Parse("AA");
            Assert.Equal(v1.GetHashCode(), v2.GetHashCode());
        }

        [Fact]
        public void GetHashCodeNotEqual()
        {
            var v1 = ReadOnlyFixedBytes.Parse("AA");
            var v2 = ReadOnlyFixedBytes.Parse("AB");
            Assert.NotEqual(v1.GetHashCode(), v2.GetHashCode());
        }

        [Fact]
        public void CompareToEqual()
        {
            var v1 = ReadOnlyFixedBytes.Parse("AA");
            var v2 = ReadOnlyFixedBytes.Parse("AA");
            Assert.Equal(0, v1.CompareTo(v2));
        }

        [Fact]
        public void CompareToLessThan()
        {
            var v1 = ReadOnlyFixedBytes.Parse("AA");
            var v2 = ReadOnlyFixedBytes.Parse("AB");
            Assert.Equal(-1, v1.CompareTo(v2));
        }

        [Fact]
        public void CompareToGreaterThan()
        {
            var v1 = ReadOnlyFixedBytes.Parse("AB");
            var v2 = ReadOnlyFixedBytes.Parse("AA");
            Assert.Equal(1, v1.CompareTo(v2));
        }

        [Fact]
        public void EqualityOperatorTrue()
        {
            var v1 = ReadOnlyFixedBytes.Parse("AA");
            var v2 = ReadOnlyFixedBytes.Parse("AA");
            Assert.True(v1 == v2);
        }

        [Fact]
        public void EqualityOperatorFalse()
        {
            var v1 = ReadOnlyFixedBytes.Parse("AA");
            var v2 = ReadOnlyFixedBytes.Parse("AB");
            Assert.False(v1 == v2);
        }

        [Fact]
        public void InequalityOperatorTrue()
        {
            var v1 = ReadOnlyFixedBytes.Parse("AA");
            var v2 = ReadOnlyFixedBytes.Parse("AB");
            Assert.True(v1 != v2);
            Assert.True(v1 < v2);
            Assert.False(v1 > v2);
            Assert.False(v2 < v1);
            Assert.True(v2 > v1);
        }

        [Fact]
        public void InequalityOperatorFalse()
        {
            var v1 = ReadOnlyFixedBytes.Parse("AA");
            var v2 = ReadOnlyFixedBytes.Parse("AA");
            Assert.False(v1 != v2);
        }

        [Fact]
        public void ToHexZeroPads()
        {
            Assert.Equal("BB0000000000000000000000000000000000000000000000000000000000000000", ReadOnlyFixedBytes.Parse("BB").ToHex());
        }

        [Fact]
        public void ToHexLimit()
        {
            Assert.Equal("BB00", ReadOnlyFixedBytes.Parse("BB").ToHex(2));
        }

        [Fact]
        public void ToHexIsUppercase()
        {
            Assert.Equal("BB00", ReadOnlyFixedBytes.Parse("bb").ToHex(2));
        }

        [Fact]
        public void ToStringIsToHex()
        {
            var v = ReadOnlyFixedBytes.Parse("ABCD");
            Assert.Equal(v.ToHex(), v.ToString());
        }

        [Fact]
        public void SerializeToString()
        {
            Assert.Equal("770000000000000000000000000000000000000000000000000000000000000000", ReadOnlyFixedBytes.Parse("77").Serialize());
        }

        [Fact]
        public void StringRoundtrip()
        {
            var v1 = ReadOnlyFixedBytes.Random();
            var serialized = v1.Serialize();
            var v2 = ReadOnlyFixedBytes.Parse(serialized);
            Assert.Equal(v1, v2);
        }

        [Fact]
        public void BufferRoundtrip()
        {
            var buffer = new byte[ReadOnlyFixedBytes.MaxLength];
            var v1 = ReadOnlyFixedBytes.Random();
            v1.Serialize(buffer);
            var v2 = new ReadOnlyFixedBytes(buffer);
            Assert.Equal(v1, v2);
        }

        [Fact]
        public void BufferRoundtripViaSpan()
        {
            var buffer = new byte[ReadOnlyFixedBytes.MaxLength];
            var v1 = ReadOnlyFixedBytes.Random();
            v1.Serialize(buffer);
            var sb = new ReadOnlySpan<byte>(buffer);
            var v2 = new ReadOnlyFixedBytes(sb);
            Assert.Equal(v1, v2);
        }

        [Fact]
        public void BufferPositiveOffsetRoundtrip()
        {
            const int offset = 3;
            var buffer = new byte[ReadOnlyFixedBytes.MaxLength + offset];
            var v1 = ReadOnlyFixedBytes.Random();
            v1.Serialize(buffer, ReadOnlyFixedBytes.MaxLength, offset);
            var v2 = new ReadOnlyFixedBytes(buffer, ReadOnlyFixedBytes.MaxLength, offset);
            Assert.Equal(v1, v2);
        }

        [Theory]
        [InlineData(10)]
        [InlineData(33)]
        public void BinaryRoundtrip(int length)
        {
            using (var ms = new MemoryStream())
            {
                using (var writer = new BinaryWriter(ms))
                {
                    var v1 = ReadOnlyFixedBytes.Random();
                    v1.Serialize(writer, length);
                    ms.Position = 0;

                    using (var reader = new BinaryReader(ms))
                    {
                        var v2 = ReadOnlyFixedBytes.ReadFrom(reader);
                        Assert.Equal(v1.ToHex(length), v2.ToHex(length));
                    }
                }
            }
        }
        
        [Fact]
        public void BinaryRoundtripWithBuffer()
        {
            using (var ms = new MemoryStream())
            {
                using (var writer = new BinaryWriter(ms))
                {
                    var buffer = new byte[100];
                    var v1 = ReadOnlyFixedBytes.Random();
                    v1.Serialize(writer, buffer);
                    ms.Position = 0;

                    using (var reader = new BinaryReader(ms))
                    {
                        var v2 = ReadOnlyFixedBytes.ReadFrom(reader);
                        Assert.Equal(v1, v2);
                    }
                }
            }
        }

        [Fact]
        public void BinaryRoundtripWithSerializeFast()
        {
            using (var ms = new MemoryStream())
            {
                using (var writer = new BinaryWriter(ms))
                {
                    var buffer = new byte[100];
                    var v1 = ReadOnlyFixedBytes.Random();
                    v1.Serialize(writer, buffer);
                    ms.Position = 0;

                    using (var reader = new BinaryReader(ms))
                    {
                        var v2 = ReadOnlyFixedBytes.ReadFrom(reader);
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
                    var fb = ReadOnlyFixedBytes.ReadFrom(reader);
                    Assert.Equal(fb[0], 0);
                }
            }
        }

        #region FixedBytesShouldContinueReadingOnPartialStreams
        [Fact]
        public void FixedBytesShouldContinueReadingOnPartialStreams()
        {
            // Depending on the underlying stream's implementation, not all requested bytes may be available at once.
            // This test makes sure that the FixedBytes class continues reading until the requested number of bytes.
            using (var testStream = new TestStream())
            {
                var testBytes = new byte[] {
                    0, 1, 2, 3, 4, 5, 6, 7, 8, 9,
                    0, 1, 2, 3, 4, 5, 6, 7, 8, 9,
                    0, 1, 2, 3, 4, 5, 6, 7, 8, 9,
                    0, 1, 2};

                // Break up the 33 bytes into 2 results
                testStream.SetResult(testBytes, new List<int>() {30, 3});

                using (var reader = new BinaryReader(testStream))
                {
                    var fb = ReadOnlyFixedBytes.ReadFrom(reader, length: 33);

                    // Make sure we got all of the expected bytes back
                    for (int i = 0; i < testBytes.Length; i++)
                    {
                        Assert.Equal(fb[i], testBytes[i]);
                    }
                }
            }
        }

        /// <summary>
        /// This stream implements the minimal surface area necessary for the <see cref="FixedBytesShouldContinueReadingOnPartialStreams"/> test.
        /// </summary>
        private class TestStream : Stream
        {
            private readonly Queue<byte> _bytes = new Queue<byte>();

            private readonly Queue<int> _byteCountToReturn = new Queue<int>();

            public override bool CanRead => true;

            public override bool CanSeek => throw new NotImplementedException();

            public override bool CanWrite => throw new NotImplementedException();

            public override long Length => throw new NotImplementedException();

            public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

            public void SetResult(byte[] bytes, List<int> byteCountsToReturn)
            {
                foreach (var b in bytes)
                {
                    _bytes.Enqueue(b);
                }

                foreach (var b in byteCountsToReturn)
                {
                    _byteCountToReturn.Enqueue(b);
                }
            }

            public override void Flush()
            {
                throw new NotImplementedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                int bytesToReturn = _byteCountToReturn.Dequeue();
                if (bytesToReturn > count)
                {
                    throw new InvalidOperationException($"Requested byte count of {count} does not match test data of: {bytesToReturn}");
                }

                for (int i=0; i < bytesToReturn; i++)
                {
                    buffer[offset + i] = _bytes.Dequeue();
                }

                return bytesToReturn;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotImplementedException();
            }

            public override void SetLength(long value)
            {
                throw new NotImplementedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }
        }
        #endregion
    }
}
