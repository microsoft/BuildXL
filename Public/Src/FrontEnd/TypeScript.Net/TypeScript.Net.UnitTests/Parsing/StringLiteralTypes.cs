// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.Utils;
using Xunit;

namespace TypeScript.Net.UnitTests.Parsing
{
    public class StringLiteralTypes
    {
        [Fact]
        public void Simple()
        {
            string code = @"export type X = ""simple""";

            var node = ParsingHelper.ParseFirstStatementFrom<ITypeAliasDeclaration>(code);

            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void UnionWithTwoQuotes()
        {
            string code = @"export type X = ""simple"" | 'notSimple'";

            var node = ParsingHelper.ParseFirstStatementFrom<ITypeAliasDeclaration>(code);

            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void UnionWithTwoQuotesInQuotes()
        {
            string code = @"export type X = ""'"" | '""'";

            var node = ParsingHelper.ParseFirstStatementFrom<ITypeAliasDeclaration>(code);

            Assert.Equal(code, node.GetFormattedText());
        }
    }
}
