// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Serialization;
using FluentAssertions;
using Xunit;

namespace Test.BuildXL.Utilities.SpanBasedSerialization
{
    public class SpanReaderExtensionTests
    {
        private const int Length = 42;

        [Fact]
        public void AdvanceMovesForward()
        {
            int length = 42;
            var source = new byte[length].AsSpan().AsReader();
            source.Advance(1);
            source.RemainingLength.Should().Be(length - 1);

            // Moving "beyond" should not throw.
            try
            {
                // Can't use 'Assert.Throw' because we can't capture a ref struct in lambdas.
                source.Advance(length);
                Assert.False(true, "The previous call should fail.");
            }
            catch(ArgumentException)
            { }

            // The length should be the same
            source.RemainingLength.Should().Be(length - 1);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(byte.MaxValue)]
        [InlineData(byte.MinValue)]
        [InlineData(42)]
        public void ReadByte(byte input)
        {
            var writer = CreateWriter(Length);
            writer.Write(input);

            SpanReader span = writer.AsSpan();

            var value = span.ReadByte();
            value.Should().Be(input);
            span.RemainingLength.Should().Be(Length - 1);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ReadBoolean(bool input)
        {
            var writer = CreateWriter(Length);
            writer.Write(input);

            SpanReader span = writer.AsSpan();

            var value = span.ReadBoolean();
            value.Should().Be(input);
            span.RemainingLength.Should().Be(Length - 1);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(int.MaxValue)]
        [InlineData(int.MinValue)]
        [InlineData(42)]
        public void ReadInt32(int input)
        {
            var writer = CreateWriter(Length);
            writer.Write(input);

            SpanReader span = writer.AsSpan();

            var value = span.ReadInt32();
            value.Should().Be(input);
            span.RemainingLength.Should().Be(Length - sizeof(int));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(long.MaxValue)]
        [InlineData(long.MinValue)]
        [InlineData(42)]
        public void ReadInt64(long input)
        {
            var writer = CreateWriter(Length);
            writer.Write(input);

            SpanReader span = writer.AsSpan();

            var value = span.ReadInt64();
            value.Should().Be(input);
            span.RemainingLength.Should().Be(Length - sizeof(long));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(uint.MaxValue)]
        [InlineData(uint.MinValue)]
        [InlineData(42)]
        public void ReadUInt32Compact(uint input)
        {
            var writer = CreateWriter(Length);
            writer.WriteCompact(input);

            //
            BuildXLReader br = new BuildXLReader(debug: false, new MemoryStream(writer.AsSpan().Remaining.ToArray()), leaveOpen: false);

            var v1 = br.ReadUInt32Compact();
            v1.Should().Be(input);

            SpanReader span = writer.AsSpan();

            var value = span.ReadUInt32Compact();
            value.Should().Be(input);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(int.MaxValue)]
        [InlineData(int.MinValue)]
        [InlineData(42)]
        public void ReadInt32Compact(int input)
        {
            var writer = CreateWriter(Length);
            writer.WriteCompact(input);
            SpanReader span = writer.AsSpan();

            int value = span.ReadInt32Compact();
            value.Should().Be(input);
        }
        
        [Theory]
        [InlineData(0)]
        [InlineData(long.MaxValue)]
        [InlineData(long.MinValue)]
        [InlineData(42)]
        public void ReadInt64Compact(long input)
        {
            var writer = CreateWriter(Length);
            writer.WriteCompact(input);
            SpanReader span = writer.AsSpan();

            long value = span.ReadInt64Compact();
            value.Should().Be(input);
        }
        
        [Fact]
        public void ReadSpan()
        {
            var writer = CreateWriter(Length);
            SpanReader span = writer.AsSpan();

            var readSpan = span.ReadSpan(10);
            readSpan.Length.Should().Be(10);
            span.RemainingLength.Should().Be(Length - 10);

            // ReadSpan can read "outside" the length of the span.
            readSpan = span.ReadSpan(Length);
            readSpan.Length.Should().Be(Length - 10);
            span.RemainingLength.Should().Be(0);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(42)]
        public void ReadArray(int arraySize)
        {
            var writer = CreateWriter(1024);

            var random = new Random(42);
            var input = Enumerable.Range(0, arraySize).Select(_ => random.Next()).ToArray();
            writer.Write(input, writer: static (writer, i) => writer.Write(i));

            var span = writer.AsSpan();
            var result = span.ReadArray(
                reader: (ref SpanReader source) => source.ReadInt32());

            result.Length.Should().Be(arraySize);
            result.Should().BeEquivalentTo(input.ToArray());
        }

        private static BuildXLWriter CreateWriter(int dataLength) => new BuildXLWriter(
            debug: false,
            stream: new MemoryStream(new byte[dataLength]),
            leaveOpen: false,
            logStats: false);
    }

    internal static class BinaryWriterHelper
    {
        public static SpanReader AsSpan(this BinaryWriter writer) => ((MemoryStream)writer.BaseStream).ToArray().AsSpan().AsReader();
    }
}