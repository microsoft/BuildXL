// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.Utils;
using Xunit;

namespace TypeScript.Net.UnitTests.Parsing
{
    public class ReadonlyModifiers
    {
        [Fact]
        public void InterfaceWithReadonlyModifier()
        {
            string code =
@"interface Foo {
    readonly x: number;
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IInterfaceDeclaration>(code, roundTripTesting: false);
            var member = node.Members[0];
            bool isReadonly = (member.Modifiers.Flags & NodeFlags.Readonly) != 0;
            Assert.True(isReadonly);
        }

        [Fact]
        public void ReadonlyIndexer()
        {
            string code =
@"interface Foo {
    readonly [x: number]: number;
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IInterfaceDeclaration>(code, roundTripTesting: false);
            var member = node.Members[0];
            bool isReadonly = (member.Modifiers.Flags & NodeFlags.Readonly) != 0;
            Assert.True(isReadonly);
        }
    }
}
