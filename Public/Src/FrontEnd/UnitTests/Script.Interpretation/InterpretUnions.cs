// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class InterpretUnions : DsTest
    {
        public InterpretUnions(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void InterpretSimpleTypeAlias()
        {
            string spec =
@"namespace M {
    type Number = number;
    export const r: Number = 42;
}";
            var result = EvaluateExpressionWithNoErrors(spec, "M.r");
            Assert.Equal(42, result);
        }

        [Fact]
        public void InterpretUnionType()
        {
            string spec =
@"namespace M {
    type Number = number | string;

    function convertToString(n: Number): string {
       return n.toString();
    }

    const a1: Number = 42;
    const a2: number | string = ""42"";

    export const r1 = convertToString(a1); // ""42""
    export const r2 = convertToString(a2); // ""42""
}";
            var result = EvaluateExpressionsWithNoErrors(spec, "M.r1", "M.r2");
            Assert.Equal("42", result["M.r1"]);
            Assert.Equal("42", result["M.r2"]);
        }

        [Fact]
        public void InterpretUnionTypeWithTypeExpression()
        {
            string spec =
@"namespace M {
    interface Custom { x: number; }

    type CustomUnion = Custom | {x: number, y: number};

    function extractX(cu: CustomUnion): number {
        return cu.x;
    }

    export const r1 = extractX(<Custom>{x: 42}); // 42
    export const r2 = extractX({x: 36, y: 41}); // 36
}";
            var result = EvaluateExpressionsWithNoErrors(spec, "M.r1", "M.r2");
            Assert.Equal(42, result["M.r1"]);
            Assert.Equal(36, result["M.r2"]);
        }
    }
}
