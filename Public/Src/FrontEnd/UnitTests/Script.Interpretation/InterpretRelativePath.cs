// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public class InterpretRelativePath : DsTest
    {
        public InterpretRelativePath(ITestOutputHelper output)
            : base(output)
        {
        }

        [Theory]
        [InlineData("extension", ".txt")]
        [InlineData("name", "file.txt")]
        [InlineData("nameWithoutExtension", "file")]
        public void TestAmbientProperties(string property, string expectedResult)
        {
            const string SpecTemplate = @"
  const relative = RelativePath.create(""file.txt"");
  export const result = relative.{0}.toString();";

            string spec = string.Format(SpecTemplate, property);
            var result = EvaluateExpressionWithNoErrors(spec, "result");

            // Path could return drive letter in differnt casing. Using lowercase to avoid build breaks on different machines.
            Assert.Equal(expectedResult, result.ToString().ToLowerInvariant());
        }

        [Fact]
        public void TestSimpleRelativePathInterpolation()
        {
            string spec =
                @"
const x = RelativePath.create(""path/to/abc"");
const y = r`path/to/abc`;
const z = (x === y);
";
            var result = EvaluateExpressionWithNoErrors(spec, "z");
            Assert.Equal(true, result);
        }

        [Fact]
        public void TestRelativePathInterpolationFromString()
        {
            string spec =
                @"
const w: string = ""path/to/abc"";
const x = RelativePath.create(w);
const y = r`${w}`;
const z = (x === y);
";
            var result = EvaluateExpressionWithNoErrors(spec, "z");
            Assert.Equal(true, result);
        }

        [Fact]
        public void TestRelativePathInterpolationWithPathFragments()
        {
            string spec =
                @"
const w: string = ""d/e/f"";
const x1 = PathAtom.create(""h"");
const x2 = RelativePath.create(""j/k"");
const x3 = r`l/m`;
const y = r`a/b/c/${w}/g/${x1}/i/${x2}/${x3}`;
const z = (r`a/b/c/d/e/f/g/h/i/j/k/l/m` === y);
";
            var result = EvaluateExpressionWithNoErrors(spec, "z");
            Assert.Equal(true, result);
        }
    }
}
