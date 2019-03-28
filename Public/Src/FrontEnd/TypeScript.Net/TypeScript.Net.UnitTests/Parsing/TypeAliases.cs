// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.Utils;
using Xunit;

namespace TypeScript.Net.UnitTests.Parsing
{
    public class TypeAliases
    {
        [Fact]
        public void SimpleAlias()
        {
            string code = @"export type Foo = string";

            var node = ParsingHelper.ParseFirstStatementFrom<ITypeAliasDeclaration>(code);

            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void SimpleUnionTypeAlias()
        {
            string code = @"export type Foo = string | number";

            var node = ParsingHelper.ParseFirstStatementFrom<ITypeAliasDeclaration>(code);

            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void UnionTypeWithTypeLiteral()
        {
            string code =
@"export type Foo = string | number | {x: boolean}";

            var node = ParsingHelper.ParseFirstStatementFrom<ITypeAliasDeclaration>(code);

            Console.WriteLine(node.GetFormattedText());
        }
    }
}
