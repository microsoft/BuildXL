// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Values;
using Test.BuildXL.FrontEnd.Core;
using TypeScript.Net.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class InterpretSwitch : DsTest
    {
        public InterpretSwitch(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void EvaluationShouldFailOnDuplicateIdentifierInDifferentCaseBlocks()
        {
            string code = @"
function fun(s: string) {
    switch(s) {
        case ""1"": 
            let x = 42;
            return 1;
        case ""2"":
            let x = 43;
            return 2;
        default: return undefined;
    }
}";
            EvaluateWithTypeCheckerDiagnostic(code, Errors.Cannot_redeclare_block_scoped_variable_0, "x");
        }

        [Fact]
        public void EvaluationShouldFailIfCaseDefineTheSame()
        {
            string code = @"
function fun(s: string) {
    switch(s) {
        case ""1"": 
            let x = 42;
            return 1;
        case ""2"":
            let x = 43;
            return 2;
        default: return undefined;
    }
}";
            EvaluateWithTypeCheckerDiagnostic(code, Errors.Cannot_redeclare_block_scoped_variable_0, "x");
        }

        [Fact]
        public void EvalutionShouldNotFailOnDuplicateIdentifierInDifferentCaseBlocksWhenDeclaredInScope()
        {
            string code = @"
function fun(s: number) {
    switch(s) {
        case 1: 
        {
            let x = 42;
            return 1;
        }
        case 2:
        {
            let x = 43;
            return 2;
        }
        default: return undefined;
    }
}
export const r = fun(1); //1
";
            var result = EvaluateExpressionWithNoErrors(code, "r");
            Assert.Equal(1, result);
        }

        [Fact]
        public void EvalutionShouldNotFailWhenEachVariableDefinedInOnwScope()
        {
            string code = @"
function fun(s: number) {
    let x = 42;
    switch(s) {
        case 1: 
            let x = 42;
            return 1;
        case 2:
        {
            let x = 43;
            return 2;
        }
        default: return undefined;
    }
}
export const r = fun(1); //1
";
            var result = EvaluateExpressionWithNoErrors(code, "r");
            Assert.Equal(1, result);
        }

        [Fact]
        public void EvaluateSwitchWithReturn()
        {
            string code = @"
namespace M {
    function fun(s: string) {
        switch(s) {
           case ""1"": return 1;
           case ""2"": return 2;
           case ""3"": return 3;
           default: return undefined;
        }
    }

    export const r1 = fun(""1""); // 1
    export const r2 = fun(""3""); // 3
    export const r3 = fun(""6""); // undefined
}";
            var result = EvaluateExpressionsWithNoErrors(code, "M.r1", "M.r2", "M.r3");

            Assert.Equal(1, result["M.r1"]);
            Assert.Equal(3, result["M.r2"]);
            Assert.Equal(UndefinedValue.Instance, result["M.r3"]);
        }

        [Fact]
        public void EvaluateSwitchWithBreaks()
        {
            string code = @"
namespace M {
    function fun(s: string) {
        let r: number = undefined;
        switch(s) {
           case ""1"": r = 1; break;
           case ""2"": r = 2; break;
           case ""3"": r = 3; break;
        }

        return r;
    }

    export const r1 = fun(""1""); // 1
    export const r2 = fun(""3""); // 3
    export const r3 = fun(""6""); // undefined
}";
            var result = EvaluateExpressionsWithNoErrors(code, "M.r1", "M.r2", "M.r3");

            Assert.Equal(1, result["M.r1"]);
            Assert.Equal(3, result["M.r2"]);
            Assert.Equal(UndefinedValue.Instance, result["M.r3"]);
        }

        [Fact]
        public void EvaluateSwitchWithFallThrough()
        {
            string code = @"
namespace M {
    function fun(s: string) {
        let r: number = undefined;
        switch(s) {
           case ""1"":
           case ""2"": r = 2; break;
           case ""3"": r = 3; break;
        }

        return r;
    }

    export const r1 = fun(""1""); // 2
    export const r2 = fun(""3""); // 3
    export const r3 = fun(""6""); // undefined
}";
            var result = EvaluateExpressionsWithNoErrors(code, "M.r1", "M.r2", "M.r3");

            Assert.Equal(2, result["M.r1"]);
            Assert.Equal(3, result["M.r2"]);
            Assert.Equal(UndefinedValue.Instance, result["M.r3"]);
        }

        [Fact]
        public void EvaluateSwitchWithSideEffectInFallThrough()
        {
            string code = @"
namespace M {
    function fun(s: string) {

        let r: number = 0;
        switch(s) {
           case ""1"": r += 1;
           case ""2"": {r += 1; break;}
        }

        return r;
    }

    export const r1 = fun(""1""); // 2
    export const r2 = fun(""2""); // 1
}";
            var result = EvaluateExpressionsWithNoErrors(code, "M.r1", "M.r2");

            Assert.Equal(2, result["M.r1"]);
            Assert.Equal(1, result["M.r2"]);
        }
    }
}
