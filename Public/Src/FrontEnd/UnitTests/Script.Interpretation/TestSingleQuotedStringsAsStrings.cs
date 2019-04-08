// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public class TestSingleQuotedStringsAsStrings : DsTest
    {
        public TestSingleQuotedStringsAsStrings(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void TestSingleQuotedStringAsStringContentIsCorrect()
        {
            string code = @"export const r = 'myString';";

            var result = EvaluateExpressionWithNoErrors(code, "r");
            Assert.Equal(result, "myString");
        }

        [Fact]
        public void TestSingleQuotedStringAsStringIsEqualToString()
        {
            string code = @"
const s1 = 'myString';
const s2 = ""myString"";
export const r = s1 === s2;
";

            var result = EvaluateExpressionWithNoErrors(code, "r");
            Assert.Equal(result, true);
        }

        [Fact]
        public void TestSingleQuotedStringAsStringIsAStringInExpression()
        {
            string code = @"export const r = 'my' + ""String"";";

            var result = EvaluateExpressionWithNoErrors(code, "r");
            Assert.Equal(result, "myString");
        }
    }
}
