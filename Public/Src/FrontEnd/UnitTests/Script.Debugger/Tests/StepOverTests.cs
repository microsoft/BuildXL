// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Evaluator;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Debugger
{
    public class StepOverTest : SteppingTest
    {
        protected override DebugAction.ActionKind Kind => DebugAction.ActionKind.StepOver;

        public StepOverTest(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void StepOver_DoNotGoIntoFunctionCall()
        {
            var result = DebugSpec(
                @"
function f() {
    let z = 0; // << breakpoint >>
    z = get1();
    return z;
}

function get1() : number { return 1; }

export const ans = f();",
                new[] { "ans" },
                async (source) =>
                {
                    var ev = await StopAndValidateLineNumber(3);

                    Debugger.Next(ev.Body.ThreadId);
                    await StopAndValidateLineNumber(4);

                    Debugger.Next(ev.Body.ThreadId); // Skip over get1()
                await StopAndValidateLineNumber(5);

                    await ClearBreakpointsContinueAndAwaitTerminate(source);
                });

            result.ExpectNoError();
        }

        [Fact]
        public void StepOver_OneLineATime()
        {
            OneLineATime();
        }

        [Fact]
        public void StepOver_PauseAfterExitingFunctionCall()
        {
            PauseAfterExitingFunctionCall();
        }

        [Fact]
        public void StepOver_TwoStmtsInSameLine()
        {
            TwoStmtsInSameLine();
        }

        [Fact]
        public void StepOver_ForLoopOneLinerBody()
        {
            ForLoopOneLinerBody();
        }

        [Fact]
        public void StepOver_ForLoopEnclosedBody()
        {
            ForLoopEnclosedBody();
        }

        [Fact]
        public void StepOver_DoubleForLoopEnclosedBody()
        {
            DoubleForLoopEnclosedBody();
        }

        [Fact]
        public void StepOver_ForOfOneLinerBody()
        {
            ForOfOneLinerBody();
        }

        [Fact]
        public void StepOver_ForOfLoopEnclosedBody()
        {
            ForOfLoopEnclosedBody();
        }

        [Fact]
        public void StepOver_Switch()
        {
            Switch();
        }

        [Fact]
        public void StepOver_ResetBreakpoint()
        {
            ResetBreakpoint();
        }
    }
}
