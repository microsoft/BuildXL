// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.Utils;
using Xunit;

namespace TypeScript.Net.UnitTests.Parsing
{
    public class ModuleImporting
    {
        [Fact]
        public void ImportStarAsImport()
        {
            string code =
@"import * as X from 'blah.dsc'";

            var node = ParsingHelper.ParseFirstStatementFrom<IImportDeclaration>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void MultipleImportsInOneFile()
        {
            string code =
@"import * as X from 'blah.dsc';
import * from ""Common"";";

            var node = ParsingHelper.ParseSourceFile(code);

            // var b = node.statements[0].NeedToAddSemicolonAfter();
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void ImportStarImport()
        {
            string code =
@"import * from 'blah.dsc'";

            var node = ParsingHelper.ParseFirstStatementFrom<IImportDeclaration>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void ImportStarAsImportWithDoubleQuotes()
        {
            string code = @"import * as X from ""blah.dsc""";

            var node = ParsingHelper.ParseFirstStatementFrom<IImportDeclaration>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void ImportCurly()
        {
            string code =
@"import {X} from 'blah.dsc'";

            var node = ParsingHelper.ParseFirstStatementFrom<IImportDeclaration>(code);

            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void ImportCurlyList()
        {
            string code =
@"import {X, Y, Z} from 'blah.dsc'";

            var node = ParsingHelper.ParseFirstStatementFrom<IImportDeclaration>(code);

            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void ImportCurlyListAs()
        {
            string code =
@"import {X, Y as Y1, Z as Z1} from 'blah.dsc'";

            var node = ParsingHelper.ParseFirstStatementFrom<IImportDeclaration>(code);

            Assert.Equal(code, node.GetFormattedText());
        }
    }
}
