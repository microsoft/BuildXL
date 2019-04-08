// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.Utils;
using Xunit;

namespace TypeScript.Net.UnitTests.Parsing
{
    public class EnumDeclarations
    {
        [Fact]
        public void ParseEnumDeclaration()
        {
            string code =
@"export enum Foo2 {
    Case1 = 1,
    Case2 = 3,
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IEnumDeclaration>(code);
            Assert.Equal(2, node.Members.Length);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void ParseConstEnumDeclaration()
        {
            string code =
@"export const enum Foo {
    Case1,
    Case2 = 3,
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IEnumDeclaration>(code);
            Assert.Equal(2, node.Members.Length);
            Assert.Equal(code, node.GetFormattedText());
        }
    }
}
