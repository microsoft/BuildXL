// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.Utils;
using Xunit;

namespace TypeScript.Net.UnitTests.Parsing
{
    public class FunctionInvocations
    {
        [Fact]
        public void SimpleFunctionInvocation()
        {
            string code =
@"{
    export function foo() {
        return 42;
    }
    let x: number = foo();
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void InlineArrowFunctionSingleArgCallWithStatements()
        {
            string code =
@"{
    export const ten = (x => {
        return x * 2;
    }
    )(5);
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void InlineArrowFunctionCallSingleArgWithExpression()
        {
            string code =
@"{
    export const ten = (x => x * 2)(5);
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void InlineArrowFunctionCallMultipleArgsWithExpression()
        {
            string code =
@"{
    export const ten = ((x, y) => x * y)(5, 2);
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void InlineArrowFunctionCallNoArgWithExpression()
        {
            string code =
@"{
    export const ten = (() => 10)();
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }
    }
}
