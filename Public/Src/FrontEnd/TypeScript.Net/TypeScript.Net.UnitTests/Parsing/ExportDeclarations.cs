// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.Utils;
using Xunit;

namespace TypeScript.Net.UnitTests.Parsing
{
    public class ExportDeclarations
    {
        [Fact]
        public void ParseEnumDeclaration()
        {
            string code =
@"export * from ""MyPackage""";

            var node = ParsingHelper.ParseFirstStatementFrom<IExportDeclaration>(code);

            Assert.Equal(code, node.GetFormattedText());

            Assert.Null(node.ExportClause);
            Assert.Equal(SyntaxKind.StringLiteral, node.ModuleSpecifier.Kind);
            Assert.Equal("MyPackage", ((ILiteralExpression)node.ModuleSpecifier).Text);
        }

        [Fact]
        public void ExportCurly()
        {
            string code =
@"export {X}";

            var node = ParsingHelper.ParseFirstStatementFrom<IExportDeclaration>(code);

            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void ExportCurlyList()
        {
            string code =
@"export {X, Y, Z}";

            var node = ParsingHelper.ParseFirstStatementFrom<IExportDeclaration>(code);

            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void ExportCurlyListAs()
        {
            string code =
@"export {X, Y as Y1, Z as Z2}";

            var node = ParsingHelper.ParseFirstStatementFrom<IExportDeclaration>(code);

            Assert.Equal(code, node.GetFormattedText());
        }
    }
}
