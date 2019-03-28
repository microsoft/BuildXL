// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Script.Debugger;
using Test.BuildXL.FrontEnd.Core;
using VSCode.DebugProtocol;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Debugger
{
    public class ModuleScopeTest : DsDebuggerTest
    {
        public ModuleScopeTest(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void TestFileModule()
        {
            var result = DebugSpec(
                @"
namespace M
{
    export namespace N 
    {
        export const x = 42; 
    }
}

namespace N
{
    export const x = 42; 
}

function block() {
    return 0; // << breakpoint >>
}

const x = 0;
const ans = x + M.N.x + N.x + block();
",
                new[] { "ans" },
                async source =>
                {
                    var ev = await Debugger.ReceiveEvent<IStoppedEvent>();
                    var locals = GetLocalVars(ev.Body.ThreadId, 0);
                    AssertAreEqual(0, locals.Count);

                    var values =
                        ToDictionary(GetScopeVars(ev.Body.ThreadId, 0, Renderer.CurrentModuleScopeName));

                    AssertExists(values, ":path");
                    AssertExists(values, ":qualifierSpace", "object{0}");
                    AssertExists(values, ":package");
                    AssertExists(values, ":parent");

                    AssertExists(values, "x", 0);
                    AssertExists(values, "block");
                    AssertExists(values, "ans");
                    AssertExists(values, "qualifier", "object{0}");
                    AssertExists(values, "M.N");
                    AssertExists(values, "N");
                    AssertExists(values, "M");

                    await ClearBreakpointsContinueAndAwaitTerminate(source);
                });

            result.ExpectNoError();
            Assert.Equal(42 * 2, result.Values[0]);
        }

        [Fact]
        public void TestLocalsAndTraversingModuleUpWard()
        {
            var result = DebugSpec(
                @"
namespace M
{
    const g = 42;
    export const ans = f(0);
    
    function f(n) {
        const y = g + 1;
        const z = 11;
        return n + y + z; // << breakpoint >>
    }
}",
                new[] { "M.ans" },
                async source =>
                {
                    var ev = await Debugger.ReceiveEvent<IStoppedEvent>();
                    var locals = ToDictionary(GetLocalVars(ev.Body.ThreadId, 0));
                    AssertAreEqual(3, locals.Count);
                    AssertExists(locals, "n", 0);
                    AssertExists(locals, "y", 43);
                    AssertExists(locals, "z", 11);

                    var values = ToDictionary(GetScopeVars(ev.Body.ThreadId, 0, Renderer.CurrentModuleScopeName));
                    AssertExists(values, "M.g", 42);
                    AssertExists(values, ":parent");
                    AssertExists(values, ":parent", "module");

                    var parent = values[":parent"];

                    var fileModuleValues = ToDictionary(GetVar(parent));
                    AssertExists(fileModuleValues, "glob", "function");

                    await ClearBreakpointsContinueAndAwaitTerminate(source);
                });

            result.ExpectNoError();
            Assert.Equal(54, result.Values[0]);
        }
    }
}
