// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.Utils;
using Xunit;

namespace TypeScript.Net.UnitTests.Parsing
{
    public class Classes
    {
        [Fact]
        public void ClassDeclarationWithOneMethod()
        {
            string code =
@"class Foo {
    public foo(): any {
        return 42;
    }
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IClassDeclaration>(code, roundTripTesting: false);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void ClassDeclarationWithConstructor()
        {
            string code =
@"class Student {
    fullName: string;
    constructor(public firstName, public middleInitial, public lastName) {
        this.fullName = firstName + "" "" + middleInitial + "" "" + lastName;
    }
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IClassDeclaration>(code, roundTripTesting: false);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void ClassDeclarationWithBaseClassAndSuperCall()
        {
            string code =
@"class Animal {
    name: string;
    constructor(theName: string) {
        this.name = theName;
    }
    move(distanceInMeters: number) {
    }
}
class Snake extends Animal {
    constructor(name: string) {
        super(name);
    }
    move(distanceInMeters) {
        super.move(distanceInMeters);
    }
}";

            var node = ParsingHelper.ParseSourceFile(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void SimpleClassExpression()
        {
            string code =
@"class Foo {
    constructor(public x: number) {
    }
}
let f = new Foo(42);";

            var node = ParsingHelper.ParseSourceFile(code);
            Assert.Equal(code, node.GetFormattedText());
        }
    }
}
