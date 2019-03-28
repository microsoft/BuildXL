// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class InterpretExpressions : DsTest
    {
        public InterpretExpressions(ITestOutputHelper output) : base(output) { }
        
        [Fact]
        public void UseDoubleNegationForCheckingArgumentsForNull()
        {
            string code = @"
function isTruthy(x: string) {
  return !!x;
}

const r1 = isTruthy(undefined); // false
const r2 = isTruthy(""""); // true in DScript, but false in TypeScript!
const r3 = isTruthy(""1""); // true
";
            var result = EvaluateExpressionsWithNoErrors(code, "r1", "r2", "r3");

            Assert.Equal(false, result["r1"]);
            Assert.Equal(true, result["r2"]);
            Assert.Equal(true, result["r3"]);
        }
    }
}
