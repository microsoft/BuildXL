// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Evaluator;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Debugger
{
    public class StepInTest : SteppingTest
    {
        protected override DebugAction.ActionKind Kind => DebugAction.ActionKind.StepIn;

        public StepInTest(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void StepIn_GoIntoFunctionCall()
        {
            var result = DebugSpec(
                @"
    function f() {
        let z = 0; // << breakpoint:3 >>
        z = get1();
        return z;
    }

    function get1(): number { 
        return 1; // Line 9
    }

    export const ans = f();",
                new[] { "ans" },
                async (source) =>
                {
                    var ev = await StopAndValidateLineNumber(3);

                    Debugger.StepIn(ev.Body.ThreadId);
                    await StopAndValidateLineNumber(4);

                    Debugger.StepIn(ev.Body.ThreadId); // Step into get1()
                await StopAndValidateLineNumber(9);

                    await ClearBreakpointsContinueAndAwaitTerminate(source);
                });

            result.ExpectNoError();
        }

        [Fact]
        public void StepIn_OneLineATime()
        {
            OneLineATime();
        }

        [Fact]
        public void StepIn_PauseAfterExitingFunctionCall()
        {
            PauseAfterExitingFunctionCall();
        }

        [Fact]
        public void StepIn_TwoStmtsInSameLine()
        {
            TwoStmtsInSameLine();
        }

        [Fact]
        public void StepIn_ForLoopOneLinerBody()
        {
            ForLoopOneLinerBody();
        }

        [Fact]
        public void StepIn_ForLoopEnclosedBody()
        {
            ForLoopEnclosedBody();
        }

        [Fact]
        public void StepIn_DoubleForLoopEnclosedBody()
        {
            DoubleForLoopEnclosedBody();
        }

        [Fact]
        public void StepIn_ForOfOneLinerBody()
        {
            ForOfOneLinerBody();
        }

        [Fact]
        public void StepIn_ForOfLoopEnclosedBody()
        {
            ForOfLoopEnclosedBody();
        }

        [Fact]
        public void StepIn_Switch()
        {
            Switch();
        }

        [Fact]
        public void StepIn_ResetBreakpoint()
        {
            ResetBreakpoint();
        }
    }
}
