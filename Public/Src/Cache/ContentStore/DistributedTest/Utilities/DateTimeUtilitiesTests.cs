// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Tasks;
using ContentStoreTest.Test;
using FluentAssertions;
using JetBrains.Annotations;
using Xunit;
using Xunit.Sdk;

namespace BuildXL.Cache.ContentStore.Distributed.Test.Utilities
{
    public class DateTimeUtilitiesTests
    {
        [Fact]
        public void TestCompactTime()
        {
            var time = DateTime.UtcNow;
            CompactTime compactTime = time;
            DateTime roundTripTime = compactTime.ToDateTime();
            var difference = Math.Abs((time - roundTripTime).Ticks);
            Assert.True(difference < TimeSpan.FromMinutes(2).Ticks);
        }

        [Fact]
        public void TestUnixTime()
        {
            var time = DateTime.UtcNow;
            UnixTime unixTime = time;
            DateTime roundTripTime = unixTime.ToDateTime();
            var difference = Math.Abs((time - roundTripTime).Ticks);
            Assert.True(difference < TimeSpan.FromSeconds(2).Ticks);
        }
    }
}
