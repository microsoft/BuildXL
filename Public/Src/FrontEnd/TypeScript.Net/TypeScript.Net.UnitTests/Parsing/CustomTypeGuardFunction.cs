// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.Utils;
using Xunit;
using Xunit.Abstractions;

namespace TypeScript.Net.UnitTests.Parsing
{
    public class CustomTypeGuardFunction
    {
        private readonly ITestOutputHelper m_output;

        public CustomTypeGuardFunction(ITestOutputHelper output)
        {
            m_output = output;
        }

        [Fact]
        public void TypePredicateCouldBeParsed()
        {
            string code =
@"{
    interface X {
        kind: ""X"",
        x: number,
    }
    interface Y {
        kind: ""Y"",
        y: number
    }
    
    type X_Or_Y = X | Y;

    let x: X_Or_Y = {kind: ""X"", x: 42}; // type checked

    function isX(x: X_Or_Y): x is X {
        return x.kind === ""X"";
    }
    
    if (isX(x)) {
        let xValue = x.x;
    }
}";
            var block = ParsingHelper.ParseFirstStatementFrom<IBlock>(code, roundTripTesting: false);
            m_output.WriteLine($"Block: \r\n{block.GetFormattedText()}");
        }
    }
}
