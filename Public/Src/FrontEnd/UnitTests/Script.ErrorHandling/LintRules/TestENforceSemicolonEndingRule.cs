// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Reflection;
using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Utilities.FormattableStringEx;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestEnforceSemicolonEndingRule : DsTest
    {
        public TestEnforceSemicolonEndingRule(ITestOutputHelper output)
            : base(output)
        {}

        [Theory]
        [MemberData(nameof(WrapFunctionThatReturnsSingleInstance), nameof(MissingSemicolonTestCases))]
        public void TestMissedSemicolon(string code)
        {
            ParseWithDiagnosticId(code, LogEventId.TypeScriptSyntaxError);
        }

        [Fact]
        public void WarnOnMissingSemicolonAfterReturnStatement()
        {
            // This is one of the examples why semicolons are required in DScript:
            // In this case there is two statements: return undefined, and standalone object literal
            // Latest TypeScript compiler (with reachability check) will emit error anyway, but
            // DScript will emit additional error about missing semicolons.
            string code =
@"function foo() {
    let x = 42    
}";
            ParseWithDiagnosticId(code, LogEventId.TypeScriptSyntaxError);
        }

        public static IEnumerable<object[]> WrapFunctionThatReturnsSingleInstance(string methodName)
        {
            var function = typeof(TestEnforceSemicolonEndingRule).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
            Contract.Assert(function != null, I($"Can't find method with name '{methodName}'"));

            var resultObject = function.Invoke(null, new object[0]);
            IEnumerable<string> result = (IEnumerable<string>) resultObject;
            foreach (var s in result)
            {
                yield return new object[] {s};
            }
        }

        private static IEnumerable<string> MissingSemicolonTestCases()
        {
            // Variable initialization
            yield return "function foo() { let x = 42 }";
            
            // Array initialization
            yield return "function foo() { let x: number[] = [1, 2, 3] }";
            
            // Return
            yield return "function foo() { return 42 }";

            // Compound assignment
            yield return "function foo() { let x = 42; x += 1 }";
            
            // Assignment
            yield return "function foo() { let x = 42; x = 1 }";

            // Function invocation
            yield return 
@"function foo() { return 42;}
function bar() { foo() }";

            // Local export
            yield return "export {Test}";

            // Type
            yield return "export type A = number";
        }
    }
}
