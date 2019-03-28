// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.Utils;
using Xunit;

namespace TypeScript.Net.UnitTests.Parsing
{
    public class ArrayExpressions
    {
        [Fact]
        public void ArrayLiteralExpression()
        {
            string code =
@"{
    let x = [1, 2];
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void EmptyArrayLiteral()
        {
            string code =
@"{
    let x: string[] = [];
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void ArrayLiteralExpressionWithFunctionInvocation()
        {
            string code =
@"{
    let x = [1, 2].toString();
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void SpreadOperator()
        {
            string code =
@"{
    let x1 = [1, 2, 3];
    let x = [3, ...x1, 4];
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void SpreadOperatorWithParens()
        {
            string code =
@"{
    let x1 = [1, 2, 3];
    let x = [
        6,
        ...(x1 || []),
        4,
        5,
    ];
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void TaggedLiterals()
        {
            string code =
@"{
    let x1 = [_`string1`, _`string2`];
}";

            // Current printing is far from perfect! Should be enhanced, if needed.
            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }
    }
}
