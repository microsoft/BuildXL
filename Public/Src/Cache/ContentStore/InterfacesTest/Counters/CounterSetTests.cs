// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Counters
{
    class CounterSetTests
    {
        [Fact]
        public void ThrowsWhenDuplicateNamesAreAdded()
        {
            var counterSet = new CounterSet();

            Assert.Throws<ArgumentException>(AddDuplicateNames);

            void AddDuplicateNames()
            {
                counterSet.Add("theName", 0);
                counterSet.Add("theName", 9);
            }
        }

        [Fact]
        public void ThrowsWhenDuplicateMetricNamesAreAdded()
        {
            var counterSet = new CounterSet();

            Assert.Throws<ArgumentException>(AddDuplicateMetricNames);

            void AddDuplicateMetricNames()
            {
                counterSet.Add("someName", 0, "theMetricName");
                counterSet.Add("otherName", 9, "theMetricName");
            }
        }
    }
}
