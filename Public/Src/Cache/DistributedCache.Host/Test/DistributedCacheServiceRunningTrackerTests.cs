// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.Host.Test
{
    public class DistributedCacheServiceRunningTracker : TestBase
    {
        public DistributedCacheServiceRunningTracker(ITestOutputHelper output = null)
            : base(TestGlobal.Logger, output)
        {
        }

        [Fact]
        public void TestDistributedCacheServiceTracker()
        {
            var context = new OperationContext(new Context(Logger));
            using var testDirectory = new DisposableDirectory(FileSystem);
            var logFilePath = testDirectory.CreateRandomFileName();

            var logIntervalSeconds = 10;
            var testClock = new MemoryClock();
            // Create a new service tracker, in the constructor we will try to get previous online time
            // Because the file was never created previously, we can not determine offlineTime
            using var testServiceTracker = new Service.ServiceOfflineDurationTracker(context, testClock, FileSystem, logIntervalSeconds, logFilePath);

            // Offline time is not available.
            testServiceTracker.GetOfflineDuration(context).ShouldBeError();
            testServiceTracker.LogTimeStampToFile(context, testClock.UtcNow.ToString());

            testClock.UtcNow += TimeSpan.FromSeconds(logIntervalSeconds);

            // From the previous simulated interval, we created a new file and wrote a timestamp to it.
            // Now we should be able to determine previous offlineTIme
            testServiceTracker.GetOfflineDuration(context).ShouldBeSuccess();
        }

        [Fact]
        public void GetTimeFromProcessStartIsNotInHours()
        {
            var context = new OperationContext(new Context(Logger));
            var timeFromProcessStart = LifetimeTrackerTracer.GetTimeFromProcessStart(context);
            timeFromProcessStart.ShouldBeSuccess();
            timeFromProcessStart.Value.Hours.Should().Be(0);
        }
    }
}
