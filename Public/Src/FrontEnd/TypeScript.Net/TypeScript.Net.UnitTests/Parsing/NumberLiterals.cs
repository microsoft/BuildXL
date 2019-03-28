// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.Utils;
using Xunit;

namespace TypeScript.Net.UnitTests.Parsing
{
    public class NumberLiterals
    {
        [Theory]
        [InlineData("1", "1")]
        [InlineData(".1", ".1")]
        [InlineData("1.", "1")] // This is a special case, because 1. just parsed to 1
        [InlineData("1.5", "1.5")]
        [InlineData("0x111111111111111111111111111", "21634570243895115118877068038417")]
        public void TestNumberLiterals(string s, string expectedLiteral)
        {
            string code = $"let x = {s};";

            var node = ParsingHelper.ParseFirstStatementFrom<IVariableStatement>(code, roundTripTesting: false);
            var literal = node.DeclarationList.Declarations[0].Initializer.Cast<ILiteralExpression>();

            Assert.Equal(expectedLiteral, literal.Text);
        }
    }
}
