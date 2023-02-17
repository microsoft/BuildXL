// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Threading;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;


namespace Test.BuildXL.Utilities
{
    public sealed class StoppableTimerTests : XunitBuildXLTest
    {
        public StoppableTimerTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void ImmediateDueTime()
        {
            bool callbackCalled = false;
            StoppableTimer timer = new StoppableTimer(() => { callbackCalled = true; }, (int)TimeSpan.FromHours(1).TotalMilliseconds, (int)TimeSpan.FromHours(1).TotalMilliseconds);
            AssertTrue(!callbackCalled);

            // don't crash
            timer.Change(0, 0);

            // nearly immediately get the callback
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.Elapsed.Seconds < 1 && !callbackCalled)
            {
                Thread.Sleep(5);
            }

            AssertTrue(callbackCalled);
        }
    }
}
