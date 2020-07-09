// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.InterfacesTest;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Tracing;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.ContentStore.Test.Tracing
{
    public class LifetimeTrackerTests : TestWithOutput
    {
        public LifetimeTrackerTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void ToStringShouldNotHaveSuccessWordInIt()
        {
            var memoryClock = new MemoryClock();
            var instance = CreateLifetimeTracker(memoryClock);
            instance.Ready();

            var stringRep = instance.ToString();
            stringRep.Should().NotContain("Success");
        }

        [Fact]
        public void PropertiesAvailableAfterReadyIsCalled()
        {
            var memoryClock = new MemoryClock();
            var instance = CreateLifetimeTracker(memoryClock);

            instance.FullInitializationDuration.ShouldBeError();
            instance.OfflineTime.ShouldBeError();

            instance.Ready();

            instance.FullInitializationDuration.ShouldBeSuccess();
            instance.OfflineTime.ShouldBeSuccess();
        }

        private static LifetimeTracker CreateLifetimeTracker(MemoryClock memoryClock)
        {
            var processStartTimeUtc = memoryClock.UtcNow;

            var startupDuration = TimeSpan.FromSeconds(1);
            memoryClock.UtcNow += TimeSpan.FromSeconds(1);

            Result<DateTime> shutdownTimeUtc = memoryClock.UtcNow - TimeSpan.FromMinutes(5);

            var instance = LifetimeTracker.Started(memoryClock, startupDuration, shutdownTimeUtc, processStartTimeUtc);
            return instance;
        }
    }
}
