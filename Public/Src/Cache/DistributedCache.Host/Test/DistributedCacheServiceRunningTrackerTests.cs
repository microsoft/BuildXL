// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
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

            // Intentionally making the interval longer to test the logic manually instead of relying on the tracker's
            // logic to write to the file when the timer fires.
            var logIntervalSeconds = 10_000;
            var expectedOfflineTime = 10;
            var testClock = new MemoryClock();

            using (var testServiceTracker = Service.ServiceOfflineDurationTracker.Create(context, testClock, FileSystem, logIntervalSeconds, logFilePath).ThrowIfFailure())
            {
                // Offline time is not available, because the file is missing.
                testServiceTracker.GetShutdownTime(context, logTimeStampToFile: false).ShouldBeError();
                testServiceTracker.LogCurrentTimeStampToFile(context);

                // Moving the clock forward
                testClock.UtcNow += TimeSpan.FromSeconds(expectedOfflineTime);

                // From the previous simulated interval, we created a new file and wrote a timestamp to it.
                // Now we should be able to determine previous offlineTIme
                // Intentionally converting the offline time to int to simplify the comparison.
                int offlineDuration = (int)testClock.UtcNow.Subtract(testServiceTracker.GetShutdownTime(context, logTimeStampToFile: false).ShouldBeSuccess().Value).TotalSeconds;
                offlineDuration.Should().Be(expectedOfflineTime);
            }

            // Making sure that once the tracker is re-created
            // we can get the right offline time.
            using (var testServiceTracker = Service.ServiceOfflineDurationTracker.Create(context, testClock, FileSystem, logIntervalSeconds, logFilePath).ThrowIfFailure())
            {
                int offlineDuration = (int)testClock.UtcNow.Subtract(testServiceTracker.GetShutdownTime(context, logTimeStampToFile: false).ShouldBeSuccess().Value).TotalSeconds;
                offlineDuration.Should().Be(expectedOfflineTime);
            }
        }
    }
}
