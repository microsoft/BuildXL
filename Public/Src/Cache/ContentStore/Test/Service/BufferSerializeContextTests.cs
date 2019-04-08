// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Service;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Service
{
    public class BufferSerializeContextTests
    {
        [Fact]
        public void LengthProperty()
        {
            var context = new BufferSerializeContext(new byte[0]);
            context.Length.Should().Be(0);
        }

        [Fact]
        public void OffsetPropertyInitialized()
        {
            var context = new BufferSerializeContext(new byte[0]);
            context.Offset.Should().Be(0);
        }

        [Fact]
        public void Int32Roundtrip()
        {
            var buffer = new byte[4];

            var context1 = new BufferSerializeContext(buffer);
            context1.Serialize(7);

            var context2 = new BufferSerializeContext(buffer);
            var i = context2.DeserializeInt32();

            i.Should().Be(7);
        }

        [Fact]
        public void Int64Roundtrip()
        {
            var buffer = new byte[16];
            var ticks = DateTime.UtcNow.Ticks;

            var context1 = new BufferSerializeContext(buffer);
            context1.Serialize(ticks);
            context1.Serialize(ticks + 1);

            var context2 = new BufferSerializeContext(buffer);
            var t1 = context2.DeserializeInt64();
            var t2 = context2.DeserializeInt64();

            t1.Should().Be(ticks);
            t2.Should().Be(ticks + 1);
        }

        [Fact]
        public void GuidRoundtrip()
        {
            var buffer = new byte[16];
            var id1 = new Guid("0647396E-C99F-47DB-B567-C022E953CB69");

            var context1 = new BufferSerializeContext(buffer);
            context1.Serialize(id1);

            var context2 = new BufferSerializeContext(buffer);
            var id2 = context2.DeserializeGuid();

            id2.Should().Be(id1);
        }
    }
}
