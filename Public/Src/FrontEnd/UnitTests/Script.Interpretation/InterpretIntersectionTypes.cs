// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class InterpretIntersectionTypes : DsTest
    {
        public InterpretIntersectionTypes(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void InterpretIntersectionTypeWithTwoMembers()
        {
            string spec =
@"interface First {
  n: number;
}

interface Second {
  s: string;
}

function foo(): First & Second { return { n: 42, s: '42' }; }
const f = foo();
export const r = f.n;
export const r2 = f.s;
";
            var result = EvaluateExpressionsWithNoErrors(spec, "r", "r2");
            Assert.Equal(42, result["r"]);
            Assert.Equal("42", result["r2"]);
        }
    }
}
