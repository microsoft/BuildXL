// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.Utils;
using Xunit;

namespace Test.DScript.Reformatter
{
    public sealed class FormattingTests
    {
        [Fact]
        public void DecoratorOnStringLiteral()
        {
            string code =
@"{
    export type x =
    @@foo
    ""foo"" |
    @@bar
    ""bar"";
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void SmallObjectOneLine()
        {
            string code =
@"{
    export const x = {a: 1, b: ""s""};
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void SmallObjectBigStringsplits()
        {
            string code =
@"{
    export const x = {
        a: 1,
        b: ""123456789A123456789B123456789C123456789D123456789E123456789F123456789G123456789H123456789I123456789J123456789K123456789L123456789M123456789N123456789M123456789O123456789M123456789P123456789M123456789Q"",
    };
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void WhileLoopWithSimpleCondition()
        {
            // Bug #943198
            string code = @"{
    while (1) {
    }
}";
            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void DoWhileLoopWithSimpleCondition()
        {
            string code = @"{
    do {
    } while (1);
}";
            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void ManyPropertiesObjectMultiLine()
        {
            string code =
@"{
    export const x = {
        a: 1,
        b: 1,
        c: 1,
        d: 1,
        e: 1,
        f: 1,
        g: 1,
        h: 1,
    };
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void SmallArrayOneLine()
        {
            string code =
@"{
    export const x = [1, 2, 3];
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void SmallArrayBigStringsplits()
        {
            string code =
@"{
    export const x = [
        ""1"",
        ""123456789A123456789B123456789C123456789D123456789E123456789F123456789G123456789H123456789I123456789J123456789K123456789L123456789M123456789N123456789M123456789O123456789M123456789P123456789M123456789Q"",
    ];
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void ManyArrayElementsMultiLine()
        {
            string code =
@"{
    export const x = [
        1,
        2,
        3,
        4,
        5,
        6,
        7,
        8,
    ];
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void SmallArgumentsOneLine()
        {
            string code =
@"{
    export const x = f(1, 2, 3, 4, 5);
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void SmallArgumentsBigStringsplits()
        {
            string code =
@"{
    export const x = f(
        ""1"",
        ""123456789A123456789B123456789C123456789D123456789E123456789F123456789G123456789H123456789I123456789J123456789K123456789L123456789M123456789N123456789M123456789O123456789M123456789P123456789M123456789Q""
    );
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void ManyArgumentsMultiLine()
        {
            string code =
@"{
    export const x = f(
        1,
        2,
        3,
        4,
        5,
        6,
        7,
        8
    );
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }
    }
}
