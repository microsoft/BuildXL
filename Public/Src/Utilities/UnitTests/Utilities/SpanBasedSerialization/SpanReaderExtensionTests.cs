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
            catch(InsufficientLengthException)
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
            Test(Length,
                (ref SpanWriter writer) => writer.Write(input),
                writer => writer.Write(input),
                (ref SpanReader reader) => reader.ReadByte().Should().Be(input),
                reader => reader.ReadByte().Should().Be(input),
                expectedPosition: 1
            );
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ReadBoolean(bool input)
        {
            Test(Length,
                (ref SpanWriter writer) => writer.Write(input),
                writer => writer.Write(input),
                (ref SpanReader reader) => reader.ReadBoolean().Should().Be(input),
                reader => reader.ReadBoolean().Should().Be(input),
                expectedPosition: 1
            );
        }

        [Theory]
        [InlineData(0)]
        [InlineData(int.MaxValue)]
        [InlineData(int.MinValue)]
        [InlineData(42)]
        public void Int32(int input)
        {
            Test(Length,
                (ref SpanWriter writer) => writer.Write(input),
                writer => writer.Write(input),
                (ref SpanReader reader) => reader.ReadInt32().Should().Be(input),
                reader => reader.ReadInt32().Should().Be(input),
                expectedPosition: 4
            );
        }

        [Theory]
        [InlineData(0)]
        [InlineData(long.MaxValue)]
        [InlineData(long.MinValue)]
        [InlineData(42)]
        public void Int64(long input)
        {
            Test(Length,
                (ref SpanWriter writer) => writer.Write(input),
                writer => writer.Write(input),
                (ref SpanReader reader) => reader.ReadInt64().Should().Be(input),
                reader => reader.ReadInt64().Should().Be(input),
                expectedPosition: 8
            );
        }

        [Theory]
        [InlineData(0)]
        [InlineData(uint.MaxValue)]
        [InlineData(uint.MinValue)]
        [InlineData(42)]
        public void UInt32Compact(uint input)
        {
            Test(Length,
                (ref SpanWriter writer) => writer.WriteUInt32Compact(input),
                writer => writer.WriteCompact(input),
                (ref SpanReader reader) => reader.ReadUInt32Compact().Should().Be(input),
                reader => reader.ReadUInt32Compact().Should().Be(input)
            );
        }

        [Theory]
        [InlineData(0)]
        [InlineData(int.MaxValue)]
        [InlineData(int.MinValue)]
        [InlineData(42)]
        public void Int32Compact(int input)
        {
            Test(Length,
                (ref SpanWriter writer) => writer.WriteInt32Compact(input),
                writer => writer.WriteCompact(input),
                (ref SpanReader reader) => reader.ReadInt32Compact().Should().Be(input),
                reader => reader.ReadInt32Compact().Should().Be(input)
            );
        }
        
        [Theory]
        [InlineData(0)]
        [InlineData(long.MaxValue)]
        [InlineData(long.MinValue)]
        [InlineData(42)]
        public void Int64Compact(long input)
        {
            Test(Length,
                (ref SpanWriter writer) => writer.WriteInt64Compact(input),
                writer => writer.WriteCompact(input),
                (ref SpanReader reader) => reader.ReadInt64Compact().Should().Be(input),
                reader => reader.ReadInt64Compact().Should().Be(input)
            );
        }
        
        [Fact]
        public void TestReadSpan()
        {
            var random = new Random(94);
            var input = new byte[Length];
            random.NextBytes(input);

            Test(Length,
                (ref SpanWriter writer) => writer.WriteSpan(input),
                writer => writer.Write(input),
                (ref SpanReader reader) =>
                {
                    var readSpan = reader.ReadSpan(10);
                    readSpan.Length.Should().Be(10);
                    reader.Position.Should().Be(10);
                    reader.RemainingLength.Should().Be(Length - 10);

                    // ReadSpan(allowIncomplete: true) can read "outside" the length of the span.
                    readSpan = reader.ReadSpan(Length, allowIncomplete: true);
                    readSpan.Length.Should().Be(Length - 10);
                    reader.RemainingLength.Should().Be(0);
                }
            );
        }

        [Theory]
        [InlineData(0)]
        [InlineData(42)]
        public void ReadArray(int arraySize)
        {
            var random = new Random(42);
            var input = Enumerable.Range(0, arraySize).Select(_ => random.Next()).ToArray();

            Test(1024,
                (ref SpanWriter writer) => writer.Write(input, write: static (ref SpanWriter writer, int i) => writer.Write(i)),
                writer => writer.Write(input, write: static (writer, i) => writer.Write(i)),
                (ref SpanReader reader) => reader.ReadArray((ref SpanReader source) => source.ReadInt32()).Should().BeEquivalentTo(input),
                reader => reader.ReadArray(source => source.ReadInt32()).Should().BeEquivalentTo(input)
            );
        }

        private void Test(
            int dataLength,
            WriteSpan writeSpan,
            Action<BuildXLWriter> write,
            ReadSpan readSpan,
            Action<BuildXLReader> read = null,
            int? expectedPosition = null)
        {
            var spanData = new byte[dataLength];
            var streamData = new byte[dataLength];

            using var writer = BuildXLWriter.Create(new MemoryStream(streamData), leaveOpen: false);
            using var reader = BuildXLReader.Create(new MemoryStream(streamData), leaveOpen: false);

            SpanWriter spanWriter = spanData.AsSpan();
            SpanReader spanReader = spanData.AsSpan();

            writeSpan(ref spanWriter);
            write(writer);

            if (expectedPosition != null)
            {
                spanWriter.WrittenBytes.Length.Should().Be(expectedPosition.Value);
                spanWriter.Position.Should().Be(expectedPosition.Value);
                spanWriter.RemainingLength.Should().Be(dataLength - expectedPosition.Value);
            }

            spanData.Should().BeEquivalentTo(streamData);
            spanWriter.WrittenBytes.Length.Should().Be(spanWriter.Position);
            spanWriter.Position.Should().Be((int)writer.BaseStream.Position);

            readSpan(ref spanReader);

            if (read != null)
            {
                read(reader);
                spanReader.Position.Should().Be((int)reader.BaseStream.Position);
            }
        }

        private delegate void ReadSpan(ref SpanReader reader);
        private delegate void WriteSpan(ref SpanWriter reader);
    }
}