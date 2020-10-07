// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Utilities.Tracing;
using FluentAssertions;
using Xunit;

namespace BuildXL.Cache.ContentStore.Test.Tracing
{
    public class ActivityTrackerTests
    {
        public enum MyCounters
        {
            Value1,
            Value2,
        }

        [Fact]
        public void TestAverage()
        {
            var clock = new MemoryClock();

            var activityTracker = new ActivityTracker<MyCounters>(clock, TimeSpan.FromSeconds(10));
            var collection = new CounterCollection<MyCounters>();

            // Initial rates are (total: 0, ratePerSecond: 0)
            Dictionary<MyCounters, (long total, double ratePerSecond)> rates = activityTracker.GetRates();
            rates[MyCounters.Value1].Should().Be((0, 0));
            rates[MyCounters.Value2].Should().Be((0, 0));

            clock.AddSeconds(1);
            collection.AddToCounter(MyCounters.Value1, 1);
            collection.AddToCounter(MyCounters.Value2, 2);
            activityTracker.ProcessSnapshot(collection);

            rates = activityTracker.GetRates();

            // Totals should be changed, but the rates are still 0. We don't have enough data.
            rates[MyCounters.Value1].Should().Be((1, 0));
            rates[MyCounters.Value2].Should().Be((2, 0));

            clock.AddSeconds(1);
            collection.AddToCounter(MyCounters.Value1, 1);
            collection.AddToCounter(MyCounters.Value2, 2);
            activityTracker.ProcessSnapshot(collection);

            rates = activityTracker.GetRates();
            rates[MyCounters.Value1].Should().Be((2, 1));
            rates[MyCounters.Value2].Should().Be((4, 2));

            // Moving the time to the end of the window
            clock.AddSeconds(9);
            collection.AddToCounter(MyCounters.Value1, 2);
            collection.AddToCounter(MyCounters.Value2, 4);
            activityTracker.ProcessSnapshot(collection);

            rates = activityTracker.GetRates();
            rates[MyCounters.Value1].Should().Be((4, 0.3));
            rates[MyCounters.Value2].Should().Be((8, 0.6));

            // Move even further, should have only one record in the window.
            clock.AddSeconds(3);
            rates = activityTracker.GetRates();
            rates[MyCounters.Value1].Should().Be((4, 0));
            rates[MyCounters.Value2].Should().Be((8, 0));

            clock.AddSeconds(2); // 5 seconds since the last snapshot
            collection.AddToCounter(MyCounters.Value1, 5);
            collection.AddToCounter(MyCounters.Value2, 10);
            activityTracker.ProcessSnapshot(collection);

            rates = activityTracker.GetRates();
            rates[MyCounters.Value1].Should().Be((9, 1));
            rates[MyCounters.Value2].Should().Be((18, 2));
        }
    }
}
