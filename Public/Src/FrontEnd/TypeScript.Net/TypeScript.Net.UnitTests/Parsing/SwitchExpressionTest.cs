// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.Utils;
using Xunit;

namespace TypeScript.Net.UnitTests.Parsing
{
    public class SwitchExpression
    {
        [Fact]
        public void Basic()
        {
            string code = 
@"{
    let x = '10' switch {
        '1': 1,
        '10': 10,
    };
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void DefaultClause()
        {
            string code =
                @"{
    let x = '10' switch {
        '1': 1,
        '10': 10,
        default: 100,
    };
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }
    }
}
