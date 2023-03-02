// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using BuildXL.Utilities.Serialization;
using FluentAssertions;
using Xunit;


namespace Test.BuildXL.Utilities.SpanBasedSerialization
{
    public class SerializationPoolTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(1024)]
        [InlineData(10_000)]
        public void ArrayResizes(int size)
        {
            var input = Enumerable.Range(0, size).Select(i => unchecked((byte)i)).ToArray();
            var sw = new SpanWriter(new BxlArrayBufferWriter<byte>(initialCapacity: 1), defaultSizeHint: 1);
            sw.WriteSpan(input);

            sw.WrittenBytes.Length.Should().Be(size);
            sw.WrittenBytes.ToArray().Should().BeEquivalentTo(input);

            sw = new SpanWriter(new BxlArrayBufferWriter<byte>(initialCapacity: 1));
            foreach (var b in input)
            {
                sw.Write(b);
            }

            sw.WrittenBytes.Length.Should().Be(size);
            sw.WrittenBytes.ToArray().Should().BeEquivalentTo(input);
        }

        [Fact]
        public void MultipleWriters()
        {
            // We want to make sure that multiple SpanWriter intances can be used
            // with a single buffer.
            var input = Enumerable.Range(0, 9).Select(i => unchecked((byte)i)).ToArray();

            var firstBuffer = new BxlArrayBufferWriter<byte>(initialCapacity: 10);
            var writer1 = new SpanWriter(firstBuffer, defaultSizeHint: 10);
            writer1.WriteSpan(input);

            var writer2 = new SpanWriter(firstBuffer, defaultSizeHint: 1);
            writer2.WriteSpan(input);

            var secondBuffer = new BxlArrayBufferWriter<byte>(initialCapacity: 100);
            var secondWriter = new SpanWriter(secondBuffer, defaultSizeHint: 100);
            secondWriter.WriteSpan(input);
            secondWriter.WriteSpan(input);

            firstBuffer.WrittenSpan.SequenceEqual(secondBuffer.WrittenSpan).Should().BeTrue();
        }
    }
}