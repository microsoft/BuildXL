// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.Utils;
using Xunit;

namespace TypeScript.Net.UnitTests.Parsing
{
    public class PolymorphicNameProperties
    {
        [Fact]
        public void DifferentSyntaxNodesShouldReturnProperResultFromNameProperty()
        {
            string code =
@"interface Foo {}

enum Bar {}

namespace Baz {}
";

            var node = ParsingHelper.ParseSourceFile(code);

            // Original impelmentation of the AST didn't support polymorphic usage of Name properties.
            // This test proofs that now it is possible.
            var declarations = node.Statements.Elements.OfType<IDeclaration>().ToList();

            var interfaceDeclaration = declarations[0] as InterfaceDeclaration;
            Assert.Equal("Foo", declarations[0].Name.Text);
            Assert.Equal("Bar", declarations[1].Name.Text);
            Assert.Equal("Baz", declarations[2].Name.Text);
        }
    }
}
