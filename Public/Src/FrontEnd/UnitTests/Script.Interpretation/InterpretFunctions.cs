// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Values;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class InterpretFunctions : DsTest
    {
        public InterpretFunctions(ITestOutputHelper output) : base(output) { }
        
        [Fact]
        public void EvaluateFunctionWithUnderscroreInItsName()
        {
            string spec =
@"function __foo() {
   return 42;
}

export const r = __foo();";

            var result = EvaluateExpressionWithNoErrors(spec, "r");
            Assert.Equal(42, result);
        }

        [Fact]
        public void EvaluateFunctionWithLocalFunction()
        {
            string spec =
@"function foo() {

  function bar() {return 42;}
  return bar();
}

export const r = foo();";
            EvaluateWithDiagnosticId(spec, LogEventId.LocalFunctionsAreNotSupported);
        }

        [Fact]
        public void EvaluateFunctionInvocation()
        {
            string spec =
@"namespace M
{
    function func() { return 42; }
    
    export const x = func();
}";
            var result = EvaluateExpressionWithNoErrors(spec, "M.x");
            Assert.Equal(42, result);
        }

        [Fact]
        public void EvaluateFunctionInvocationViaDelegate()
        {
            string spec =
@"function simpleFunction(n1: number, n2: number): number {
   return n1 + n2;
}

const iterationsCount = 42;

function measure() {
    for (let i = 0; i < iterationsCount; i++)
    {
        simpleFunction(i, i + 1);
    }
    return iterationsCount;
}

        export const r = measure();";
            var result = EvaluateExpressionWithNoErrors(spec, "r");
            Assert.Equal(42, result);
        }

        [Fact]
        public void CallFunctionInTheLoop()
        {
            string spec =
@"namespace M
{
    function func() { return 42; }
    const fn = func;
    export const x = fn();
}";
            var result = EvaluateExpressionWithNoErrors(spec, "M.x");
            Assert.Equal(42, result);
        }

        [Fact]
        public void EvaluateFunctionInvocationWithIdentity()
        {
            string spec =
@"namespace M
{
    function func(x: number) { return x; }
    export const x = func(41) + 1;
}";
            var result = EvaluateExpressionWithNoErrors(spec, "M.x");
            Assert.Equal(42, result);
        }

        [Fact]
        public void EvaluateFunctionWithImplicitReturn()
        {
            string spec =
@"namespace M
{
    function func(x: number) {}
    export const x = func(42);
}";
            var result = EvaluateExpressionWithNoErrors(spec, "M.x");
            Assert.Equal(UndefinedValue.Instance, result);
        }

        [Fact]
        public void FunctionCanReturnGlobal()
        {
            string spec =
@"namespace M
{
    const tmp = 42;
    function func() { return tmp; }
    export const x = func();
}";
            var result = EvaluateExpressionWithNoErrors(spec, "M.x");
            Assert.Equal(42, result);
        }

        [Fact]
        public void EvaluateAbsFunction()
        {
            string spec =
@"namespace M
{
    function abs(x: number) { if (x < 0) return -x; else return x; }
    export const x = abs(-42);
}";
            var result = EvaluateExpressionWithNoErrors(spec, "M.x");
            Assert.Equal(42, result);
        }
        [Fact]
        public void EvaluateAbsFunctionWithTernaryOperator()
        {
            // Note, this sample doesn't work in coco-based parser!
            string spec =
@"namespace M
{
    function abs(x: number) { return x < 0 ? -x : x; }
    export const x = abs(-42);
}";
            var result = EvaluateExpressionWithNoErrors(spec, "M.x");
            Assert.Equal(42, result);
        }

        [Fact]
        public void EvaluateFunctionWithIfStatement()
        {
            string spec =
@"namespace M
{
    function abs(x: number) { if (x > 10) return x + 1; return x; }
    export const x = abs(41);
    export const y = abs(7);
}";
            var result = EvaluateExpressionWithNoErrors(spec, "M.x");
            Assert.Equal(42, result);

            result = EvaluateExpressionWithNoErrors(spec, "M.y");
            Assert.Equal(7, result);
        }

        [Fact]
        public void TestFibonacci()
        {
            string code = @"
namespace M {
    function fibo(n: number): number {
        if (n === 0) { return 0; }
        if (n === 1) { return 1; }
        return fibo(n - 1) + fibo(n - 2);
    }

    export const x = fibo(7);
}";
            var result = EvaluateExpressionWithNoErrors(code, "M.x");

            Assert.Equal(13, result);
        }

        [Fact]
        public void TestSpreadCallWithArray()
        {
            string code = @"
namespace M 
{
    function f(dummy: number, ...x: any[]) : number {
        return x.length;
    }

    const arr = [1, 2, 3];
    export const fArr = f(0, arr); // 1
    export const fArr2 = f(0, [1,2,3]); // 1
    export const fSpreadArr = f(0, ...arr); // 3
}";

            var result = EvaluateExpressionsWithNoErrors(code, "M.fArr", "M.fSpreadArr", "M.fArr2");
            Assert.Equal(1, result["M.fArr"]);
            Assert.Equal(1, result["M.fArr2"]);
            Assert.Equal(3, result["M.fSpreadArr"]);
        }

        [Fact]
        public void TestSpreadCallWithManualList()
        {
            string code = @"
namespace M 
{
    function f(dummy: number, ...x: any[]) : number {
        if (!x) {return undefined;}
        return x.length;
    }

    export const r1 = f(0); // 0
    export const r2 = f(0, 1, 2); // 2
}";

            var result = EvaluateExpressionsWithNoErrors(code, "M.r1", "M.r2");
            Assert.Equal(0, result["M.r1"]);
            Assert.Equal(2, result["M.r2"]);
        }

        [Fact]
        public void TestReturnEmptyStatement()
        {
            string code = @"
namespace M {
    export function f() {
        return;
    }
}

export const r = M.f(); // undefined";

            var result = EvaluateExpressionWithNoErrors(code, "r");
            Assert.Equal(UndefinedValue.Instance, result);
        }
    }
}
