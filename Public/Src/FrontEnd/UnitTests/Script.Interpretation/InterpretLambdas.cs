// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class InterpretLambdas : DsTest
    {
        public InterpretLambdas(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void CustomAmbientFunctionsAreNotAllowed()
        {
            string code =
@"declare function foo();
// this call led to undefined dereference previously.
export const r = foo();";

            var result = EvaluateWithFirstError(code);
            Assert.Equal(LogEventId.NotSupportedCustomAmbientFunctions, (LogEventId)result.ErrorCode);
        }

        [Fact]
        public void CustomAmbientFunctionsDeclarationsAreNotAllowed()
        {
            // Just declaration should fail.
            string code =
@"declare function foo();";

            var result = EvaluateWithFirstError(code);
            Assert.Equal(LogEventId.NotSupportedCustomAmbientFunctions, (LogEventId)result.ErrorCode);
        }

        [Fact]
        public void EvaluateSimpleMapFunction()
        {
            string code = @"
namespace M {
    function join(ar: number[], s: string): string {
        let r = ar.map(e => e.toString());
        return r.join(s);
    }

    export const r1 = join([1, 2, 3], """"); // 123
    export const r2 = join([3, 4, 6], "",""); // 3,4,6
}";
            var result = EvaluateExpressionsWithNoErrors(code, "M.r1", "M.r2");

            Assert.Equal("123", result["M.r1"]);
            Assert.Equal("3,4,6", result["M.r2"]);
        }

        [Fact]
        public void EvaluateSimpleMapFunctionWithReturn()
        {
            string code = @"
namespace M {
    function join(ar: number[], s: string): string {
        let r = ar.map((e: number) => {let r = e + 1; return r.toString();});
        return r.join(s);
    }

    export const r1 = join([1, 2, 3], """"); // 234
    export const r2 = join([3, 4, 6], "",""); // 4,5,7
}";
            var result = EvaluateExpressionsWithNoErrors(code, "M.r1", "M.r2");

            Assert.Equal("234", result["M.r1"]);
            Assert.Equal("4,5,7", result["M.r2"]);
        }

        [Fact]
        public void EvaluateFunctionThatReturnsLambda()
        {
            string code = @"
namespace M {
    function foo(n: number): () => number {
        return () => n;
    }

    const fun = foo(42);
    export const r = fun(); // 42
}";
            var result = EvaluateExpressionWithNoErrors(code, "M.r");

            Assert.Equal(42, result);
        }

        [Fact]
        public void EvaluateCapturingLoopVariable()
        {
            string code = @"
namespace M {
    function foo(ar: number[]): (() => number)[] {
        let result = [];
        for(let e of ar) {
            result = [...result, () => e];
        }

        return result;
    }

    const ar = [1, 2, 3];
    const funs = foo(ar);
    export const r1 = funs[0]();
    export const r2 = funs[1]();
    export const r3 = funs[2]();
}";
            var result = EvaluateExpressionsWithNoErrors(code, "M.r1", "M.r2", "M.r3");

            // This test shows that DScript has similar semantic of capturing lambdas as JavaScript!
            Assert.Equal(3, result["M.r1"]);
            Assert.Equal(3, result["M.r2"]);
            Assert.Equal(3, result["M.r3"]);
        }

        [Fact]
        public void EvaluateMutatingClosureIsNotAllowed()
        {
            string code = @"
namespace M {
    function foo(n: number): () => number {
        let local = n;
        return () => {n += 1; return n;};
    }

    export const r = foo(1);
}";
            var result = EvaluateWithFirstError(code, "M.r");
            Assert.Equal((int)LogEventId.OuterVariableCapturingForMutationIsNotSupported, result.ErrorCode);
        }

        [Fact]
        public void EvaluateMutatingClosureWithIncrementIsNotAllowed()
        {
            string code = @"
namespace M {
    function foo(n: number): () => number {
        let local = n;
        return () => {return n++;};
    }

    export const r = foo(1);
}";
            var result = EvaluateWithFirstError(code, "M.r");
            Assert.Equal((int)LogEventId.OuterVariableCapturingForMutationIsNotSupported, result.ErrorCode);
        }
    }
}
