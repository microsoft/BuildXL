// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class TestToString : DsTest
    {
        public TestToString(ITestOutputHelper output) : base(output) { }

        [Theory]
        [InlineData("42", "42")]
        [InlineData("42 + 1", "43")]
        [InlineData("[1, 2, 3]", @"[1, 2, 3]")]
        [InlineData("{x: 42}", "{x: 42}")]
        [InlineData("p`c:/foo.txt`", "p`c:/foo.txt`")]
        [InlineData("d`c:/sources`", "d`c:/sources`")]
        [InlineData("f`c:/sources/a.cs`", "f`c:/sources/a.cs`")]
        [InlineData("a`a.cs`", "a.cs")] // This is expected behavior, that path atom doesn't have back ticks around!
        [InlineData("r`a.cs`", "r`a.cs`")]
        public void TestToStringInvocation(string expression, string expected)
        {
            if (OperatingSystemHelper.IsUnixOS)
            {
                expression = expression.Replace("`c:", "`");
                expected = expected.Replace("`c:", "`");
            }

            const string CodeTemplate = "const v = {0}; export const result = v.toString();";

            var code = string.Format(CodeTemplate, expression);
            var result = EvaluateExpressionWithNoErrors(code, "result");

            // Need to use case insensitive comparison, because drive letter could be upper-cased or
            // lower cased depending on the machine.
            XAssert.EqualIgnoreWhiteSpace(expected, (string)result, ignoreCase: true);
        }

        [Fact]
        public void TestToStringOnEnum()
        {
            string code = @"
const enum Foo {
  value = 42,
}

export const r = Foo.value.toString();
";

            var result = EvaluateExpressionWithNoErrors(code, "r");

            // Note that this is different from TypeScript! In typescript toString returns numeric representation
            // of the value, but not a name!
            Assert.Equal("value", result);
        }
    }
}
