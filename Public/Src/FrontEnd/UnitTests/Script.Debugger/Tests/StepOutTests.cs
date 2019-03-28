// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Debugger
{
    public class StepOutTest : DsDebuggerTest
    {
        public StepOutTest(ITestOutputHelper output)
            : base(output) { }

        // VS CODE: F11 -> F11 -> F11
        [Fact]
        public void StepOut_ExitFunctionCalls()
        {
            var result = DebugSpec(
                @"
    function f() {
        let z = 0;
        z = get1(); // Line 4
        return z;
    }

    function get1(): number { 
        return 1; // << breakpoint:9 >>
    }

    export const ans = f(); // Line 12",
                new[] { "ans" },
                async (source) =>
                {
                    var ev = await StopAndValidateLineNumber(9);

                    Debugger.StepOut(ev.Body.ThreadId);
                    await StopAndValidateLineNumber(4);

                    Debugger.StepOut(ev.Body.ThreadId);
                    await StopAndValidateLineNumber(12);

                    await ClearBreakpointsContinueAndAwaitTerminate(source);
                });

            result.ExpectNoError();
        }

        [Fact]
        public void StepOut_SkipFunctionCallInSameStackFrame()
        {
            var result = DebugSpec(
                @"
    function f() {
        let z = 0; // << breakpoint:3 >>
        z = get1();
        return z;
    }

    function get1(): number { 
        return 1;
    }

    export const ans = f(); // Line 12",
                new[] { "ans" },
                async (source) =>
                {
                    var ev = await StopAndValidateLineNumber(3);

                    Debugger.StepOut(ev.Body.ThreadId);
                    await StopAndValidateLineNumber(12);

                    await ClearBreakpointsContinueAndAwaitTerminate(source);
                });

            result.ExpectNoError();
        }

        // VS CODE: F11 -> SHIFT+F11 -> F11 -> SHIFT+F11
        [Fact]
        public void StepOut_InCombinationWithStepIn()
        {
            var result = DebugSpec(
                @"
    function f() {
        let z = 0; // << breakpoint:3 >>
        z = getV(4) + getV(5); // Line 4
        return z;
    }

    function getV(v: number) : number {
        return v; // Line 9
    }

    export const ans = f(); // Line 12",
                new[] { "ans" },
                async (source) =>
                {
                    var ev = await StopAndValidateLineNumber(3);
                    Debugger.Next(ev.Body.ThreadId);

                    await StopAndValidateLineNumber(4);

                    // Get into getV(4) and out
                    Debugger.StepIn(ev.Body.ThreadId);

                    await StopAndValidateLineNumber(9);
                    Debugger.StepOut(ev.Body.ThreadId);

                    await StopAndValidateLineNumber(4);

                    // Get into getV(5) and out
                    Debugger.StepIn(ev.Body.ThreadId);

                    await StopAndValidateLineNumber(9);
                    Debugger.StepOut(ev.Body.ThreadId);

                    await StopAndValidateLineNumber(4);

                    Debugger.StepOut(ev.Body.ThreadId);

                    await StopAndValidateLineNumber(12);
                    await ClearBreakpointsContinueAndAwaitTerminate(source);
                });

            result.ExpectNoError();
        }

        [Fact]
        public void StepOut_ResetBreakpoint()
        {
            var result = DebugSpec(
                @"
    function g() {
        let z = 0;// << breakpoint:3 >>
        return z;
    }

    function f() {
        let x = g() + 4; // Line 8
        return x;
    }

    export const ans = f(); // Line 12",
                new[] { "ans" },
                async (source) =>
                {
                    var ev = await StopAndValidateLineNumber(3);

                    // Now add a new breakpoint at line 8
                    SetBreakpoints(source, new int[] { 3, 8 });

                    // Stepping out, we should now hit line 8, which also happens to be the line
                    // where we just set the breakpoint
                    Debugger.StepOut(ev.Body.ThreadId);
                    await StopAndValidateLineNumber(8);

                    // Stepping out again, we should end at the outer function.
                    Debugger.StepOut(ev.Body.ThreadId);
                    await StopAndValidateLineNumber(12);

                    await ClearBreakpointsContinueAndAwaitTerminate(source);
                });

            result.ExpectNoError();
        }
    }
}
