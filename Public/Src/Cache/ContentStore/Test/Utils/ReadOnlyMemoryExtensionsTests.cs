// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Utils;
using FluentAssertions;
using Xunit;

namespace BuildXL.Cache.ContentStore.Test.Utils
{
    public class ReadOnlyMemoryExtensionsTests
    {
        [Fact]
        public void ArrayIsReused()
        {
            var array = new byte[1] { 42 };
            var memory = new ReadOnlyMemory<byte>(array);

            using var memoryStream = memory.AsMemoryStream(out var newArrayWasCreated);
            newArrayWasCreated.Should().BeFalse();
            memoryStream.ToArray().Should().BeEquivalentTo(array);
        }

        [Fact]
        public void SubArrayIsReused()
        {
            var array = new byte[] { 42, 1 };
            var memory = new ReadOnlyMemory<byte>(array, 1, 1);

            using var memoryStream = memory.AsMemoryStream(out var newArrayWasCreated);
            newArrayWasCreated.Should().BeFalse();
            memoryStream.ToArray().Should().BeEquivalentTo(memory.ToArray());
        }

        [Fact]
        public void NewArrayIsReusedForEmptyMemory()
        {
            // This case can be optimized, but for now AsMemoryStream
            // should return a new array if the empty (default) instance is passed.
            var memory = ReadOnlyMemory<byte>.Empty;

            using var memoryStream = memory.AsMemoryStream(out var newArrayWasCreated);
            newArrayWasCreated.Should().BeFalse();
            var length = memoryStream.ToArray().Length;
            length.Should().Be(0);
        }
    }
}
