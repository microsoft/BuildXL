// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public class AmbientStringBuilderTests : DsTest
    {
        public AmbientStringBuilderTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void EvaluateStringBuilder()
        {
            string code = @"
function useStringBuilder(): string {
  let sb = StringBuilder.create();

  return sb.append('foo bar')
    .replace('foo', 'bar')
    .appendLine(' x')
    .toString();
}

export const r = useStringBuilder();
";

            var result = EvaluateExpressionWithNoErrors(code, "r");
            Assert.Equal("bar bar x\n", result);
        }

        [Theory]
        [InlineData("X", 0, "")]
        [InlineData("", 8, "")]
        [InlineData("X", 8, "XXXXXXXX")]
        [InlineData("XX", 4, "XXXXXXXX")]
        public void AppendRepeat(string value, string count, string expected)
        {
            string code = $@"
function useStringBuilder(): string {{
  return StringBuilder
    .create()
    .appendRepeat('{value}', {count})
    .toString();
}}

export const r = useStringBuilder();
";

            var result = EvaluateExpressionWithNoErrors(code, "r");
            Assert.Equal(expected, result);
        }

        [Fact]
        public void MutableSetIsProhibitedOnTopLevelWithImplicitType()
        {
            string code = @"const x = StringBuilder.create();";
            EvaluateWithDiagnosticId(code, LogEventId.NoMutableDeclarationsAtTopLevel);
        }
    }
}
