// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Utilities.FormattableStringEx;

namespace Test.DScript.Ast.Interpretation
{
    public class EvaluateArithmeticOperations : DsTest
    {
        public EvaluateArithmeticOperations(ITestOutputHelper output)
            : base(output)
        {}



        [Theory]
        [InlineData("1 << 1111111", 128)] // there is no overflows in C# for left shift!!
        [InlineData("1 << 33", 2)] // there is no overflows in C# for left shift!!
        [InlineData("111111111 >> 32", 111111111)] // there is no overflows in C# for right shift!!

        [InlineData("8 >> -5", 0)]
        [InlineData("8 >> 2", 2)]
        [InlineData("1 >> 0", 1)]
        [InlineData("-8 >> 2", -2)] // There is a huge difference between >> and >>>. See official documentation for more examples.
        [InlineData("-8 >>> 2", 1073741822)]

        [InlineData("(1<<5) | (1<<6)", 96)]
        [InlineData("(3<<5) & (1<<6)", 64)]
        [InlineData("(3<<5) ^ (~(1<<6))", -33)]
        public void EvaluateBitwiseOperations(string literal, int expectedValue)
        {
            var code = I($"export const r = {literal};");
            var result = EvaluateExpressionWithNoErrors(code, "r");

            Assert.Equal(expectedValue, result);
        }

        [Theory]
        [InlineData("1 + 1111111", 1111112)]
        [InlineData("25 * 26", 650)]
        [InlineData("25 % 4", 1)]
        [InlineData("2 ** 4", 16)]
        [InlineData("2 ** 0", 1)]
        public void EvaluateBasicMathOperations(string literal, int expectedValue)
        {
            var code = I($"export const r = {literal};");
            var result = EvaluateExpressionWithNoErrors(code, "r");

            Assert.Equal(expectedValue, result);
        }

        [Theory]
        [InlineData("-1 >>> 0", LogEventId.ArithmeticOverflow)] // throws because -1 >>> 0 lead to uint.MaxValue that doesn't fit into int.MaxValue
        [InlineData("5100000 * 100000", LogEventId.ArithmeticOverflow)]
        [InlineData("2147483647 + 2147483647", LogEventId.ArithmeticOverflow)]
        [InlineData("55 ** 1000", LogEventId.ArithmeticOverflow)]
        [InlineData("55 ** -1", LogEventId.ArgumentForPowerOperationShouldNotBeNegative)]
        [InlineData("-(-2147483647-1)", LogEventId.ArithmeticOverflow)]
        public void EvaluateArithmeticOperationsWithOverflow(string literal, LogEventId expectedEventId)
        {
            var code = I($"export const r = {literal};");
            var result = EvaluateWithFirstError(code);

            Assert.Equal(expectedEventId, (LogEventId)result.ErrorCode);
        }

        [Theory]
        [InlineData(2, "+= 42", 44)]
        [InlineData("\"a\"", "+= \"b\"", "ab")]
        [InlineData(36, "*= 0", 0)]
        [InlineData(1, "*= 56555", 56555)]
        [InlineData(10, "%= 5", 0)]
        [InlineData(2, "**= 4", 16)]
        [InlineData(1, "<<= 2", 4)]
        [InlineData(1, "<<= 33", 2)]
        [InlineData(1, ">>= 1", 0)]
        [InlineData(3, ">>= 33", 1)] // 3 >> 33 is equivalent to 3 >> 1.
        [InlineData(1, ">>>= 1", 0)]
        [InlineData(1, "&= 2", 0)]
        [InlineData(1, "&= 3", 1)]
        [InlineData(1, "^= 1", 0)]
        [InlineData(1, "^= 3", 2)]
        [InlineData(1, "|= (1<<1)", 3)]
        public void EvaluateCompoundOperators(object initialValue, string expression, object expectedValue)
        {
            const string CodeTemplate = @"
function foo() {{
  let x = {0}; 
  x {1};

  return x;
}}

export const r = foo();
";
            var code = string.Format(CodeTemplate, initialValue, expression);
            var result = EvaluateExpressionWithNoErrors(code, "r");

            Assert.Equal(expectedValue, result);
        }

