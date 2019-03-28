// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.DScript;
using TypeScript.Net.Parsing;
using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using Xunit;

namespace TypeScript.Net.UnitTests.Parsing.ErrorRecovery
{
    public class PrettyPrinterTests
    {
        [Fact]
        public void MissingStatementShouldNotLeadToCrash()
        {
            // This was a Bug #868105

            // The following declaration is broken and will ended up with a missing node instance.
            // This should never happen in a real world when errors from the parser/checker are checked.
            // But currently office build can turn this validation off, that can cause the issue at the runtime.
            string code =
"@foo;";

            var parser = new Parser();

            ISourceFile node = parser.ParseSourceFile(
                "fakeFile.dsc",
                code,
                ScriptTarget.Es2015,
                syntaxCursor: null,
                setParentNodes: true,
                parsingOptions: ParsingOptions.DefaultParsingOptions);

            var statement = node.Statements[0];

            // This call caused an unhandled exception
            string text = statement.GetText();
            Assert.NotNull(text);
        }
    }
}
