// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.Utils;
using Xunit;

namespace TypeScript.Net.UnitTests.Parsing
{
    public class SwitchStatement
    {
        [Fact]
        public void SwitchWithSingleStatements()
        {
            string code =
@"{
    switch (x) {
        case ""A"":
            return 1;
        case ""B"":
            return 2;
        default:
            return 4;
    };
}";

            // Current printing is far from perfect! Should be enhanced, if needed.
            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void SwitchWithMultipleStatements()
        {
            string code =
@"{
    switch (x) {
        case ""A"":
            {
                log(1);
                return 1;
            }
        case ""B"":
            log(2);
            return 2;
        default:
            return 3;
    };
}";

            // Current printing is far from perfect! Should be enhanced, if needed.
            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void SwitchWithFallthroughStatements()
        {
            string code =
@"{
    switch (x) {
        case ""A"":
            return 1;
        case ""B"":
        case ""C"":
        case ""D"":
            return 2;
        default:
            return 4;
    };
}";

            // Current printing is far from perfect! Should be enhanced, if needed.
            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void SwitchWithBreakStatements()
        {
            string code =
@"{
    switch (x) {
        case ""A"":
            log(1);
            break;
        case ""B"":
            log(2);
            break;
        default:
            log(3);
            break;
    };
}";

            // Current printing is far from perfect! Should be enhanced, if needed.
            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }
    }
}
