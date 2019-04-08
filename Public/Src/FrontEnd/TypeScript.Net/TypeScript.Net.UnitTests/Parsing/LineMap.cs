// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.UnitTests.Utils;
using Xunit;

namespace TypeScript.Net.UnitTests.Parsing
{
    public class LineMap
    {
        [Fact]
        public void SimpleLineMap()
        {
            string code =
@"namespace X {
    enum Foo {value = 42}
}";

            var sourceFile = ParsingHelper.ParseSourceFile(code);
            var lineMap = sourceFile.LineMap;

            Assert.Equal(new [] { 0, 15, 42 }, lineMap.Map);
        }

        [Fact]
        public void ComplexLineMapWithTrivia()
        {
            string code =
@"/*
* This is a 
* multiline comment
*/
namespace X {
    enum Foo {value = 42}
}

// This is a single line comment
namespace Y{
    const z = 53;
}

const w = 44; // This is a trailing trivia
const u = 55;
";

            var sourceFile = ParsingHelper.ParseSourceFile(code);
            var lineMap = sourceFile.LineMap;

            Assert.Equal(new[] { 0, 4, 18, 39, 43, 58, 85, 88, 90, 124, 138, 157, 160, 162, 206, 221 }, lineMap.Map);
        }
    }
}
