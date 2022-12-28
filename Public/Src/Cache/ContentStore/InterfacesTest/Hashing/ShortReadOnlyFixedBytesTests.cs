// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Text;
using BuildXL.Cache.ContentStore.Hashing;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Hashing
{
    public class ShortReadOnlyFixedBytesTests
    {
        [Fact]
        public void FromSpanForMaxLength()
        {
            var bytes = Enumerable.Range(0, ShortReadOnlyFixedBytes.MaxLength).Select(i => (byte)i).ToArray();
            var fixedBytes = ShortReadOnlyFixedBytes.FromSpan(bytes.AsSpan());

            var fixedBytes2 = new ShortReadOnlyFixedBytes(bytes.AsSpan());
            Assert.Equal(fixedBytes, fixedBytes2);
        }

        [Fact]
        public void FromSpanForSmallLength()
        {
            var bytes = Enumerable.Range(0, ShortReadOnlyFixedBytes.MaxLength - 1).Select(i => (byte)i).ToArray();
            var fixedBytes = ShortReadOnlyFixedBytes.FromSpan(bytes.AsSpan());

            var fixedBytes2 = new ShortReadOnlyFixedBytes(bytes.AsSpan());
            Assert.Equal(fixedBytes, fixedBytes2);
        }

        [Fact]
        public void ToHexTest()
        {
            var bytes = Enumerable.Range(0, ShortReadOnlyFixedBytes.MaxLength).Select(i => (byte)i).ToArray();
            var fixedBytes = new ShortReadOnlyFixedBytes(bytes);

            var string1 = fixedBytes.ToHex();

            var sb = new StringBuilder();
            fixedBytes.ToHex(sb, 0, fixedBytes.Length);
            var string2 = sb.ToString();

            Assert.Equal(string1, string2);
        }

        [Fact]
        public void DefaultOperatorCreatesAllZeros()
        {
            var v = default(ShortReadOnlyFixedBytes);
            var bytes = v.ToByteArray();
            Assert.True(bytes.All(b => b == 0));
        }

        [Fact]
        public void DefaultConstructorCreatesAllZeros()
        {
#pragma warning disable SA1129 // Do not use default value type constructor
            var v = new ShortReadOnlyFixedBytes();
#pragma warning restore SA1129 // Do not use default value type constructor
            var bytes = v.ToByteArray();
            Assert.True(bytes.All(b => b == 0));
        }

        [Fact]
        public void IndexerGet()
        {
            var bytes = Enumerable.Range(0, ShortReadOnlyFixedBytes.MaxLength).Select(i => (byte)i).ToArray();
            var fixedBytes = new ShortReadOnlyFixedBytes(bytes);
            foreach (var i in Enumerable.Range(0, ShortReadOnlyFixedBytes.MaxLength))
            {
                var b = fixedBytes[i];
                Assert.Equal((byte)i, b);
            }
        }

        [Fact]
        public void BufferRoundtrip()
        {
            var buffer = new byte[ShortReadOnlyFixedBytes.MaxLength];
            var v1 = ShortReadOnlyFixedBytes.FromSpan(
                ToSpan(ReadOnlyFixedBytes.Random()));
            v1.Serialize(buffer);
            var v2 = new ShortReadOnlyFixedBytes(buffer);
            Assert.Equal(v1, v2);
        }

        [Fact]
        public void BufferRoundtripViaSpan()
        {
            var buffer = new byte[ShortReadOnlyFixedBytes.MaxLength];
            var v1 = ShortReadOnlyFixedBytes.FromSpan(
                ToSpan(ReadOnlyFixedBytes.Random()));
            v1.Serialize(buffer);
            var sb = new ReadOnlySpan<byte>(buffer);
            var v2 = new ShortReadOnlyFixedBytes(sb);
            Assert.Equal(v1, v2);
        }

        [Fact]
        public void BufferPositiveOffsetRoundtrip()
        {
            const int offset = 3;
            var buffer = new byte[ShortReadOnlyFixedBytes.MaxLength + offset];
            var v1 = ShortReadOnlyFixedBytes.FromSpan(
                ToSpan(ReadOnlyFixedBytes.Random()));
            v1.Serialize(buffer, ShortReadOnlyFixedBytes.MaxLength, offset);
            var v2 = new ShortReadOnlyFixedBytes(buffer, ShortReadOnlyFixedBytes.MaxLength, offset);
            Assert.Equal(v1, v2);
        }

        private ReadOnlySpan<byte> ToSpan(in ReadOnlyFixedBytes fixedBytes)
        {
#if NETCOREAPP
            return MemoryHelpers.AsBytesUnsafe(fixedBytes);
#else
            return fixedBytes.ToByteArray();
#endif
        }
    }
}
