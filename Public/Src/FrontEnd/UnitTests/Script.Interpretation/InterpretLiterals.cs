// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Utilities.FormattableStringEx;

namespace Test.DScript.Ast.Interpretation
{
    public class InterpretLiterals : DsTest
    {
        public InterpretLiterals(ITestOutputHelper output)
            : base(output)
        {}

        [Theory]
        [InlineData("0b01", 1)]
        [InlineData("0b11111111111111111", 131071)]
        [InlineData("42", 42)]
        [InlineData("0x2A", 42)]
        [InlineData("0o52", 42)]
        [InlineData("0b101010", 42)]
        [InlineData("0X2A", 42)]
        [InlineData("0O52", 42)]
        [InlineData("0B101010", 42)]
        [InlineData("-42", -42)]
        [InlineData("-2147483648", int.MinValue)]
        public void EvaluateLiterals(string literal, int expectedValue)
        {
            var code = I($"export const r = {literal};");
            var result = EvaluateExpressionWithNoErrors(code, "r");

            Assert.Equal(expectedValue, result);
        }


        [Theory]
        [InlineData("0x1111111111111111111")]
        [InlineData("-11111111111111111111")]
        [InlineData("5100000 * 100000")]
        [InlineData("2147483647 + 2147483647")]
        public void OverflowInLiteral(string literal)
        {
            // Note, that there is two types of overflows:
            // 1. Contant folding overflow that is happening right now only with enums
            // 2. Evaluation overflow that is happening during evaluation.
            // This test checks the first case.
            var code = I($"const enum Foo {{value = {literal}}}");
            EvaluateWithTypeCheckerDiagnostic(code, TypeScript.Net.Diagnostics.Errors.Const_enum_member_initializer_was_evaluated_to_a_non_integer_value);
        }

        [Theory]
        [InlineData("1 << 1111111", 128)] // there is no overflows in C# for left shift!!
        [InlineData("1 << 33", 2)] // there is no overflows in C# for left shift!!

        [InlineData("8 >> 2", 2)]
        [InlineData("-8 >> 2", -2)] // There is a huge difference between >> and >>>. See official documentation for more examples.
        [InlineData("-8 >>> 2", 1073741822)]
        public void EvaluateBitwiseOperations(string literal, int expectedValue)
        {
            var code = I($"export const r = {literal};");
            var result = EvaluateExpressionWithNoErrors(code, "r");

            Assert.Equal(expectedValue, result);
        }
    }
}
