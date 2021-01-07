// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Threading.Tasks;
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
            using var testDirectory = new DisposableDirectory(FileSystem);
            TestDistributedCacheServiceTrackerCore(testDirectory.Path, serviceName: null);
        }

        [Fact]
        public async Task MultipleTrackersCanWorkAtTheSameTime()
        {
            int trackerCount = 5;
            using var testDirectory = new DisposableDirectory(FileSystem);
            var tasks = Enumerable.Range(1, trackerCount)
                .Select(n => Task.Run(() => TestDistributedCacheServiceTrackerCore(testDirectory.Path, serviceName:n.ToString()))).ToList();
            await Task.WhenAll(tasks);

            Output.WriteLine("Done!");
        }

        private void TestDistributedCacheServiceTrackerCore(AbsolutePath logFilePath, string serviceName)
        {
            var context = new OperationContext(new Context(Logger));

            // Intentionally making the interval longer to test the logic manually instead of relying on the tracker's
            // logic to write to the file when the timer fires.
            var logIntervalSeconds = TimeSpan.FromSeconds(10_000);
            var expectedOfflineTime = 10;
            var testClock = new MemoryClock();

            using (var tracker = ServiceOfflineDurationTracker.Create(context, testClock, FileSystem, logIntervalSeconds, logFilePath, serviceName).ThrowIfFailure())
            {
                // Offline time is not available, because the file is missing.
                tracker.GetLastServiceHeartbeatTime(context).ShouldBeError();

                tracker.LogCurrentTimeStampToFile(context);

                // Moving the clock forward
                testClock.UtcNow += TimeSpan.FromSeconds(expectedOfflineTime);

                // GetLastServiceHeartbeatTime is memoized, so we can't change the state and get different results!
                tracker.GetLastServiceHeartbeatTime(context).ShouldBeError();
                // Need to create another tracker to test the behavior
                using var nestedTracker = ServiceOfflineDurationTracker.Create(context, testClock, FileSystem, logIntervalSeconds, logFilePath, serviceName).ThrowIfFailure();

                var lastHeartbeat = nestedTracker.GetLastServiceHeartbeatTime(context).ShouldBeSuccess();

                // From the previous simulated interval, we created a new file and wrote a timestamp to it.
                // Now we should be able to determine previous offlineTIme
                // Intentionally converting the offline time to int to simplify the comparison.
                int offlineDuration = (int)testClock.UtcNow.Subtract(lastHeartbeat.Value.lastServiceHeartbeatTime).TotalSeconds;
                offlineDuration.Should().Be(expectedOfflineTime);

                lastHeartbeat.Value.shutdownCorrectly.Should().BeFalse();
            }

            // Moving the clock forward
            testClock.UtcNow += TimeSpan.FromSeconds(expectedOfflineTime);

            using (var tracker = ServiceOfflineDurationTracker.Create(context, testClock, FileSystem, logIntervalSeconds, logFilePath, serviceName).ThrowIfFailure())
            {
                // The previous tracker should have written the files.
                var result = tracker.GetLastServiceHeartbeatTime(context).ShouldBeSuccess();
                result.Value.shutdownCorrectly.Should().BeTrue();

                // From the previous simulated interval, we created a new file and wrote a timestamp to it.
                // Now we should be able to determine previous offlineTIme
                // Intentionally converting the offline time to int to simplify the comparison.
                int offlineDuration = (int)testClock.UtcNow.Subtract(result.Value.lastServiceHeartbeatTime).TotalSeconds;
                offlineDuration.Should().Be(expectedOfflineTime);
            }
        }
    }
}
