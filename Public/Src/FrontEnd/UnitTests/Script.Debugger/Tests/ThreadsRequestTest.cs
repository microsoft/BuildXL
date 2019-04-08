// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using Test.BuildXL.FrontEnd.Core;
using VSCode.DebugProtocol;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Debugger
{
    public class ThreadsRequestTest : DsDebuggerTest
    {
        public ThreadsRequestTest(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void Test1Thread()
        {
            var result = DebugSpec(
                @"
function f() {
    return 42; // << breakpoint >>
}

export const ans = f();",
                new[] { "ans" },
                async (source) =>
                {
                    await Debugger.ReceiveEvent<IStoppedEvent>();
                    var threads = GetThreads();
                    AssertAreEqual(1, threads.Count);
                    AssertTrue(threads.First().Name.StartsWith("ans"));
                    await ClearBreakpointsContinueAndAwaitTerminate(source);
                });

            result.ExpectNoError();
            Assert.Equal(42, result.Values[0]);
        }

        [Fact]
        public void Test2Threads()
        {
            var result = DebugSpec(
                @"
function f() {
    return 42; // << breakpoint >>
}

export const ans1 = f();
export const ans2 = f() + 1;",
                new[] { "ans1", "ans2" },
                async (source) =>
                {
                    await Debugger.ReceiveEvent<IStoppedEvent>();
                    await Debugger.ReceiveEvent<IStoppedEvent>();

                    var threads = GetThreadsSortedByName();
                    AssertAreEqual(2, threads.Count);
                    AssertTrue(threads[0].Name.StartsWith("ans1"));
                    AssertTrue(threads[1].Name.StartsWith("ans2"));
                    await ClearBreakpointsContinueAndAwaitTerminate(source);
                });

            result.ExpectNoError();
            result.ExpectValues(count: 2);
            Assert.Equal(42, result.Values[0]);
            Assert.Equal(43, result.Values[1]);
        }
    }
}
