// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Parsing
{
    /// <summary>
    /// Tests for relative path interpolation (only applicable to the new parser).
    /// </summary>
    [Trait("Category", "Parsing")]
    public class TestRelativePathInterpolation : DsTest
    {
        public TestRelativePathInterpolation(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void InterpolateWithErrorShouldNotCrach()
        {
            var spec = @"
function foo(): number {
  const x: string = undefined;
  return x.length;
}
const x = p`${foo()}`;";

            EvaluateWithFirstError(spec);
        }

        [Fact]
        public void InterpolateSingleRelativePathExpression()
        {
            TestExpression("r`${x}`", "r`${x}`");
        }

        [Fact]
        public void LiteralRelativePathInterpolation1()
        {
            TestExpression("r`${x}/path/${y.name}/abc.txt`", "r`${x}/path/${y.name}/abc.txt`");
        }

        [Fact]
        public void LiteralRelativePathInterpolation2()
        {
            TestExpression(
                "r`${x}/path/${y.name}/additional/${x.fun()}`",
                "r`${x}/path/${y.name}/additional/${x.fun()}`");
        }

        [Fact]
        public void LiteralRelativePathInterpolation3()
        {
            TestExpression("r`path/${y.name}/abc.txt`", "r`path/${y.name}/abc.txt`");
        }

        private void TestExpression(string source, string expected = null)
        {
            expected = expected ?? source;

            source = "const y = {name: 42}; const x = 42; const a = " + source + ";";
            expected = "const y = {name: 42}; const x = 42; const a = " + expected + ";";

            PrettyPrint(source, expected);
        }
    }
}
