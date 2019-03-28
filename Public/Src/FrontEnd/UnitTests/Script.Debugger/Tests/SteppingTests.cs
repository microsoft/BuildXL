// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Evaluator;
using Test.BuildXL.FrontEnd.Core;
using VSCode.DebugProtocol;
using Xunit.Abstractions;

namespace Test.DScript.Debugger
{
    public abstract class SteppingTest : DsDebuggerTest
    {
        public SteppingTest(ITestOutputHelper output)
            : base(output) { }

        protected abstract DebugAction.ActionKind Kind { get; }

        private void IssueRequest(IStoppedEvent ev)
        {
            if (Kind == DebugAction.ActionKind.StepOver)
            {
                Debugger.Next(ev.Body.ThreadId);
            }
            else if (Kind == DebugAction.ActionKind.StepIn)
            {
                Debugger.StepIn(ev.Body.ThreadId);
            }
        }

        protected void OneLineATime()
        {
            var result = DebugSpec(
                @"
function f() {
    let a = 42;// << breakpoint:3 >>
    return a;
}

export const ans = f();",
                new[] { "ans" },
                async (source) =>
                {
                    var ev = await StopAndValidateLineNumber(3);

                    IssueRequest(ev);
                    await StopAndValidateLineNumber(4);

                    await ClearBreakpointsContinueAndAwaitTerminate(source);
                });

            result.ExpectNoError();
        }

        protected void PauseAfterExitingFunctionCall()
        {
            var result = DebugSpec(
                @"
function getVal(): number {
    return 5; // << breakpoint:3 >>
}

function f() {
    let z = 0;
    z = getVal(); // Line 8. We should pause here when F10/F11 from BP:5
    return z;
}

export const ans = f();",
                new[] { "ans" },
                async (source) =>
                {
                    var ev = await StopAndValidateLineNumber(3);

                    IssueRequest(ev);
                    await StopAndValidateLineNumber(8);

                    await ClearBreakpointsContinueAndAwaitTerminate(source);
                });

            result.ExpectNoError();
        }

        protected void TwoStmtsInSameLine()
        {
            var result = DebugSpec(
                @"
function f() {
    let a = 42; a = 46;// << breakpoint:3 >>
    return a;
}

export const ans = f();",
                new[] { "ans" },
                async (source) =>
                {
                // let a = 42;
                int startline = 3;
                    var ev = await StopAndValidateLineNumber(startline);

                // a = 46; (same line as previous stmt)
                IssueRequest(ev);
                    await StopAndValidateLineNumber(startline);

                // return a;
                IssueRequest(ev);
                    await StopAndValidateLineNumber(startline + 1);

                    await ClearBreakpointsContinueAndAwaitTerminate(source);
                });

            result.ExpectNoError();
        }

        protected void ForLoopOneLinerBody()
        {
            var result = DebugSpec(
                @"
function f() {
    let z = 0;
    for (let x = 0; x < 2; x++) // << breakpoint:4 >>
        z = x;
    return z;
}

export const ans = f();",
                new[] { "ans" },
                async (source) =>
                {
                // for (let x = 0; x < 2; x++)
                int startline = 4;
                    var ev = await StopAndValidateLineNumber(startline);

                // Iteration 1
                // body
                IssueRequest(ev);
                    await StopAndValidateLineNumber(startline + 1);

                // incrementor
                IssueRequest(ev);
                    await StopAndValidateLineNumber(startline);

                // Iteration 2
                // body
                IssueRequest(ev);
                    await StopAndValidateLineNumber(startline + 1);

                // incrementor
                IssueRequest(ev);
                    await StopAndValidateLineNumber(startline);

                // return z;
                IssueRequest(ev);
                    await StopAndValidateLineNumber(startline + 2);

                    await ClearBreakpointsContinueAndAwaitTerminate(source);
                });

            result.ExpectNoError();
        }

        protected void ForLoopEnclosedBody()
        {
            var result = DebugSpec(
                @"
function f() {
    let z = 0;
    for (let x = 0; x < 2; x++) { // << breakpoint:4 >>
        z = x;
    }
    return z;
}

export const ans = f();",
                new[] { "ans" },
                async (source) =>
                {
                // for (let x = 0; x < 2; x++)
                int startline = 4;
                    var ev = await StopAndValidateLineNumber(startline);

                // Iteration 1
                // body
                IssueRequest(ev);
                    await StopAndValidateLineNumber(startline + 1);

                // incrementor
                IssueRequest(ev);
                    await StopAndValidateLineNumber(startline);

                // Iteration 2
                // body
                IssueRequest(ev);
                    await StopAndValidateLineNumber(startline + 1);

                // incrementor
                IssueRequest(ev);
                    await StopAndValidateLineNumber(startline);

                // return z;
                IssueRequest(ev);
                    await StopAndValidateLineNumber(startline + 3);

                    await ClearBreakpointsContinueAndAwaitTerminate(source);
                });

            result.ExpectNoError();
        }

