// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using Xunit;

namespace BuildXL.Cache.ContentStore.Distributed.Test.Utilities
{
    public class ExpiringValueTests
    {
        [Fact]
        public void TestExpirationWithZeroTimeout()
        {
            var memoryClock = new MemoryClock();
            var entry = new ExpiringValue<int>(TimeSpan.Zero, memoryClock);

            // Should be false when the entry is first created
            Assert.False(entry.IsUpToDate());
            Assert.False(entry.TryGetValue(out _));

            entry.Update(42);
            Assert.True(entry.IsUpToDate());
            Assert.True(entry.TryGetValue(out var value));
            Assert.Equal(42, value);

            memoryClock.Increment();
            Assert.False(entry.IsUpToDate());
            Assert.False(entry.TryGetValue(out _));

            entry.Update(43);
            Assert.True(entry.TryGetValue(out value));
            Assert.Equal(43, value);
        }

        [Fact]
        public void GetValueReturnsOriginalValue()
        {
            var memoryClock = new MemoryClock();
            var entry = new ExpiringValue<string>(TimeSpan.Zero, memoryClock, "1");

            Assert.False(entry.IsUpToDate());
            Assert.False(entry.TryGetValue(out _));

            Assert.Equal("1", entry.GetValueOrDefault());

            entry.Update("2");
            Assert.Equal("2", entry.GetValueOrDefault());
            Assert.True(entry.TryGetValue(out var value));
            Assert.Equal("2", value);
        }

        [Fact]
        public void TestExpirationWithNonZeroTimeout()
        {
            var expiry = TimeSpan.FromSeconds(10);
            var memoryClock = new MemoryClock();
            var entry = new ExpiringValue<int>(expiry, memoryClock);

            Assert.False(entry.IsUpToDate());

            entry.Update(42);
            Assert.True(entry.TryGetValue(out var value));
            Assert.Equal(42, value);

            memoryClock.Increment(TimeSpan.FromTicks(expiry.Ticks / 2));
            Assert.True(entry.TryGetValue(out value));
            Assert.Equal(42, value);

            memoryClock.Increment(expiry);
            Assert.False(entry.IsUpToDate());
            Assert.False(entry.TryGetValue(out _));

            entry.Update(43);
            Assert.True(entry.TryGetValue(out value));
            Assert.Equal(43, value);
        }
    }
}
