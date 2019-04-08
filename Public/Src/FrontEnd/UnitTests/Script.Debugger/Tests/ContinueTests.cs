// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Debugger
{
    // Tests for Continue command, a.k.a. F5 experience.
    public class ContinueTest : DsDebuggerTest
    {
        public ContinueTest(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void Continue_StopAtEachBreakpointOnce()
        {
            var result = DebugSpec(
                @"
namespace M
{
    function f() {
        let z = 0;// << breakpoint:5 >>
        z = 42 + z;// << breakpoint:6 >> 
        return z;// << breakpoint:7 >>
    }

    export const ans = f();
}",
                new[] { "M.ans" },
                async (source) =>
                {
                    var ev = await StopAndValidateLineNumber(5);

                    Debugger.Continue(ev.Body.ThreadId);
                    await StopAndValidateLineNumber(6);

                    Debugger.Continue(ev.Body.ThreadId);
                    await StopAndValidateLineNumber(7);

                    await ClearBreakpointsContinueAndAwaitTerminate(source);
                });

            result.ExpectNoError();
        }

        [Fact]
        public void Continue_StopAtSameBreakpointInRecursion()
        {
            var result = DebugSpec(
                @"
namespace M
{
    function f(a: number) : number {
        if (a > 0) {// << breakpoint:5 >>
            return f(a - 1);
        } else {
            return 0;// << breakpoint:8 >>
        }
    }

    export const ans = f(2);
}",
                new[] { "M.ans" },
                async (source) =>
                {
                    var ev = await StopAndValidateLineNumber(5);

                    // a == 2
                    Debugger.Continue(ev.Body.ThreadId);
                    await StopAndValidateLineNumber(5);

                    // a == 1
                    Debugger.Continue(ev.Body.ThreadId);
                    await StopAndValidateLineNumber(5);

                    // a == 0
                    Debugger.Continue(ev.Body.ThreadId);
                    await StopAndValidateLineNumber(8);

                    await ClearBreakpointsContinueAndAwaitTerminate(source);
                });

            result.ExpectNoError();
        }

        [Fact]
        public void Continue_StopAtSameBreakpointInLoop()
        {
            var result = DebugSpec(
                @"
namespace M
{
    function f(z: number) : number {
        let b = 0;
        for(
            let x = 0;
            x < z;// << breakpoint:8 >>
            x++){
            b = z;
        }

        return b;
    }

    export const ans = f(2);
}",
                new[] { "M.ans" },
                async (source) =>
                {
                    // x == 0
                    var ev = await StopAndValidateLineNumber(8);

                    // x == 1
                    Debugger.Continue(ev.Body.ThreadId);
                    await StopAndValidateLineNumber(8);

                    // x == 2
                    Debugger.Continue(ev.Body.ThreadId);
                    await StopAndValidateLineNumber(8);

                    await ClearBreakpointsContinueAndAwaitTerminate(source);
                });

            result.ExpectNoError();
        }
    }
}
