// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.PrettyPrinter
{
    public class PrettyPrinterDeclarationTests : DsTest
    {
        public PrettyPrinterDeclarationTests(ITestOutputHelper output) : base(output) { }

        #region Var

        [Fact]
        public void VarUntyped()
        {
            TestDeclaration("const a = 1;", "const a = 1;");
        }

        [Fact]
        public void VarTypedNumber()
        {
            TestDeclaration("const a : number = 1;");
        }

        [Fact]
        public void VarTypedString()
        {
            TestDeclaration("const a : string = \"a\";");
        }

        [Fact]
        public void VarTypedBoolean()
        {
            TestDeclaration("const a : boolean = true;");
        }

        [Fact]
        public void VarTypedAny()
        {
            TestDeclaration("const a : any = true;");
        }

        #endregion

        #region Function

        [Fact]
        public void FunctionNoGenericsNoArgsNoTypeNoBody()
        {
            TestDeclaration("function f () {}", expected: @"function f() {
}");
        }

        [Fact]
        public void FunctionNoGenericsNoArgsNoTypeBody()
        {
            TestDeclaration("function f () {let a= 1;}", expected: @"function f() {
    let a = 1;
}");
        }

        [Fact]
        public void FunctionSingleArg()
        {
            TestDeclaration("function f (a) {}", expected: @"function f(a) {
}");
        }

        [Fact]
        public void FunctionSingleTypedArg()
        {
            TestDeclaration("function f (a : string) {}", expected: @"function f(a: string) {
}");
        }

        [Fact]
        public void FunctionSingleOptionalTypedArg()
        {
            TestDeclaration("function f (a? : string) {}", expected: @"function f(a?: string) {
}");
        }

        [Fact]
        public void FunctionSingleParamsArg()
        {
            TestDeclaration("function f (...params) {}", expected: @"function f(...params) {
}");
        }

        [Fact]
        public void FunctionSingleTypedParamsArg()
        {
            TestDeclaration("function f (...params : string[]) {}", expected: @"function f(...params: string[]) {
}");
        }

        [Fact]
        public void FunctionSingleArgWithParams()
        {
            TestDeclaration("function f (a, ...params) {}", expected: @"function f(a, ...params) {
}");
        }

        [Fact]
        public void FunctionSingleArgWithTypedParams()
        {
            TestDeclaration("function f (a, ...params : string[]) {}", expected: @"function f(a, ...params: string[]) {
}");
        }

        [Fact]
        public void FunctionGenerics()
        {
            TestDeclaration("function f<T> (a) {}", expected: @"function f<T>(a) {
}");
        }

        #endregion

        #region Interface

        [Fact]
        public void InterfaceEmpty()
        {
            TestDeclaration("interface IA {}");
        }

        [Fact]
        public void InterfaceEmptyGeneric()
        {
            TestDeclaration("interface IA<T> {}");
        }

        [Fact]
        public void InterfaceExtendsOne()
        {
            TestDeclaration("interface IB {} interface IA<T> extends IB {}");
        }

        [Fact]
        public void InterfaceExtendsTwo()
        {
            TestDeclaration("interface IB {} interface IC {} interface IA<T> extends IB, IC {}");
        }

        [Fact]
        public void InterfaceSingleTypedMember()
        {
            TestDeclaration("interface IA {field: string;}", expected: "interface IA {field: string}");
        }

        [Fact]
        public void InterfaceMultipleTyped()
        {
            TestDeclaration("interface IA {a: number; b: number; c: number;}", expected: @"interface IA {
    a: number;
    b: number;
    c: number
}");
        }

        [Fact]
        public void InterfaceFunMember()
        {
            TestDeclaration("interface IA {fun(a: string, b: string): boolean;}", expected: @"interface IA {fun: (a: string, b: string) => boolean}");
        }

        [Theory]
        [InlineData(@"type NumToStr = (a: number) => string;")]
        [InlineData(@"type MapFn<E, V> = (elem: E) => V;")]
        [InlineData(@"type FoldFn<A, E> = (accumulator: A, elem: E) => A;")]
        public void FunctionTypeAsInterface(string decl)
        {
            TestDeclaration(decl);
        }

        #endregion

        #region Enum

        [Fact]
        public void EnumEmpty()
        {
            TestDeclaration("const enum MyEnum {}", "enum MyEnum {}");
        }

        [Fact]
        public void EnumSingle()
        {
            TestDeclaration("const enum MyEnum {val}", "enum MyEnum {val=0}");
        }

        [Fact]
        public void EnumMultiple()
        {
            TestDeclaration("const enum MyEnum {val1 = 2, val2 = 3}", "enum MyEnum {val1 = 2, val2 = 3}");
        }

        #endregion

        #region TypeLiterals

        [Fact]
        public void TypeString()
        {
            TestDeclaration("const a : string = undefined;");
        }

        [Fact]
        public void TypeNumber()
        {
            TestDeclaration("const a : number = undefined;");
        }

        [Fact]
        public void TypeBoolean()
        {
            TestDeclaration("const a : boolean = undefined;");
        }

        [Fact]
        public void TypeAny()
        {
            TestDeclaration("const a : any = undefined;");
        }

        [Fact]
        public void TypeArray()
        {
            TestDeclaration("const a : string[] = undefined;");
        }

        [Fact]
        public void TypeReference()
        {
            TestDeclaration("interface TA {} const a : TA = undefined;");
        }

        [Fact]
        public void TypeReferenceGeneric()
        {
            TestDeclaration("interface TA<T> { } const a : TA<string> = undefined;");
        }

        [Fact]
        public void TypeFunction1()
        {
            TestDeclaration("const f : (a) => string = undefined;");
        }

        [Fact]
        public void TypeFunction2()
        {
            TestDeclaration(
                "const f : (a: number, b: string) => string = (a, b) => b;",
                @"const f : (a: number, b: string) => string = (a, b) => return b;;");
        }

        [Fact]
        public void TypeFunction3()
        {
            TestDeclaration(
                "const f : (a: number, b: string) => number = (a, b) => { return a; };",
                @"const f : (a: number, b: string) => number = (a, b) => {
    return a;
};");
        }

        [Fact]
        public void TypeObject()
        {
            TestDeclaration("const a : {a: string} = undefined;");
        }

        [Fact]
        public void TypeAlias1()
        {
            TestDeclaration("type T = string | number;");
        }

        #endregion

        private void TestDeclaration(string source, string expected = null, PrettyPrintedFileKind fileKind = PrettyPrintedFileKind.Project)
        {
            expected = expected ?? source;
            PrettyPrint(source, expected, fileKind);
        }
    }
}
