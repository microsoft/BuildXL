// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Utilities;
using Xunit;

namespace Test.BuildXL.Utilities
{
    public class StatusMessageThrottlerTests
    {
        [Theory]
        // No throttling under a minute
        [InlineData(10_000, 1, false)]
        [InlineData(10_000, 10_000, false)]
        // 1-5 min, every 10 sec
        [InlineData(4 * 60_000, 5_000, true)]
        [InlineData(4 * 60_000, 20_000, false)]
        // 5-30 min, 20 sec
        [InlineData(6 * 60_000, 10_000, true)]
        [InlineData(6 * 60_000, 25_000, false)]
        // 30-60 min, 30 sec
        [InlineData(31 * 60_000, 25_000, true)]
        [InlineData(31 * 60_000, 55_000, false)]
        // 60+ min, 60 sec
        [InlineData(61 * 60_000, 55_000, true)]
        [InlineData(61 * 60_000, 65_000, false)]
        public void EventThrottlingTests(int baseTimeAgoMs, int lastStatusLogTimeAgoMs, bool expectThrottle)
        {
            DateTime currentTime = DateTime.UtcNow;
            DateTime lastStatusLogTime = currentTime.Subtract(TimeSpan.FromMilliseconds(lastStatusLogTimeAgoMs));
            
            Assert.Equal(expectThrottle, StatusMessageThrottler.ShouldThrottleStatusUpdate(lastStatusLogTime, currentTime.Subtract(TimeSpan.FromMilliseconds(baseTimeAgoMs)), currentTime, out DateTime newLastSTatusLogTime));
            if (!expectThrottle)
            {
                Assert.Equal(newLastSTatusLogTime, currentTime);
            }
        }
    }
}
