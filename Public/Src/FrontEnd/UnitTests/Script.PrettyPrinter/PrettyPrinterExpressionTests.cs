// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.PrettyPrinter
{
    public class PrettyPrinterExpressionTests : DsTest
    {
        public PrettyPrinterExpressionTests(ITestOutputHelper output) : base(output) { }

        #region LambdaExpression

        [Fact]
        public void LambdaNoArgsExpr()
        {
            TestExpression(
                "() => 1",
                expected:
                    @"() => return 1;");
        }

        [Fact]
        public void LambdaNoArgsExprEmptyObject()
        {
            TestExpression(
                "() => {}",
                expected:
                    @"() => {
}");
        }

        [Fact]
        public void LambdaNoArgsExprSingleIdentifierField()
        {
            TestExpression(
                "() => { return {field: 1}; }",
                expected:
                    @"() => {
    return {field: 1};
}");
        }

        [Fact]
        public void LambdaNoArgsExprMultipleIdentifierFields()
        {
            TestExpression(
                "() => { return {field: 1, otherField: 2}; }",
                expected:
                    @"() => {
    return {field: 1, otherField: 2};
}");
        }

        [Fact]
        public void LambdaSingleArgExprNoParens()
        {
            TestExpression(
                "x => 1",
                expected:
                    @"(x) => return 1;");
        }

        [Fact]
        public void LambdaSingleArgExprNoParensEmptyObject()
        {
            TestExpression(
                "x => {}",
                expected:
                    @"(x) => {
}");
        }

        [Fact]
        public void LambdaSingleArgParensExprMultipleIdentifierFields()
        {
            TestExpression(
                "(x) => { return {field: 1, otherField: 2}; }",
                expected:
                    @"(x) => {
    return {field: 1, otherField: 2};
}");
        }

        #endregion LambdaExpression 

        #region LiteralExpression

        [Fact]
        public void LiteralUndefined()
        {
            TestExpression("undefined");
        }

        [Fact]
        public void LiteralBooleanTrue()
        {
            TestExpression("true");
        }

        [Fact]
        public void LiteralBooleanFalse()
        {
            TestExpression("false");
        }

        [Fact]
        public void LiteralNumberInt()
        {
            TestExpression("42");
        }

        [Fact]
        public void LiteralPathValue()
        {
            TestExpression("p`abc.txt`");
        }

        [Fact]
        public void LiteralString()
        {
            TestExpression(@"""This is a String""");
        }

        [Fact]
        public void LiteralStringWithCommonEscapeChars()
        {
            TestExpression(@"""CariageReturn:\r NewLine:\n Tab:\t Slash:\\ DoubleQuote:\"" """);
        }

        [Fact]
        public void LiteralStringWithSingleQuoteEscapedAndNot()
        {
            TestExpression(@"""SingleRegular:' SingleEscaped:\' """, @"""SingleRegular:' SingleEscaped:' """);
        }

        [Fact]
        public void LiteralSourcePath1()
        {
            TestExpression("p`path/to/abc.txt`");
        }

        [Fact]
        public void LiteralSourceBacktickPath()
        {
            TestExpression("p`path/to/abc.txt`", "p`path/to/abc.txt`");
        }

        [Fact]
        public void LiteralBacktickPathInterpolation1()
        {
            TestExpression("p`${x}/path/to/abc.txt`", "p`${x}/path/to/abc.txt`");
        }

        [Fact]
        public void LiteralBacktickPathInterpolation4()
        {
            TestExpression("p`path/${y}/abc.txt`", "p`path/${y}/abc.txt`");
        }

        #endregion

        #region ArrayExpression

        [Fact]
        public void ArrayMultiple()
        {
            TestExpression(
                "[1,2,3]",
                expected:
                    @"[
    1,
    2,
    3
]");
        }

        [Fact]
        public void ArraySpreadInTheMiddle()
        {
            TestExpression(
                "[1,2,3, ...x, 7, 8]",
                expected:
                    @"[
    1,
    2,
    3,
    ...(x),
    7,
    8]");
        }

        [Fact]
        public void ArraySpreadWithScalarInBetween()
        {
            TestExpression(
                "[1,2,3, ...x, 7, ...y]",
                expected:
                    @"[
    1,
    2,
    3,
    ...(x),
    7,
    ...y]");
        }

        [Fact]
        public void ArraySpreadAtTheBeginning()
        {
            TestExpression(
                "[...x, 7, ...y]",
                expected: @"[...(x), 7, ...y]");
        }

        [Fact]
        public void ArraySpreadConsecutiveAtTheEnd()
        {
            TestExpression(
                "[1,2,3, ...x, ...y, ...z]",
                expected:
                    @"[
    1,
    2,
    3,
    ...(x),
    ...y,
    ...z
]");
        }

        [Fact]
        public void ArraySpreadWithComplexExpressionAtTheBeginningAndTheEnd()
        {
            TestExpression(
                "[...(x.y || []), ...x.map(r => r)]",
                expected:
                    @"[...(x.y || []), ...(x.map((r) => return r;))]");
        }

        [Fact]
        public void ArraySpreadWithOnlySingleSpreadElement()
        {
            TestExpression("[...(x || [])]", expected: @"[...(x || [])]");
        }

        // [Fact]
        // public void ArrayEmptyTail()
        // {
        //    TestExpression("[1,2,]", "[1, 2]");
        // }

        // [Fact]
        // public void ArrayDoubleEmptyTail()
        // {
        //    TestExpression("[1,,]", "[1, undefined]");
        // }

        #endregion ArrayExpression

        #region UnaryExpression

        [Fact]
        public void PrefixOperatorNot()
        {
            TestExpression("!true");
        }

        [Fact]
        public void PrefixOperatorNotNot()
        {
            TestExpression("!!true", "!(!true)");
        }

        [Fact]
        public void PrefixOperatorChain()
        {
            TestExpression("typeof <string>typeof (typeof \"foo\")", "typeof(<string>typeof(typeof(\"foo\")))");
        }

        [Fact]
        public void PrefixOperatorMinus()
        {
            TestExpression("-1");
        }

        [Fact]
        public void SpreadOperator()
        {
            TestExpression("[...[], ...a]", "[...([]), ...(a)]");
        }

        [Theory]
        [InlineData("~1")]
        [InlineData("~(~1)")]
        public void BitWiseNot(string expr)
        {
            TestExpression(expr);
        }

        [Fact]
        public void BitWiseNotNot()
        {
            TestExpression("~~1", "~(~1)");
        }

        #endregion UnaryExpression

        #region BinaryOperators

        [Fact]
        public void BinOpAnd()
        {
            TestExpression("true && true", expected: "true && true");
        }

        [Fact]
        public void BinOpOr()
        {
            TestExpression("false || false", expected: "false || false");
        }

        [Fact]
        public void BinOpAdd()
        {
            TestExpression("1 + 1");
        }

        [Fact]
        public void BinOpMin()
        {
            TestExpression("1 - 1");
        }

        [Fact]
        public void BinOpMul()
        {
            TestExpression("1 * 1");
        }

        [Fact]
        public void BinOpEquals()
        {
            TestExpression("1 === 1");
        }

        [Fact]
        public void BinOpNotEquals()
        {
            TestExpression("1 !== 1");
        }

        // [Fact]
        // public void BinOpLessThan1()
        // {
        //    TestExpression("1 < 1");
        // }

        // [Fact]
        // public void BinOpLessThan2()
        // {
        //    TestExpression("x < 42");
        // }

        [Fact]
        public void BinOpGreaterThan()
        {
            TestExpression("1 > 1");
        }

        // [Fact]
        // public void BinOpLessThanOrEqual()
        // {
        //    TestExpression("1 <= 1");
        // }

        [Fact]
        public void BinOpGreaterThanOrEqual()
        {
            TestExpression("1 >= 1");
        }

        [Fact]
        public void BinaryParenthesised()
        {
            TestExpression("false || (true && false)", expected: "false || (true && false)");
        }

        [Theory]
        [InlineData("1 | 2")]
        [InlineData("(1 | 2) | 4")]
        [InlineData("1 | (2 | 4)")]
        [InlineData("1 & 2")]
        [InlineData("(1 & 2) & 4")]
        [InlineData("1 & (2 & 4)")]
        [InlineData("1 ^ 2")]
        [InlineData("(1 ^ 2) ^ 4")]
        [InlineData("1 ^ (2 ^ 4)")]
        public void BitWise(string expr)
        {
            TestExpression(expr);
        }
        
        #endregion BinaryExpression

        #region ObjectExpression

        [Fact]
        public void TypicalObjectExpression()
        {
            TestExpression("{field1: 1, field2: 2}");
        }

        [Fact]
        public void ObjectExpressionWithSymbolStringKey()
        {
            TestExpression("{\"field1\": 1, \"field2\": 2}", "{field1: 1, field2: 2}");
        }

        [Fact]
        public void ObjectExpressionWithNonSymbolStringKey()
        {
            TestExpression("{\"field1.A.B\": 1, \"field2.B.C-1.0\": 2}");
        }

        #endregion ObjectExpression

        #region Other Expressions

        [Fact]
        public void TernaryOperator()
        {
            TestExpression("true ? 1 : 2");
        }

        [Fact]
        public void RefinementIndex()
        {
            TestExpression("a[1]");
        }

        [Fact]
        public void InvocationEmpty()
        {
            TestExpression("fun()");
        }

        [Fact]
        public void InvocationSingle()
        {
            TestExpression("fun(1)");
        }

        [Fact]
        public void InvocationSingleWithTypeArgument()
        {
            TestExpression("fun<T, S>(1)");
        }

        [Fact]
        public void InvocationMultiple()
        {
            TestExpression(
                "fun(1, 2, 3)",
                expected:
                    @"fun(
    1,
    2,
    3
)");
        }

        [Fact]
        public void InvocationMultiplePathInterpolations()
        {
            TestExpression("fun(p`a/b.txt`, p`c/d.txt`)", "fun(p`a/b.txt`, p`c/d.txt`)");
        }

        [Theory]
        [InlineData("<number>1")]
        [InlineData("<string>\"1\"")]
        [InlineData("<number>(1 + 2)")]
        [InlineData("<Object>{}")]
        [InlineData("<Object>{a: <number>123, b: <string>\"1\"}")]
        public void TypeCast(string expression)
        {
            TestExpression(expression);
        }

        [Theory]
        [InlineData("1 as number")]
        [InlineData("\"1\" as string")]
        [InlineData("(1 + 2) as number")]
        [InlineData("{} as Object")]
        [InlineData("<Object>{a: 123 as number, b: \"1\" as string}")]
        public void AsCast(string expression)
        {
            TestExpression(expression);
        }

        [Fact]
        public void TypeCastPrecedence1()
        {
            TestExpression("<number>(1 + 2) + 3");
        }

        [Fact]
        public void TypeCastPrecedence2()
        {
            TestExpression("<number>1 + 2 + 3", "(<number>1 + 2) + 3");
        }

        #endregion Other Expressions

        private void TestExpression(string source, string expected = null)
        {
            expected = expected ?? source;

            source = "const x = [{y: 1}]; const a = " + source + ";";
            expected = "const x = [{y: 1}]; const a = " + expected + ";";

            PrettyPrint(source, expected);
        }
    }
}