        [Theory]
        [InlineData("\"a\"", "+= 42")]
        [InlineData("42", "+= \"a\"")]
        public void EvaluateCompoundOperatorErrors(object initialValue, string expression)
        {
            const string CodeTemplate = @"
function foo() {{
  let x: any = {0}; 
  x {1};

  return x;
}}

export const r = foo();
";
            var code = string.Format(CodeTemplate, initialValue, expression);
            var result = EvaluateWithFirstError(code, "r");

            Assert.Equal((int)LogEventId.UnexpectedValueTypeForName, result.ErrorCode);
        }

        [Fact]
        public void CompoundOrShouldWorkWithEnums()
        {
            string code = @"
const enum CustomEnum {
  value1 = 1 << 1,
  value2 = 1 << 2,
  value3 = 1 << 3,
}

// Returning CustomEnum.value1 | CustomEnum.value3 in a weird way!
function createCustomEnumValue1Or3() {{
  let x = CustomEnum.value1; 
  x |= CustomEnum.value3;
  return x;
}}

const enumValue = createCustomEnumValue1Or3();
export const r = enumValue === (CustomEnum.value1 | CustomEnum.value3);
";
            var result = EvaluateExpressionWithNoErrors(code, "r");

            Assert.Equal(true, result);
        }

        [Fact]
        public void CompoundAndShouldWorkWithEnums()
        {
            string code = @"
const enum CustomEnum {
  value1 = 1 << 1,
  value2 = 1 << 2,
  value3 = 1 << 3,
}

// Returning CustomEnum.value1 | CustomEnum.value3 in a weird way!
function createCustomEnumValue1Or3() {{
  let x = CustomEnum.value1 | CustomEnum.value2 | CustomEnum.value3; 
  x &= ~CustomEnum.value2;
  return x;
}}

const enumValue = createCustomEnumValue1Or3();
export const r = enumValue === (CustomEnum.value1 | CustomEnum.value3);
";
            var result = EvaluateExpressionWithNoErrors(code, "r");

            Assert.Equal(true, result);
        }

        [Fact]
        public void CompoundXorShouldWorkWithEnums()
        {
            string code = @"
const enum CustomEnum {
  value1 = 1 << 1,
  value2 = 1 << 2,
  value3 = 1 << 3,
}

// Returning CustomEnum.value1 | CustomEnum.value3 in a weird way!
function createCustomEnumValue1Or3() {{
  let x = CustomEnum.value1 | CustomEnum.value2 | CustomEnum.value3; 
  x ^= CustomEnum.value2;
  return x;
}}

const enumValue = createCustomEnumValue1Or3();
export const r = enumValue === (CustomEnum.value1 | CustomEnum.value3);
";
            var result = EvaluateExpressionWithNoErrors(code, "r");

            Assert.Equal(true, result);
        }

        [Theory]
        [InlineData(-1, ">>>= 0", LogEventId.ArithmeticOverflow)] // throws because -1 >>> 0 lead to uint.MaxValue that doesn't fits into int.MaxValue
        [InlineData(5100000, " *= 100000", LogEventId.ArithmeticOverflow)]
        [InlineData(2147483647, " += 2147483647", LogEventId.ArithmeticOverflow)]
        [InlineData(55, " **= 1000", LogEventId.ArithmeticOverflow)]
        [InlineData(42, " **= -1", LogEventId.ArgumentForPowerOperationShouldNotBeNegative)]
        public void EvaluateArithmeticOperationsWithError(int initialValue, string expression, LogEventId expectedEventId)
        {
            const string CodeTemplate = @"
function foo() {{
  let x = {0}; 
  x {1};

  return x;
}}

export const r = foo();
";
            var code = string.Format(CodeTemplate, initialValue, expression);

            var result = EvaluateWithFirstError(code);

            Assert.Equal(expectedEventId, (LogEventId)result.ErrorCode);
        }

        [Theory]
        [InlineData("1+=1")]
        [InlineData("1*=1")]
        public void AssignmentMustOperateOnLocalVariable(string expression)
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

            EvaluateWithTypeCheckerDiagnostic(code, TypeScript.Net.Diagnostics.Errors.Invalid_left_hand_side_of_assignment_expression);
        }
    }
}
