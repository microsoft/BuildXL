// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.Utils;
using Xunit;

namespace TypeScript.Net.UnitTests.Parsing
{
    public class InterfaceDeclarations
    {
        [Fact]
        public void InterfaceWithOneMember()
        {
            string code =
@"interface X {
    x: number;
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IInterfaceDeclaration>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void InterfaceInheritance()
        {
            string code =
@"{
    interface X {
        x: number;
    }
    interface Y extends X {
        y: number;
    }
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void InterfaceDeclarationWithTypeLiteral()
        {
            string code =
@"interface X {
    x: ""Foo"";
    y: number;
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IInterfaceDeclaration>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void InterfaceUnionLiteralType()
        {
            string code =
@"interface X {
    x: ""Foo"" | ""Foo2"";
    y: number;
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IInterfaceDeclaration>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void InterfaceWithNestedDeclaration()
        {
            string code =
@"interface X {
    x: {f1: string, f2: {f3: boolean}};
    y: number;
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IInterfaceDeclaration>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void InterfaceWithNestedInterfaceMembers()
        {
            string code =
@"interface X {
    x: number;
}
interface Y {
    z: number;
    b: boolean;
    x?: X;
}";

            var node = ParsingHelper.ParseSourceFile(code);
            Console.WriteLine(node.GetFormattedText());
            Assert.Equal(code, node.GetFormattedText());
        }
    }
}
