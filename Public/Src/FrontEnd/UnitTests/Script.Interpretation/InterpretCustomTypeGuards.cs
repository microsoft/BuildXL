// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class InterpretCustomTypeGuards : DsTest
    {
        public InterpretCustomTypeGuards(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void EvaluateCustomTypeGuard()
        {
            string code = @"
interface X {
    kind: ""X"";
    x: number;
}

interface Y {
    kind: ""Y"";
    y: number;
}

type X_Or_Y = X | Y;

function isX(input: X_Or_Y): input is X {
    return input.kind === ""X"";
}

function isY(input: X_Or_Y): input is Y {
    return input.kind === ""Y"";
}

function getNumber(input: X_Or_Y): number {
    if (isX(input)) {
        return input.x;
    }
    
    if (isY(input)) {
        return input.y;
    }
    
    return undefined;
}

export const r1 = getNumber({kind: ""X"", x: 42});
export const r2 = getNumber({kind: ""Y"", y: 36});";
            var result = EvaluateExpressionsWithNoErrors(code, "r1", "r2");

            Assert.Equal(42, result["r1"]);
            Assert.Equal(36, result["r2"]);
        }
    }
}