        protected void DoubleForLoopEnclosedBody()
        {
            var result = DebugSpec(
                @"
function f() {
    let z = 0;// << breakpoint:3 >>
    for(let x = 0; x < 1; x++){       // Line 4
        for(let y = 0; y < 1; y++){   // Line 5
            z = y;                    // Line 6
        }
    }

    return z;
}

export const ans = f();",
                new[] { "ans" },
                async (source) =>
                {
                    int startline = 3;
                    var ev = await StopAndValidateLineNumber(startline);

                // for(let x = 0; x < 1; x++){
                IssueRequest(ev);
                    await StopAndValidateLineNumber(startline + 1);

                // for(let y = 0; y < 1; y++){
                IssueRequest(ev);
                    await StopAndValidateLineNumber(startline + 2);

                // z = y;
                IssueRequest(ev);
                    await StopAndValidateLineNumber(startline + 3);

                // for(let y = 0; y < 1; y++){
                IssueRequest(ev);
                    await StopAndValidateLineNumber(startline + 2);

                // for(let x = 0; x < 1; x++){
                IssueRequest(ev);
                    await StopAndValidateLineNumber(startline + 1);

                    await ClearBreakpointsContinueAndAwaitTerminate(source);
                });

            result.ExpectNoError();
        }

        protected void ForOfOneLinerBody()
        {
            var result = DebugSpec(
                @"
function f() {
    let z = 0;
    let arr = [10, 20];
    for (let a of arr) // << breakpoint:5 >>
        z = a;
    return z;
}

export const ans = f();",
                new[] { "ans" },
                async (source) =>
                {
                // for (let a of arr)
                int startline = 5;
                    var ev = await StopAndValidateLineNumber(startline);

                // Iteration 1
                // body
                IssueRequest(ev);
                    await StopAndValidateLineNumber(startline + 1);

                // generator
                IssueRequest(ev);
                    await StopAndValidateLineNumber(startline);

                // Iteration 2
                // body
                IssueRequest(ev);
                    await StopAndValidateLineNumber(startline + 1);

                // no more generator since we already hit the end

                // return z;
                IssueRequest(ev);
                    await StopAndValidateLineNumber(startline + 2);

                    await ClearBreakpointsContinueAndAwaitTerminate(source);
                });

            result.ExpectNoError();
        }

        protected void ForOfLoopEnclosedBody()
        {
            var result = DebugSpec(
                @"
function f() {
    let z = 0;
    let arr = [10, 20];
    for (let a of arr) { // << breakpoint:5 >>
        z = a;
    }
    return z;
}

export const ans = f();",
                new[] { "ans" },
                async (source) =>
                {
                // for(let a of arr)
                int startline = 5;
                    var ev = await StopAndValidateLineNumber(startline);

                // Iteration 1
                // body
                IssueRequest(ev);
                    await StopAndValidateLineNumber(startline + 1);

                // generator
                IssueRequest(ev);
                    await StopAndValidateLineNumber(startline);

                // Iteration 2
                // body
                IssueRequest(ev);
                    await StopAndValidateLineNumber(startline + 1);

                // no more generator since we already hit the end

                // return z;
                IssueRequest(ev);
                    await StopAndValidateLineNumber(startline + 3);

                    await ClearBreakpointsContinueAndAwaitTerminate(source);
                });

            result.ExpectNoError();
        }

        /// <remarks>
        /// Since case condition is evaluated in runtime, the debugger will pause at each case condition as long as
        /// it's being evaluated. This is different from debuggers for compiled language where the case is constant
        /// and skipped when stepping over.
        ///
        /// switch(...){   // Hit F10 here to step over...
        ///   case 1:      // In DSC debugger, pause here for the evaluation of case expression (in this case a number literal 1)
        ///                // In comparison, C# debugger will not pause here but directly go to the case statements, if matched.
        ///      ... ...
        ///      break;
        ///   ...
        /// }
        /// </remarks>
        protected void Switch()
        {
            var result = DebugSpec(
                @"
function f() {
    let z = 2;
    let a = 0;
    let b = 0;
    switch(z) // << breakpoint:6 >>
    {
        case b + 1: // pause
            a = 1;
            break;
        case b + 2: // pause
            a = 2;  // pause
        case b + 3:
            a = 3;  // pause
            break;  // pause
        default:
            break;
    }

    return a;
}

export const ans = f();",
                new[] { "ans" },
                async (source) =>
                {
                // switch(z)
                int startline = 6;
                    var ev = await StopAndValidateLineNumber(startline);

                    IssueRequest(ev);

                // Case 1
                // expression
                await StopAndValidateLineNumber(startline += 2);

                // (No match)
                IssueRequest(ev);

                // Case 2
                // expression
                await StopAndValidateLineNumber(startline += 3);

                // (Matched)
                // body
                IssueRequest(ev);
                    await StopAndValidateLineNumber(startline += 1);

                    IssueRequest(ev);

                // Case 3
                // expression
                // (Skipped)
                // body
                await StopAndValidateLineNumber(startline += 2);
                    IssueRequest(ev);

                // break;
                await StopAndValidateLineNumber(startline += 1);

                // return a;
                IssueRequest(ev);
                    await StopAndValidateLineNumber(startline += 5);

                    await ClearBreakpointsContinueAndAwaitTerminate(source);
                });

            result.ExpectNoError();
        }

        public void ResetBreakpoint()
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

                // Continue stepping, we will stop at a line where we have just added a new breakpoint.
                // But this fact should not cause unexpceted bahavior such as pausing twice or skipping.
                IssueRequest(ev);
                    await StopAndValidateLineNumber(4);
                    IssueRequest(ev);
                    await StopAndValidateLineNumber(8);
                    IssueRequest(ev);
                    await StopAndValidateLineNumber(9);

                    await ClearBreakpointsContinueAndAwaitTerminate(source);
                });

            result.ExpectNoError();
        }
    }
}
