// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Values;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class InterpretStatements : DsTest
    {
        public InterpretStatements(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void ReturnFromVoidFunctionShouldBeEager()
        {
            string code = @"
function runTest() {
    if (1 < 3) {
        Debug.writeLine('TRUE');
        return;
    }

    // Assertion should never fail, because the code is not reachable.
    Contract.assert(false, 'unreachable');
}

export const r = runTest();
";

            var result = EvaluateExpressionWithNoErrors(code, "r");
            Assert.Equal(UndefinedValue.Instance, result);
        }

        [Fact]
        public void InterpretEmptyStatement()
        {
            string code = @"
namespace M {
    function f() {
        ; //This creates an empty statement
        return 42;
    }
    export const r1 = f();
}
";
            var result = EvaluateExpressionsWithNoErrors(code, "M.r1");

            Assert.Equal(42, result["M.r1"]);
        }
    }
}
