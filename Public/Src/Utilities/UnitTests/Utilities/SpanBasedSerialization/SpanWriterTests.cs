// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Buffers;
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
            var sw = new SpanWriter(new ArrayBufferWriter<byte>(initialCapacity: 1), defaultSizeHint: 1);
            sw.WriteSpan(input);

            sw.WrittenBytes.Length.Should().Be(size);
            sw.WrittenBytes.ToArray().Should().BeEquivalentTo(input);

            sw = new SpanWriter(new ArrayBufferWriter<byte>(initialCapacity: 1));
            foreach (var b in input)
            {
                sw.Write(b);
            }

            sw.WrittenBytes.Length.Should().Be(size);
            sw.WrittenBytes.ToArray().Should().BeEquivalentTo(input);
        }
    }
}