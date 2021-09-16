// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;
using BuildXL.Utilities;

namespace Test.DScript.Ast.Parsing
{
    [Trait("Category", "Parsing")]
    public class TestPathInterpolation : DsTest
    {
        public TestPathInterpolation(ITestOutputHelper output)
            : base(output)
        {}

        [Fact]
        public void InterpolateExpressionWithString()
        {
            TestExpression("p`${x}/path/to/abc.txt`", "p`${x}/path/to/abc.txt`");
        }

        [Fact(Skip = "Implement when .. would be forbidden")]
        public void ShouldFailOnDotDotInPathInterpolation()
        {
            TestExpression("p`${x}/../path/to/abc.txt`", "p`${x}/../path/to/abc.txt`");
        }

        [Fact]
        public void InterpolateTwoExpression()
        {
            TestExpression("p`${x}/${y}`", "p`${x}/${y}`");
        }

        [Fact]
        public void InterpolateExpressionWithMissingSlashShouldFail()
        {
            WithError("p`${x}path/to/abc.txt`", LogEventId.InvalidPathInterpolationExpression);
        }

        [Fact]
        public void InterpolateExpressionWithMissingSlashAtTheBeginningShouldFail()
        {
            // should be 'foo/${x}
            WithError("p`foo${x}`", LogEventId.InvalidPathInterpolationExpression);
        }

        [Fact]
        public void InterpolateTwoExpressionWithoutSlashShouldFail()
        {
            WithError("p`${x}${x}`", LogEventId.InvalidPathInterpolationExpression);
        }

        [Fact]
        public void InterpolateTwoExpressionWithoutTailingSlashShouldFail()
        {
            WithError("p`${x}/foo${x}`", LogEventId.InvalidPathInterpolationExpression);
        }

        [Fact]
        public void InterpolateSingleExpression()
        {
            TestExpression("p`${x}`", "p`${x}`");
        }

        [Fact]
        public void LiteralBacktickPathInterpolation2()
        {
            TestExpression("p`${x}/path/${x}/abc.txt`", "p`${x}/path/${x}/abc.txt`");
        }

        [Fact]
        public void LiteralBacktickPathInterpolation3()
        {
            TestExpression(
                "p`${x}/path/${x}/additional/${fun()}`",
                "p`${x}/path/${x}/additional/${fun()}`");
        }

        [Fact]
        public void LiteralBacktickPathInterpolation4()
        {
            TestExpression("p`path/${x}/abc.txt`", "p`path/${x}/abc.txt`");
        }

        [Fact]
        public void InterpolatedPathWithStringVarAsHead()
        {
            var absolutePath = OperatingSystemHelper.IsUnixOS ? "/foo" : "c:/foo";

            string code = 
$@"const pathAsString : string = '{absolutePath}';
const x = p`${{pathAsString}}`;
export const r = `x is ${{x}}`;";

            var result = EvaluateExpressionWithNoErrors(code, "r");

            Assert.Equal($"x is p`{absolutePath}`", (string)result, ignoreCase: true);
        }

        private void WithError(string expression, LogEventId eventId)
        {
            ParseWithDiagnosticId($"const x = {expression};", eventId);
        }

        private void TestExpression(string source, string expected = null)
        {
            expected = expected ?? source;

            source = "const x = 42; const y = {name: 42}; function fun() {return 42;} const a = " + source + ";";
            expected = "const x = 42; const y = {name: 42}; function fun() {return 42;} const a = " + expected + ";";

            PrettyPrint(source, expected);
        }
    }
}
