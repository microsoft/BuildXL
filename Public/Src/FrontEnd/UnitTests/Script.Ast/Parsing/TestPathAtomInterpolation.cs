// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Parsing
{
    /// <summary>
    /// Tests for path atom interpolation (only applicable to the new parser).
    /// </summary>
    [Trait("Category", "Parsing")]
    public class TestPathAtomInterpolation : DsTest
    {
        public TestPathAtomInterpolation(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void InterpolateSinglePathAtomExpression()
        {
            TestExpression("a`${x}`", "PathAtom.interpolate(x)");
        }

        [Fact]
        public void LiteralPathAtomInterpolation1()
        {
            TestExpression("a`${x}.txt`", "PathAtom.interpolate(x, a`.txt`)");
        }

        [Fact]
        public void LiteralPathAtomInterpolation2()
        {
            TestExpression("a`${x}${x}`", "PathAtom.interpolate(x, x)");
        }

        private void TestExpression(string source, string expected = null)
        {
            expected = expected ?? source;

            source = "const x = 42; const y = " + source + ";";
            expected = "const x = 42; const y = " + expected + ";";

            PrettyPrint(source, expected);
        }
    }
}
