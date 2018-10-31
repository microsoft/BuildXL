// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities.Tasks;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
#pragma warning disable AsyncFixer03 // Avoid fire & forget async void methods
    public sealed class AwaitWithProgressReportingTests : XunitBuildXLTest
    {
        public AwaitWithProgressReportingTests(ITestOutputHelper output)
            : base(output) { }

        /// <summary>
        /// Report period is significantly less than task time, so there should be more reports than in edge cases (immediately and at-end).
        /// </summary>
        [Theory]
        [InlineData(1000, 1, true, true, 2)]
        [InlineData(1000, 1, false, true, 1)]
        [InlineData(1000, 1, true, false, 1)]
        [InlineData(1000, 1, false, false, 1)]
        public void AwaitMinimumNumberOfSuccessfulReports(int taskTimeMilliseconds, int reportPeriodMilliseconds, bool reportImmediately, bool reportAtEnd, int numberOfReports)
        {
           int i = 0;
           int successValue = 42;

           var result = TaskUtilities.AwaitWithProgressReporting(
               task: TestTask(TimeSpan.FromMilliseconds(taskTimeMilliseconds), successValue),
               period: TimeSpan.FromMilliseconds(reportPeriodMilliseconds),
               action: (time) => Interlocked.Increment(ref i),
               reportImmediately: reportImmediately,
               reportAtEnd: reportAtEnd).GetAwaiter().GetResult();

           XAssert.AreEqual(successValue, result);
           XAssert.IsTrue(numberOfReports < i, $"Should find {numberOfReports} < {i}");
        }

        /// <summary>
        /// Report period is larger than task time, so only edge cases (immediately and at-end) will report.
        /// </summary>
        [Theory]
        [InlineData(200, 10000, true, true, 2)]
        [InlineData(200, 10000, false, true, 1)]
        [InlineData(200, 10000, true, false, 1)]
        [InlineData(200, 10000, false, false, 0)]
        public void AwaitPreciseNumberOfSuccessfulReports(int taskTimeMilliseconds, int reportPeriodMilliseconds, bool reportImmediately, bool reportAtEnd, int numberOfReports)
        {
            int i = 0;
            int successValue = 42;

            var result = TaskUtilities.AwaitWithProgressReporting(
                task: TestTask(TimeSpan.FromMilliseconds(taskTimeMilliseconds), successValue),
                period: TimeSpan.FromMilliseconds(reportPeriodMilliseconds),
                action: (time) => Interlocked.Increment(ref i),
                reportImmediately: reportImmediately,
                reportAtEnd: reportAtEnd).GetAwaiter().GetResult();

            XAssert.AreEqual(successValue, result);
            XAssert.AreEqual(numberOfReports, i);
        }

        [Fact]
        public void AwaitFailure()
        {
           int i = 0;
           string exceptionText = "Boom";

           try
           {
               var result = TaskUtilities.AwaitWithProgressReporting(
                   task: Task.Run(() => TestMethod(exceptionText)),
                   period: TimeSpan.FromHours(24),
                   action: (time) => Interlocked.Increment(ref i),
                   reportImmediately: false,
                   reportAtEnd: false).GetAwaiter().GetResult();
           }
           catch (Exception e)
           {
               XAssert.AreEqual(exceptionText, e.Message);
               XAssert.AreEqual(0, i);
               return;
           }

           XAssert.Fail("Should have thrown an exception");
        }

        private async Task<int> TestTask(TimeSpan delaySpan, int retVal)
        {
            await Task.Delay(delaySpan);
            return await Task.Run(() => retVal);
        }

        private int TestMethod(string exceptionText)
        {
            throw new Exception(exceptionText);
        }
    }
#pragma warning restore AsyncFixer03 // Avoid fire & forget async void methods
}
