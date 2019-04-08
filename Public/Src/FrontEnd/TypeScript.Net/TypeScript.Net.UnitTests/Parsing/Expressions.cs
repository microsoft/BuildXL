// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.Utils;
using Xunit;

namespace TypeScript.Net.UnitTests.Parsing
{
    public class Expressions
    {
        [Theory]
        [InlineData("1 << 1111111")]
        [InlineData("1 << 33")]

        [InlineData("8 >> 2")]
        [InlineData("-8 >> 2")]
        [InlineData("-8 >>> 2")]
        public void ParseBinaryExpression(string expression)
        {
            var code = $"let x = {expression}";
            var node = ParsingHelper.ParseFirstStatementFrom<IVariableStatement>(code, roundTripTesting: false);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void OrExpression()
        {
            string code =
@"{
    let x = 42;
    let y = (x || 36).toString();
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void ConditionalExpression()
        {
            string code =
@"{
    let x = 42;
    let y = x == 42 ? 'blah' : 'foo';
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void DottedCallExpression()
        {
            string code =
@"{
    const x = Context.foo(42);
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }
    }
}
