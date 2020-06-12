// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using ContentStoreTest.Test;
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
            var context = new Context(Logger);
            using var testDirectory = new DisposableDirectory(FileSystem);
            var logFilePath = testDirectory.CreateRandomFileName();

            var logIntervalSeconds = 10;
            var testClock = new MemoryClock();
            // Create a new service tracker, in the constructor we will try to get previous online time
            // Because the file was never created previously, we can not determine offlineTime
            using var testServiceTracker = new Service.ServiceOfflineDurationTracker(new OperationContext(context), testClock, FileSystem, logIntervalSeconds, logFilePath);

            // Offline time is not available.
            testServiceTracker.GetOfflineDuration().ShouldBeError();
            testServiceTracker.LogTimeStampToFile(new OperationContext(context), testClock.UtcNow.ToString());

            testClock.UtcNow += TimeSpan.FromSeconds(logIntervalSeconds);

            // From the previous simulated interval, we created a new file and wrote a timestamp to it.
            // Now we should be able to determine previous offlineTIme
            testServiceTracker.GetOfflineDuration().ShouldBeSuccess();
        }
    }
}
