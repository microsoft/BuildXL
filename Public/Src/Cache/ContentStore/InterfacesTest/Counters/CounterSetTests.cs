// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Utilities.Tracing;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Counters
{
    public class CounterSetTests : TestWithOutput
    {
        private enum MyCounter
        {
            [CounterType(CounterType.Stopwatch)]
            Counter1,
            Counter2,
        }
        public CounterSetTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void ToCounterSetTest()
        {
            var counterCollection = new CounterCollection<MyCounter>();
            using (counterCollection[MyCounter.Counter1].Start())
            using (counterCollection[MyCounter.Counter2].Start())
            {
                Thread.Sleep(10);
            }

            var counterSet = counterCollection.ToCounterSet();
            var counters = counterSet.ToDictionaryIntegral();
            counters.Should().ContainKey(nameof(MyCounter.Counter1) + ".Count");
            counters.Should().ContainKey(nameof(MyCounter.Counter1) + ".DurationMs");
            counters.Should().ContainKey(nameof(MyCounter.Counter2) + ".Count");

            counterSet.LogOrderedNameValuePairs(str => Output.WriteLine(str));
        }

        [Fact]
        public void ThrowsWhenDuplicateNamesAreAdded()
        {
            var counterSet = new CounterSet();

            Assert.Throws<ArgumentException>(addDuplicateNames);

            void addDuplicateNames()
            {
                counterSet.Add("theName", 0);
                counterSet.Add("theName", 9);
            }
        }

        [Fact]
        public void NoExceptionWhenDuplicateMetricNamesAreAdded()
        {
            var counterSet = new CounterSet();

            // Its ok to have counters with the same metric names but different names.
            addDuplicateMetricNames();

            void addDuplicateMetricNames()
            {
                counterSet.Add("someName", 0, "theMetricName");
                counterSet.Add("otherName", 9, "theMetricName");
            }
        }
    }
}
