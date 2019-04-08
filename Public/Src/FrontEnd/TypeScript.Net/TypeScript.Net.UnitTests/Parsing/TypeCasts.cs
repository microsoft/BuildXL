// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.Utils;
using Xunit;

namespace TypeScript.Net.UnitTests.Parsing
{
    public class TypeCasts
    {
        [Fact]
        public void CastToInterface()
        {
            string code =
@"{
    interface Custom {
        x: number;
    }
    let x = <Custom>{x: 42};
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);

            Assert.Equal(code, node.GetFormattedText());
        }
    }
}
