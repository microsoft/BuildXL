// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Values;
using Test.DScript.Ast.Utilities;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class ClosureTests : DsTest
    {
        public ClosureTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void TestVariableOfFunctionType()
        {
            var result = EvaluateSpec(@"
namespace M
{
    export const x = () => {
        return 42;
    };

    export const xVal = x();
}", new[] { "M.x", "M.xVal" });

            result.ExpectNoError();
            result.ExpectValues(count: 2);
            Assert.Equal(typeof(Closure), result.Values[0].GetType());
            Assert.Equal(42, result.Values[1]);
        }

        [Fact]
        public void TestVariableOfFunctionTypeWithTypeDeclaration()
        {
            var result = EvaluateSpec(@"
namespace M
{
    export const x : () => number = () => {
        return 42;
    };

    export const xVal = x();
}", new[] { "M.x", "M.xVal" });


            result.ExpectNoError();
            result.ExpectValues(count: 2);
            Assert.Equal(typeof(Closure), result.Values[0].GetType());
            Assert.Equal(42, result.Values[1]);
        }

        [Fact]
        public void TestVariableOfFunctionTypeWithTypeDeclarationAndNonEmptyArguments()
        {
            var result = EvaluateSpec(@"
namespace M
{
    export const x : (x: number) => number = (x) => {
        return 42 + x;
    };

    export const xOf10 = x(10);
}", new[] { "M.x", "M.xOf10" });

            result.ExpectNoError();
            result.ExpectValues(count: 2);
            Assert.Equal(typeof(Closure), result.Values[0].GetType());
            Assert.Equal(52, result.Values[1]);
        }

        [Fact]
        public void TestClosureVariableShadowing1()
        {
            var result = EvaluateSpec(@"
namespace M
{
    const y = 10;
    export const x = (y) => {
        return 42 + y;
    };
    export const xOf0 = x(0);
}", new[] { "M.xOf0" });

            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal(42, result.Values[0]);
        }

        [Fact]
        public void TestClosureLocalVariableShadowingModuleVariable()
        {
            var result = EvaluateSpec(@"
namespace M
{
    const y = 10;
    export const x = (x) => {
        const y = 42;
        return x + y;
    };
    export const xOf0 = x(0);
}", new[] { "M.xOf0" });

            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal(42, result.Values[0]);
        }

        [Fact]
        public void TestClosureAccessingModuleVariableShadowedByLocalVariable()
        {
            var result = EvaluateSpec(@"
export namespace M
{
    export const y = 10;
    export const x = (x) => {
        const y = 42;
        return x + M.y;
    };
    export const xOf0 = x(0);
}", new[] { "M.xOf0" });

            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal(10, result.Values[0]);
        }

        [Fact]
        public void TestAmbientCallWithClosureWithLocalVariable()
        {
            var result = EvaluateSpec(@"
namespace M
{
    export const x = [1, 2, 3].map((e) => {
        let i = 1;
        return e + i;
    });
}", new[] { "M.x" });

            result.ExpectNoError();
            result.ExpectValues(count: 1);
            var res = result.Values[0] as ArrayLiteral;
            Assert.NotNull(res);

            // To make ReSharper happy.
            Contract.Assume(res != null);
            Assert.Equal(3, res.Length);
            Assert.Equal(2, ((IReadOnlyList<object>) res.ValuesAsObjects())[0]);
            Assert.Equal(3, ((IReadOnlyList<object>) res.ValuesAsObjects())[1]);
            Assert.Equal(4, ((IReadOnlyList<object>) res.ValuesAsObjects())[2]);
        }

        [Fact]
        public void TestHigherOrderFunPlusClosureWithLocalVariable()
        {
            var result = EvaluateSpec(@"
namespace M
{
    function map<A, B>(x: A, mapFn: (a: A) => B): B {
        let i = 10;
        return mapFn(x);
    }

    export const x = map(1, (e) => {
        let i = 1;
        return e + i;
    });
}", new[] { "M.x" });

            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal(2, result.Values[0]);
        }

        [Fact]
        public void TestHigherOrderMethod()
        {
            var result = EvaluateSpec(@"
namespace M
{
    const obj = {
        invoke: (fn) => {
            let i = 5;
            return fn(i);
        }
    };
            
    export const x = obj.invoke((x) => {
        let i = 1;
        return x - i;
    });
}", new[] { "M.x" });

            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal(4, result.Values[0]);
        }

        [Fact]
        public void TestHigherOrderMethodInvokedFromFunction()
        {
            var result = EvaluateSpec(@"
namespace M
{
    const obj = {
        invokeWith5: (fn) => {
            let i = 5;
            return fn(i);
        }
    };

    function f() {
        let i = 10; let k = 0; let l = 0; let m = 0;
        let j = obj.invokeWith5((x) => {
            let i = 1;
            return x - i; 
        });
        return i + j;
    }

    export const x = f();
}", new[] { "M.x" });

            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal(14, result.Values[0]);
        }

        [Fact]
        public void TestNested()
        {
            var result = EvaluateSpec(@"
namespace M
{
    export const x = ((e) => {
        return ((f) => f + 1)(e);
    })(1);
}", new[] { "M.x" });

            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal(2, result.Values[0]);
        }

        [Fact(Skip = "For some reason the interpreter does not allow this particular variable shadowing")]
        public void TestNestedShadowed()
        {
            var result = EvaluateSpec(@"
namespace M
{
    export const x = ((e) => {
        return ((e) => e + 1)(e);
    })(1);
}", new[] { "M.x" });

            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal(2, result.Values[0]);
        }

        [Fact(Skip = "Currently the interpreter allows variable redeclaration")]
        public void TestVariableRedeclaration()
        {
            var result = EvaluateSpec(@"
namespace M
{
    function f() {
        let i = 10; 
        let fn = (x) => { return x - i; };
        let i = 30;
        return fn(i);
    }

    export const x = f();
}", new[] { "M.x" });

            result.ExpectErrors(1);
        }
    }
}
