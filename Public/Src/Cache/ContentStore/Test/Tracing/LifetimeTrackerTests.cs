// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Tracing;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.ContentStore.Test.Tracing
{
    public class LifetimeTrackerTests : TestWithOutput
    {
        private const string FullyInitializedMessage = "CaSaaS instance is fully initialized and ready to process requests";
        private const string UnavailableOfflineTime = "Could not determine shutdown time";
        private readonly PassThroughFileSystem _fileSystem;
        public LifetimeTrackerTests(ITestOutputHelper output)
            : base(output)
        {
            _fileSystem = new PassThroughFileSystem(TestGlobal.Logger);
        }

        [Fact]
        public void NormalStartup()
        {
            var logger = TestGlobal.Logger;
            var context = new Context(logger);

            using var testDirectory = new DisposableDirectory(_fileSystem);
            // Intentionally using a subfolder:
            LifetimeTracker.ServiceStarting(context, serviceRunningLogInterval: TimeSpan.FromMinutes(10), logFilePath: testDirectory.Path / "1");
            
            LifetimeTracker.ServiceStarted(context);
            GetFullOutput().Should().NotContain(FullyInitializedMessage);

            LifetimeTracker.ServiceReadyToProcessRequests(context);
            var output = GetFullOutput();
            output.Should().Contain(FullyInitializedMessage);
            output.Should().Contain(UnavailableOfflineTime);
        }

        [Fact]
        public void ReadyBeforeStartup()
        {
            var logger = TestGlobal.Logger;
            var context = new Context(logger);

            using var testDirectory = new DisposableDirectory(_fileSystem);
            try
            {
                LifetimeTracker.ServiceStarting(context, serviceRunningLogInterval: TimeSpan.FromMinutes(10), logFilePath: testDirectory.Path);

                LifetimeTracker.ServiceReadyToProcessRequests(context);
            
                GetFullOutput().Should().NotContain(FullyInitializedMessage);

                LifetimeTracker.ServiceStarted(context);
                var output = GetFullOutput();
                output.Should().Contain(FullyInitializedMessage);
                output.Should().Contain(UnavailableOfflineTime);
            }
            finally
            {
                LifetimeTracker.ServiceStopped(context, BoolResult.Success);
            }
        }

        [Fact]
        public void StressTest()
        {
            for (int i = 0; i < 100; i++)
            {
                TraceWithMultipleStartupAndShutdown();
            }
        }
        
        [Fact]
        public void TraceWithMultipleStartupAndShutdown()
        {
            var memoryClock = new MemoryClock();
            var logger = TestGlobal.Logger;
            var context = new Context(logger);

            var process1StartupTime = memoryClock.UtcNow;
            memoryClock.AddSeconds(10);

            using var testDirectory = new DisposableDirectory(_fileSystem);
            LifetimeTracker.ServiceStarting(context, serviceRunningLogInterval: TimeSpan.FromMinutes(10), logFilePath: testDirectory.Path, clock: memoryClock, processStartupTime: process1StartupTime);

            memoryClock.AddSeconds(10);
            LifetimeTracker.ServiceStarted(context, memoryClock);
            memoryClock.AddSeconds(10);
            LifetimeTracker.ServiceReadyToProcessRequests(context);

            // Offline time should be unavailable.
            GetFullOutput().Should().Contain(UnavailableOfflineTime);
            LifetimeTracker.ServiceStopped(context, BoolResult.Success);

            // Now we're running the same stuff the second time, like after the process restart.

            memoryClock.AddSeconds(300);
            var process2StartupTime = memoryClock.UtcNow;
            memoryClock.AddSeconds(10);
            LifetimeTracker.ServiceStarting(context, serviceRunningLogInterval: TimeSpan.FromMinutes(10), logFilePath: testDirectory.Path, clock: memoryClock, processStartupTime: process2StartupTime);
            GetFullOutput().Should().Contain("LastHeartBeatTime");

            memoryClock.AddSeconds(10);
            LifetimeTracker.ServiceStarted(context, memoryClock);
            memoryClock.AddSeconds(10);
            LifetimeTracker.ServiceReadyToProcessRequests(context);
            GetFullOutput().Should().Contain("OfflineTime=[00:05:50");
        }

        [Fact]
        public void ToStringShouldNotHaveSuccessWordInIt()
        {
            var memoryClock = new MemoryClock();
            var instance = CreateLifetimeTracker(memoryClock);
            instance.Ready(memoryClock.UtcNow);
            instance.Started(memoryClock.AddSeconds(1));

            var stringRep = instance.ToString();
            stringRep.Should().NotContain("Success");
        }

        [Fact]
        public void PropertiesAvailableAfterReadyAndStartedAreCalled()
        {
            var memoryClock = new MemoryClock();
            var instance = CreateLifetimeTracker(memoryClock);

            instance.IsFullyInitialized().Should().BeFalse();

            instance.Ready(memoryClock.UtcNow);
            instance.IsFullyInitialized().Should().BeFalse();

            var startedTime = memoryClock.AddSeconds(1);
            instance.Started(startedTime);

            instance.IsFullyInitialized().Should().BeTrue();
            instance.OfflineTime.ShouldBeSuccess();

            instance.ServiceReadyTimeUtc.Should().Be(startedTime);
        }

        [Fact]
        public void PropertiesAvailableAfterStartedAndReadyCalled()
        {
            var memoryClock = new MemoryClock();
            var instance = CreateLifetimeTracker(memoryClock);

            instance.Started(memoryClock.UtcNow);
            instance.IsFullyInitialized().Should().BeFalse();

            var readyTime = memoryClock.AddSeconds(1);
            instance.Ready(readyTime);

            instance.IsFullyInitialized().Should().BeTrue();

            instance.ServiceReadyTimeUtc.Should().Be(readyTime);
        }

        private static LifetimeTrackerHelper CreateLifetimeTracker(MemoryClock memoryClock)
        {
            var processStartTimeUtc = memoryClock.UtcNow;

            memoryClock.UtcNow += TimeSpan.FromSeconds(1);

            var instance = LifetimeTrackerHelper.Starting(memoryClock.UtcNow, processStartTimeUtc, new Result<DateTime>(processStartTimeUtc.Subtract(TimeSpan.FromMinutes(10))));
            return instance;
        }
    }
}
