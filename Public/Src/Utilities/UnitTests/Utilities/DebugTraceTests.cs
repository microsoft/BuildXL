// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Debugging;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
#if NETCOREAPP
    public sealed class DebugTraceTests : XunitBuildXLTest
    {
        public DebugTraceTests(ITestOutputHelper output)
            : base(output) { }

        private static void FillTrace(DebugTrace dt, int someNumber)
        {
            dt.AppendLine("Hello");
            dt.AppendLine("world!");
            dt.AppendLine($"It is {someNumber} o'clock.");
        }

        private string ExpectedTrace(bool enabled, int someNumber) => enabled ? $"Hello{Environment.NewLine}world!{Environment.NewLine}It is {someNumber} o'clock.{Environment.NewLine}" : "(disabled DebugTrace)";

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Basic(bool enabled)
        {
            using var debugTrace = new DebugTrace(enabled);
            int someNumber = DateTime.Now.Hour;
            FillTrace(debugTrace, someNumber);
            XAssert.AreEqual(ExpectedTrace(enabled, someNumber), debugTrace.ToString());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Array(bool enabled)
        {
            var count = 20;
            using var debugTraceArray = new DebugTraceArray(enabled, 20);
            for (var i = 0; i < count; i++)
            {
                FillTrace(debugTraceArray[i], i);
            }

            for (var i = 0; i < count; i++)
            {
                XAssert.AreEqual(ExpectedTrace(enabled, i), debugTraceArray[i].ToString());
            }
        }

        [Fact]
        public void ThreadSafe()
        {
            var debugTrace = new DebugTrace(true);
            _ = Parallel.ForEach(Enumerable.Range(0, 1000), i =>
            {
                debugTrace.AppendLine($"Appending {i}");
                var s = debugTrace.ToString();
            });

            var s = debugTrace.ToString();
            var splits = s.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            XAssert.AreEqual(1000, splits.Length);
        }

        private string ActuallyThrows() => throw new Exception("Exception thrown!");

        [Fact]
        public void InterpolationIsNotDoneWhenTraceDisabled()
        {
            var debugTrace = new DebugTrace(enabled: false);
            debugTrace.AppendLine($"This would throw: {ActuallyThrows()}");
        }
#endif
    }
}
