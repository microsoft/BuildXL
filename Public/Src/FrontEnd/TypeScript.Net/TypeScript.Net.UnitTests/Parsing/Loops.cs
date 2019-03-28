// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.Utils;
using Xunit;

namespace TypeScript.Net.UnitTests.Parsing
{
    public class Loops
    {
        [Fact]
        public void ForOfStatement()
        {
            string code =
@"for (const x of [1, 2]) {
    console.writeLine(x);
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IForOfStatement>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void ForInStatement()
        {
            string code =
@"for (const x in [1, 2]) {
    console.writeLine(x);
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IForInStatement>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void ForStatement()
        {
            string code =
@"for (let x = 0; i < 10; i++) {
    let y = --x;
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IForStatement>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        // TODO: add while, do-while loops
    }
}
