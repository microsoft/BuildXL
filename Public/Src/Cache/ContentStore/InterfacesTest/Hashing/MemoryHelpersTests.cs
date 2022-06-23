// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if NETCOREAPP
using System;
using System.Runtime.InteropServices;
using BuildXL.Cache.ContentStore.Hashing;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Hashing
{
    public class MemoryHelpersTests
    {
        private readonly ITestOutputHelper _helper;

        public MemoryHelpersTests(ITestOutputHelper helper) => _helper = helper;

        [Fact]
        public void AsSpanUnsafeTests()
        {
            var p = new Point { X = 42, Y = 36 };
            var bytes = MemoryHelpers.AsBytesUnsafe(p);
            var p2 = MemoryMarshal.Cast<byte, Point>(bytes)[0];

            p2.Should().Be(p);
        }

        public record struct Point
        {
            public int X { get; set; }
            public int Y { get; set; }
        }
    }
}

#endif
