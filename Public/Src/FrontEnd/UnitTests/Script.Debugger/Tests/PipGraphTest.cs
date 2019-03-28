// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Test.BuildXL.FrontEnd.Core;
using VSCode.DebugProtocol;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Debugger
{
    public class PipGraphTest : DsDebuggerTest
    {
        public PipGraphTest(ITestOutputHelper output)
            : base(output) { }

        [Fact(Skip = "context.PipGraph is null during testing (?!)")]
        public void TestEmptyGraph()
        {
            var result = DebugSpec(
                @"
namespace M
{
    function f() {
        return 42; // << breakpoint >>
    }

    export const ans = f();
}",
                new[] { "M.ans" },
                async (source) =>
                {
                    var ev = await Debugger.ReceiveEvent<IStoppedEvent>();
                    var pipVars = GetPipVars(ev.Body.ThreadId, 0);
                    AssertAreEqual(0, pipVars.Count);
                    await ClearBreakpointsContinueAndAwaitTerminate(source);
                });

            result.ExpectNoError();
            Assert.Equal(42, result.Values[0]);
        }

        [Fact(Skip = "context.PipGraph is null during testing (?!)")]
        public void TestOneCopyPip()
        {
            var result = DebugSpec(
                @"
namespace M
{
    function f() {
        const thisSpec = Context.getSpecFile();
        const outDir = Context.getNewOutputDirectory(""tmp"");
        Transformer.copyFile(thisSpec, p`${outDir}/${thisSpec.name}`);
        
        return 42; // << breakpoint >>
    }

    export const ans = f();
}",
                new[] { "M.ans" },
                async (source) =>
                {
                    var ev = await Debugger.ReceiveEvent<IStoppedEvent>();
                    var pipVars = GetPipVars(ev.Body.ThreadId, 0);
                    AssertAreEqual(1, pipVars.Count);
                    await ClearBreakpointsContinueAndAwaitTerminate(source);
                });

            result.ExpectNoError();
            Assert.Equal(42, result.Values[0]);
        }
    }
}
