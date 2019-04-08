// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.Utils;
using Xunit;

namespace TypeScript.Net.UnitTests.Parsing
{
    public class NamespaceDeclarations
    {
        [Fact]
        public void NamespaceWithConstBinding()
        {
            string code =
@"namespace X {
    const x: number = 42;
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IModuleDeclaration>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void NamespaceWithDottedName()
        {
            string code =
@"namespace X.Y {
    const x: number = 42;
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IModuleDeclaration>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void NestedDottedNamespacesAreExported()
        {
            // Only Y should be exported
            string code =
@"namespace X.Y {
    const x: number = 42;
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IModuleDeclaration>(code, roundTripTesting: false);
            Assert.True(!IsExport(node));
            var nestedModule = node.Body.AsModuleDeclaration();

            Assert.True(IsExport(nestedModule));
        }

        private bool IsExport(INode node)
        {
            return (node.Flags & NodeFlags.Export) == NodeFlags.Export;
        }
    }
}
