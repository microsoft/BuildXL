// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Values;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public class AmbientNumberTests : DsTest
    {
        public AmbientNumberTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Theory]
        [InlineData("1", 1)]
        [InlineData("23", 23)]
        [InlineData("-42", -42)]
        [InlineData("42.0", 42)]
        [InlineData(" 42 ", 42)]
        [InlineData("42,123.0", 42123)]
        [InlineData("12E03", 12000)]
        [InlineData("20356-", -20356)]
        public void TestParseSuccesfulInt(string str, int expectedResult)
        {
            var spec = $@"export const r = Number.parseInt(""{str}"");";

            var result = Build()
                .AddSpec("spec.dsc", spec)
                .RootSpec("spec.dsc")
                .EvaluateExpressionWithNoErrors<int>("r");

            Assert.Equal(expectedResult, result);
        }

        [Theory]
        [InlineData("0x80E")]
        [InlineData("80E")]
        [InlineData("3,000,000,000")]
        [InlineData("-3,000,000,000")]
        [InlineData("whatever")]
        [InlineData("$21")]
        public void TestParseUnsuccesfulIntReturnsUndefined(string str)
        {
            var spec = $@"export const r = Number.parseInt(""{str}"");";

            var result = Build()
                .AddSpec("spec.dsc", spec)
                .RootSpec("spec.dsc")
                .EvaluateExpressionWithNoErrors("r");

            Assert.Equal(UndefinedValue.Instance, result);
        }
    }
}
