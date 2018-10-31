// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Processes;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Processes
{
    public sealed class ProcessTimesTest : XunitBuildXLTest
    {
        public ProcessTimesTest(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void Roundtrip()
        {
            var pt = new ProcessTimes(1, 2, 3, 4);
            XAssert.AreEqual(1, pt.StartTimeUtc.ToFileTimeUtc());
            XAssert.AreEqual(2, pt.ExitTimeUtc.ToFileTimeUtc());
            XAssert.AreEqual(3, pt.PrivilegedProcessorTime.Ticks);
            XAssert.AreEqual(4, pt.UserProcessorTime.Ticks);
            XAssert.AreEqual(7, pt.TotalProcessorTime.Ticks);
        }

        [Fact]
        public void GracefullyHandledIllegalExitTime()
        {
            var pt = new ProcessTimes(1, -1, 3, 4);
            XAssert.AreEqual(DateTime.MaxValue, pt.ExitTimeUtc);
        }
    }
}
