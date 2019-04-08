// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.Utils;
using Xunit;

namespace TypeScript.Net.UnitTests.Parsing
{
    public class ObjectLiterals
    {
        [Fact]
        public void ParseObjectLiteralWithArrowFunctionDeclarations()
        {
            string code =
@"{
    interface Foo {
        x: number;
        func: () => string;
        func2: (x: number) => string;
        func3: (x: number, y: string) => {x: number};
    }
    let foo: Foo = {
        x: 42,
        func: () => 'literal',
        func2: x => x.toString(),
        func3: (x, y) => {
            return {x: x};
        },
    };
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void ParseObjectLiteralWithFunction()
        {
            string code =
@"{
    interface Foo {
        x: number;
        func: () => string;
        func2: (x: number) => string;
        func3: (x: number, y: string) => {x: number};
    }
    let foo: Foo = {
        x: 42,
        func: function() {
            return 'foo';
        },
        func2: function(x: number) {
            return x.toString();
        },
        func3: function(x, y) {
            return {x: x};
        },
    };
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Console.WriteLine(node.GetFormattedText());
            Assert.Equal(code, node.GetFormattedText());
        }
    }
}
