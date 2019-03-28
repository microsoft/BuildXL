// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using Test.BuildXL.FrontEnd.Core;
using VSCode.DebugProtocol;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Debugger
{
    public class LocalVarNamesTests : DsDebuggerTest
    {
        public LocalVarNamesTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void TestVarsAppearAsTheyAreEvaluated()
        {
            var result = DebugSpec(
                @"
    function fn(a: number) {
        let b = 0; // << breakpoint:3 >>
        return b;  // << breakpoint:4 >> 
    }

    export const ans = fn(1);",
            new[] { "ans" },
            async (source) =>
            {
                var ev = await StopAndValidateLineNumber(3);
                AssertLocalsAndAssertEvaluateLocals(
                    names: new[] { "a" },
                    values: new[] { "1" },
                    threadId: ev.Body.ThreadId,
                    frameIdx: 0);

                Debugger.Continue(ev.Body.ThreadId);
                ev = await StopAndValidateLineNumber(4);

                AssertLocalsAndAssertEvaluateLocals(
                    names: new[] { "a", "b" },
                    values: new[] { "1", "0" },
                    threadId: ev.Body.ThreadId,
                    frameIdx: 0);

                await ContinueThreadAndAwaitTerminate(ev.Body.ThreadId);
            });

            result.ExpectNoError();
        }

        [Fact]
        public void TestVarsInLambdaAppearAsTheyAreEvaluated()
        {
            var result = DebugSpec(
                @"
    function fn(a: number) {
        let b = 1;
        return [2].map(e => {
            let x = 3;        // << breakpoint:5 >>
            return e + x;     // << breakpoint:6 >>
        });
    }

    export const ans = fn(0);",
            new[] { "ans" },
            async (source) =>
            {
                // run to BP:5
                var ev = await StopAndValidateLineNumber(5);

                // test locals in top stack frame
                AssertLocalsAndAssertEvaluateLocals(
                    names: new[] { "a", "b", "e" },
                    values: new[] { "0", "1", "2" },
                    threadId: ev.Body.ThreadId,
                    frameIdx: 0);

                // test locals in parent (not ambient) frame
                AssertLocalsAndAssertEvaluateLocals(
                    names: new[] { "a", "b" },
                    values: new[] { "0", "1" },
                    threadId: ev.Body.ThreadId,
                    frameIdx: 2);

                // continue to BP:6
                Debugger.Continue(ev.Body.ThreadId);
                ev = await StopAndValidateLineNumber(6);

                // test locals in top stack frame
                AssertLocalsAndAssertEvaluateLocals(
                    names: new[] { "a", "b", "e", "x" },
                    values: new[] { "0", "1", "2", "3" },
                    threadId: ev.Body.ThreadId,
                    frameIdx: 0);

                // test locals in parent non-ambient frame
                AssertLocalsAndAssertEvaluateLocals(
                    names: new[] { "a", "b" },
                    values: new[] { "0", "1" },
                    threadId: ev.Body.ThreadId,
                    frameIdx: 2);

                await ContinueThreadAndAwaitTerminate(ev.Body.ThreadId);
            });

            result.ExpectNoError();
        }

        [Fact]
        public void TestVarsInNestedLambdas()
        {
            var result = DebugSpec(
                @"
    function fn(a: number) {
        let b = 1;
        return [2].map(c => {
            let d = 3;
            return [4].map(e => {
                let f = 5;
                return f;          // << breakpoint:8 >>
            });
        });
    }

    export const ans = fn(0);",
            new[] { "ans" },
            async (source) =>
            {
                // run to BP:8
                var ev = await StopAndValidateLineNumber(8);

                // test locals in top stack frame
                AssertLocalsAndAssertEvaluateLocals(
                    names: new[] { "a", "b", "c", "d", "e", "f" },
                    values: new[] { "0", "1", "2", "3", "4", "5" },
                    threadId: ev.Body.ThreadId,
                    frameIdx: 0);

                // test locals in parent non-ambient frame
                AssertLocalsAndAssertEvaluateLocals(
                    names: new[] { "a", "b", "c", "d" },
                    values: new[] { "0", "1", "2", "3" },
                    threadId: ev.Body.ThreadId,
                    frameIdx: 2);

                // test locals in parent parent non-ambient frame
                AssertLocalsAndAssertEvaluateLocals(
                    names: new[] { "a", "b" },
                    values: new[] { "0", "1" },
                    threadId: ev.Body.ThreadId,
                    frameIdx: 4);

                await ContinueThreadAndAwaitTerminate(ev.Body.ThreadId);
            });

            result.ExpectNoError();
        }

        [Fact]
        public void TestVarsInNestedLambdasWithAliasing()
        {
            var result = DebugSpec(
                @"
    function fn(a: number) {
        let b = 1;
        return [2].map(c => {
            let d = 3;
            return [4].map(c => {
                let d = 5;        // << breakpoint:7 >>
                return d;         // << breakpoint:8 >>
            });
        });
    }

    export const ans = fn(0);",
            new[] { "ans" },
            async (source) =>
            {
                // run to BP:7
                var ev = await StopAndValidateLineNumber(7);

                // test locals in top stack frame
                AssertLocalsAndAssertEvaluateLocals(
                    names: new[] { "a", "b", "c", "d" },
                    values: new[] { "0", "1", "4", "3" },
                    threadId: ev.Body.ThreadId,
                    frameIdx: 0);

                // test locals in parent non-ambient frame
                AssertLocalsAndAssertEvaluateLocals(
                    names: new[] { "a", "b", "c", "d" },
                    values: new[] { "0", "1", "2", "3" },
                    threadId: ev.Body.ThreadId,
                    frameIdx: 2);

                // test locals in parent parent non-ambient frame
                AssertLocalsAndAssertEvaluateLocals(
                    names: new[] { "a", "b" },
                    values: new[] { "0", "1" },
                    threadId: ev.Body.ThreadId,
                    frameIdx: 4);

                // continue to BP:8
                Debugger.Continue(ev.Body.ThreadId);
                ev = await StopAndValidateLineNumber(8);

                // test locals in top stack frame
                AssertLocalsAndAssertEvaluateLocals(
                    names: new[] { "a", "b", "c", "d" },
                    values: new[] { "0", "1", "4", "5" },
                    threadId: ev.Body.ThreadId,
                    frameIdx: 0);

                // test locals in parent non-ambient frame
                AssertLocalsAndAssertEvaluateLocals(
                    names: new[] { "a", "b", "c", "d" },
                    values: new[] { "0", "1", "2", "3" },
                    threadId: ev.Body.ThreadId,
                    frameIdx: 2);

                // test locals in parent parent non-ambient frame
                AssertLocalsAndAssertEvaluateLocals(
                    names: new[] { "a", "b" },
                    values: new[] { "0", "1" },
                    threadId: ev.Body.ThreadId,
                    frameIdx: 4);

                await ContinueThreadAndAwaitTerminate(ev.Body.ThreadId);
            });

            result.ExpectNoError();
        }

        [Fact]
        public void TestVarsInLambdaInNestedFunctionCalls()
        {
            var result = DebugSpec(
                @"
    function foo(q: number) {
        let w = 42;
        return boo(0);        // << breakpoint:4 >>
    }

    function boo(a: number) {
        let b = 1;            // << breakpoint:8 >>
        return [2].map(e => {
            let x = 3;        
            return e + x;     // << breakpoint:11 >>
        });
    }

    export const ans = foo(32);",
            new[] { "ans" },
            async (source) =>
            {
                // run to BP:4
                var ev = await StopAndValidateLineNumber(4);
                AssertLocalsAndAssertEvaluateLocals(
                    names: new[] { "q", "w" },
                    values: new[] { "32", "42" },
                    threadId: ev.Body.ThreadId,
                    frameIdx: 0);

                // continue to BP:8
                Debugger.Continue(ev.Body.ThreadId);
                ev = await StopAndValidateLineNumber(8);

                AssertLocalsAndAssertEvaluateLocals(
                    names: new[] { "a" },
                    values: new[] { "0" },
                    threadId: ev.Body.ThreadId,
                    frameIdx: 0);

                // continue to BP:11
                Debugger.Continue(ev.Body.ThreadId);
                ev = await StopAndValidateLineNumber(11);

                // test local vars in top stack frame
                AssertLocalsAndAssertEvaluateLocals(
                    names: new[] { "a", "b", "e", "x" },
                    values: new[] { "0", "1", "2", "3" },
                    threadId: ev.Body.ThreadId,
                    frameIdx: 0);

                // test local vars in parent non-ambient frame
                AssertLocalsAndAssertEvaluateLocals(
                    names: new[] { "a", "b" },
                    values: new[] { "0", "1" },
                    threadId: ev.Body.ThreadId,
                    frameIdx: 2);

                // test local vars in parent parent non-ambient frame
                AssertLocalsAndAssertEvaluateLocals(
                    names: new[] { "q", "w" },
                    values: new[] { "32", "42" },
                    threadId: ev.Body.ThreadId,
                    frameIdx: 3);

                await ContinueThreadAndAwaitTerminate(ev.Body.ThreadId);
            });

            result.ExpectNoError();
        }

        private string[] GetValues(IReadOnlyList<IVariable> locals)
        {
            return locals.Select(lvar => lvar.Value).ToArray();
        }

        private string[] GetNames(IReadOnlyList<IVariable> locals)
        {
            return locals.Select(lvar => lvar.Name).ToArray();
        }

        private void AssertLocalsAndAssertEvaluateLocals(string[] names, string[] values, int threadId, int frameIdx)
        {
            AssertLocals(names, values, threadId, frameIdx);
            for (int i = 0; i < names.Length; i++)
            {
                AssertEvaluate(values[i], names[i], threadId, frameIdx);
            }

            var missingVarName = "dummy__" + string.Join(string.Empty, names);
            AssertEvaluate(null, missingVarName, threadId, frameIdx);
        }

        private void AssertLocals(string[] names, string[] values, int threadId, int frameIdx)
        {
            var locals = GetLocalVars(threadId, frameIdx);
            Assert.Equal(names, GetNames(locals));
            Assert.Equal(values, GetValues(locals));
        }

        private void AssertEvaluate(string expectedResult, string expression, int threadId, int frameIdx)
        {
            var res = Debugger.Evaluate(expression, GetStackFrames(threadId)[frameIdx].Id);
            if (expectedResult == null)
            {
                Assert.Null(res);
            }
            else
            {
                Assert.NotNull(res);
                Assert.Equal(expectedResult, res.Result);
            }
        }
    }
}
