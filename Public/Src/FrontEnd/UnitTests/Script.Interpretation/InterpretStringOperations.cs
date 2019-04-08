// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class InterpretStringOperations : DsTest
    {
        public InterpretStringOperations(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void EvaluateStringConcatenationOnBackTickedString()
        {
            // Bug #441703
            string spec =
@"const language = ""C#"";
export const r = `foo` + language;
            ";
            var result = EvaluateExpressionWithNoErrors(spec, "r");
            Assert.Equal("fooC#", result);
        }

        [Fact]
        public void EvaluateStringConcatenation()
        {
            string spec =
@"namespace M
{
    const y = ""4"";
    export const x = y + ""2"";
}";
            var result = EvaluateExpressionWithNoErrors(spec, "M.x");
            Assert.Equal("42", result);
        }

        [Fact]
        public void EvaluateNumberToString()
        {
            string spec =
@"namespace M
{
    const y = 4;
    export const x = y.toString();
}";
            var result = EvaluateExpressionWithNoErrors(spec, "M.x");
            Assert.Equal("4", result);
        }

        [Fact]
        public void EvaluateNumberToString_16()
        {
            string spec =
@"const y = 0x10000;
export const x = y.toString(16);";
            var result = EvaluateExpressionWithNoErrors(spec, "x");
            Assert.Equal("10000", result);
        }

        [Fact]
        public void EvaluateParseNumber_16()
        {
            string spec =
@"
export const x1 = Number.parseInt('FF', 16);
export const x2 = Number.parseInt('0xFF', 16);";
            var result = EvaluateExpressionsWithNoErrors(spec, "x1", "x2");
            Assert.Equal(255, result["x1"]);
            Assert.Equal(255, result["x2"]);
        }

        [Fact]
        public void EvaluateParseNumber_InvalidRadix()
        {
            string spec =
                @"
export const x1 = Number.parseInt('FF', 11);";

            EvaluateWithDiagnosticId(spec, LogEventId.InvalidRadix);
        }

        [Fact]
        public void EvaluateInvalidRadixOnToString()
        {
            string spec =
@"const y = 0x10000;
export const x = y.toString(17);";
            EvaluateWithDiagnosticId(spec, LogEventId.InvalidRadix);
        }

        [Fact]
        public void EvaluateStringLength()
        {
            // Note, that in DScript length is a function, not a property on the string!
            string spec =
@"namespace M
{
    const y = ""41"";
    export const x = (y + ""2"").length;
}";
            var result = EvaluateExpressionWithNoErrors(spec, "M.x");
            Assert.Equal(3, result);
        }

        [Fact]
        public void TestString()
        {
            var result = EvaluateSpec(@"
namespace M 
{
    const x : string = ""abc"".concat(""def"");
    export const result = x.endsWith(""ef"");
}
", new[] { "M.result" });

            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal(true, result.Values[0]);
        }

        [Fact]
        public void TestStringUndefinedOrEmpty()
        {
            var result = EvaluateSpec(@"
namespace M 
{
    export const b1 : boolean = String.isUndefinedOrEmpty(undefined);
    export const b2 : boolean = String.isUndefinedOrEmpty("""");
    export const b3 : boolean = String.isUndefinedOrEmpty(""hello"");
}
", new[] { "M.b1", "M.b2", "M.b3" });

            result.ExpectNoError();
            result.ExpectValues(count: 3);
            Assert.Equal(true, result.Values[0]);
            Assert.Equal(true, result.Values[1]);
            Assert.Equal(false, result.Values[2]);
        }

        [Fact]
        public void TestStringUndefinedOrWhitespace()
        {
            var result = EvaluateExpressionsWithNoErrors(@"
namespace M 
{
    const s: string = undefined;
    export const b1 : boolean = String.isUndefinedOrWhitespace(s);
    export const b2 : boolean = String.isUndefinedOrWhitespace("""");
    export const b3 : boolean = String.isUndefinedOrWhitespace(""hello"");
    export const b4 : boolean = String.isUndefinedOrWhitespace("" "");
}
", "M.b1", "M.b2", "M.b3", "M.b4");

            Assert.Equal(true, result["M.b1"]);
            Assert.Equal(true, result["M.b2"]);
            Assert.Equal(false, result["M.b3"]);
            Assert.Equal(true, result["M.b4"]);
        }
    }
}
