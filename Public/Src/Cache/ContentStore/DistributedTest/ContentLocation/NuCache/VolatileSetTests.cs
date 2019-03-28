// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Distributed.ContentLocation.NuCache
{
    public class VolatileSetTests
    {
        [Fact]
        public void TestVolatileSet()
        {
            var clock = new MemoryClock();
            var set = new VolatileSet<int>(clock);

            set.Add(0, TimeSpan.FromSeconds(100));
            set.Add(1, TimeSpan.FromSeconds(10));

            set.Contains(1).Should().BeTrue();

            // Increment time to invalidate item
            clock.UtcNow += TimeSpan.FromSeconds(11);

            // Item 1 should be invalidated, but 0 should remain
            set.Contains(1).Should().BeFalse();
            set.Contains(0).Should().BeTrue();

            // Explicitly invalidating item should remove it
            set.Invalidate(0);
            set.Contains(0).Should().BeFalse();

            set = new VolatileSet<int>(clock);

            set.Add(1, TimeSpan.FromSeconds(10));
            set.Add(2, TimeSpan.FromSeconds(20));
            set.Add(3, TimeSpan.FromSeconds(30));
            set.Add(4, TimeSpan.FromSeconds(40));
            set.Add(5, TimeSpan.FromSeconds(50));

            clock.UtcNow += TimeSpan.FromSeconds(25);

            // Should only be able to remove items 1 and 2 since they have expired
            set.CleanStaleItems(20).Should().Be(2);

            set.Contains(1).Should().BeFalse();
            set.Contains(2).Should().BeFalse();
            set.Contains(3).Should().BeTrue();
            set.Contains(4).Should().BeTrue();
            set.Contains(5).Should().BeTrue();

            // Move time past original timeout for 3 but add with 20 seconds from current time
            // 3 should be retained
            set.Add(3, TimeSpan.FromSeconds(20));
            clock.UtcNow += TimeSpan.FromSeconds(10);
            set.Contains(3).Should().BeTrue();
        }

        [Fact]
        public void TestVolatileSetAddExpired()
        {
            var clock = new MemoryClock();
            var set = new VolatileSet<int>(clock);

            set.Add(0, TimeSpan.FromSeconds(100));

            clock.UtcNow += TimeSpan.FromSeconds(101);

            set.Add(0, TimeSpan.FromSeconds(10)).Should().BeTrue("Adding for an expired entry should return true (as if the entry was missing)");
        }
    }
}
