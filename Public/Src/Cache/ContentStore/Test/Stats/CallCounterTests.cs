// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Stats;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Stats
{
    public class CallCounterTests
    {
        [Fact]
        public void CallCountAccumulates()
        {
            var counter = new CallCounter("name");
            counter.Completed(0);
            counter.Completed(0);

            counter.Calls.Should().Be(2);
        }

        [Fact]
        public void CallDurationAccumulates()
        {
            var counter = new CallCounter("name");
            counter.Completed(1);
            counter.Duration.Ticks.Should().Be(1);

            counter.Completed(TimeSpan.FromMilliseconds(1).Ticks);
            counter.Duration.Ticks.Should().Be(TimeSpan.FromMilliseconds(1).Ticks + 1);
            ((long)counter.Duration.TotalMilliseconds).Should().Be(1);
        }
    }
}
