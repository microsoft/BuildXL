// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class EvaluateUnaryExpressions : DsTest
    {
        public EvaluateUnaryExpressions(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void EvaluateTypeOfOnImportFrom()
        {
            // Bug #880768
            string spec1 = 
@"export const x = 42;";

            string spec2 =
@"export const r = typeof importFrom('ModuleA');";

            var result = Build()
                .AddSpec("spec1.dsc", spec1)
                .AddSpec("spec2.dsc", spec2)
                .AddSpec("ModuleA\\module.config.dsc", "module({name: 'ModuleA'});")
                .AddSpec("ModuleA\\project.dsc", "@@public export const x = 42;")
                .RootSpec("spec2.dsc")
                .EvaluateExpressionWithNoErrors("r");
            Assert.NotNull(result);
        }

        [Theory]
        [InlineData("typeof undefined", "undefined")]
        [InlineData("typeof p`a/b/c`", "Path")]
        [InlineData("typeof PathAtom.create(\"atom\")", "PathAtom")]
        [InlineData("typeof \"foo\"", "string")]
        [InlineData("typeof 7", "number")]
        [InlineData("typeof !!true", "boolean")]
        [InlineData("typeof {a: 1, b: 2}", "object")]
        [InlineData("typeof [1, 2, 3]", "array")]
        [InlineData("typeof f", "function")]
        [InlineData("typeof Map.empty<string, number>()", "Map")]
        [InlineData("typeof Set.empty<number>()", "Set")]
        [InlineData("typeof CustomEnum.value", "enum")]
        [InlineData("typeof <CustomInterface>{}", "object")]
        public void EvaluateTypeOf(string expression, string expectedResult)
        {
            const string CodeTemplate = @"
interface CustomInterface {{}}

function f() {{ return 0; }}
const enum CustomEnum {{ value }}

export const result = {0};";
            string code = string.Format(CodeTemplate, expression);

            var result = EvaluateExpressionWithNoErrors(code, "result");

            Assert.Equal(expectedResult, result);
        }

        [Theory]
        [InlineData("+\"42\"", 42)]
        [InlineData("+\"0xA\"", 10)]
        [InlineData("+\"0b11\"", 3)]
        [InlineData("+\"0o7\"", 7)]
        [InlineData("+42", 42)]
        [InlineData("+true", 1)]
        [InlineData("+false", 0)]
        [InlineData("+returnsString42()", 42)]
        [InlineData("+CustomEnum.value4", 4)]
        public void EvaluateUnaryPlus(string expression, int expectedResult)
        {
            const string CodeTemplate = @"
function returnsString42() {{return ""42"";}}
const enum CustomEnum {{ value4 = 4 }}

export const result = {0};";
            string code = string.Format(CodeTemplate, expression);

            var result = EvaluateExpressionWithNoErrors(code, "result");

            Assert.Equal(expectedResult, result);
        }

        [Theory]
        [InlineData("+unknown", global::BuildXL.FrontEnd.Core.Tracing.LogEventId.CheckerError)]
        [InlineData("+\"111111111111111111111111111111111\"", LogEventId.ArithmeticOverflow)] // number is too long
        [InlineData("+\"1.2\"", LogEventId.InvalidFormatForStringToNumberConversion)] // Floating points are not supported!
        [InlineData("+\"NotANumber\"", LogEventId.InvalidFormatForStringToNumberConversion)]
        [InlineData("+undefined", LogEventId.UnexpectedValueType)]
        [InlineData("+functionFoo", LogEventId.UnexpectedValueType)]
        [InlineData("+{x: 42}", LogEventId.UnexpectedValueType)]
        public void EvaluateUnaryPlusWithFailure(string expression, LogEventId expectedEventId)
        {
            const string CodeTemplate = @"
function functionFoo() {{return ""42"";}}
export const result = {0};";
            string code = string.Format(CodeTemplate, expression);

            EvaluateWithDiagnosticId(code, expectedEventId);
        }

        [Theory]
        [InlineData("--x", -1, -1)]
        [InlineData("++x", +1, +1)]
        [InlineData("x--", -1, 0)]
        [InlineData("x++", +1, 0)]
        [InlineData("x++ + x++ + x++", 1 + 1 + 1, 0 + 1 + 2)]
        [InlineData("x-- + x-- + x--", -1 + -1 + -1, 0 - 1 - 2)]
        [InlineData("++x + ++x + ++x", 1 + 1 + 1, 1 + 2 + 3)]
        [InlineData("--x + --x + --x", -1 + -1 + -1, -1 + -2 + -3)]
        public void EvaluatePrefixAndSuffixOperators(string expression, int stateResult, int expressionResult)
        {
            const string CodeTemplate = @"
function foo() {{
  let x = 0; 
  let y = {0};
  return x * 10 + y;
}}

export const r = foo();
";
            var code = string.Format(CodeTemplate, expression);

            var result = EvaluateExpressionWithNoErrors(code, "r");

            Assert.Equal((stateResult * 10) + expressionResult, result);
        }

        [Fact]
        public void IncrementOperatorInLoop()
        {
            const string Code = @"
function foo() {{
  let x = 0; 
  for (let y=0; y<10; y++) x++;
  return x;
}}

export const r = foo();
";
            var result = EvaluateExpressionWithNoErrors(Code, "r");

            Assert.Equal(10, result);
        }

        [Theory]
        [InlineData("1++", LogEventId.OperandOfIncrementOrDecrementOperatorMustBeLocalVariable)]
        [InlineData("--1", LogEventId.OperandOfIncrementOrDecrementOperatorMustBeLocalVariable)]
        public void AssignmentMustOperateOnLocalVariable(string expression, LogEventId expectedEventId)
        {
            const string CodeTemplate = @"
function foo() {{
  let x = 0; 
  {0};
  return x;
}}

export const r = foo();
";

            var code = string.Format(CodeTemplate, expression);
            EvaluateWithDiagnosticId(code, expectedEventId);
        }
    }
}
