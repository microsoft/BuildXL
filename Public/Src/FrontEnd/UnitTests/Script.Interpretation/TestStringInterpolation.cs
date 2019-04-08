// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Utilities;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class TestStringInterpolation : DsTest
    {
        private readonly string m_absolutePath = OperatingSystemHelper.IsUnixOS ? "/foo" : "c:/foo";
        public TestStringInterpolation(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void StringInterpolationWithTheSpace()
        {
            string code = @"
const x = '1';
const y = '2';
export const r = `${x} ${y}`;
";

            var result = EvaluateExpressionWithNoErrors(code, "r");
            Assert.Equal("1 2", result);
        }

        [Fact]
        public void TaggedStringWithoutCapturedVariables()
        {
            string code =
@"export const r = `foo`;";

            var result = EvaluateExpressionWithNoErrors(code, "r");

            Assert.Equal("foo", result);
        }

        [Fact]
        public void InterpolatedStringWithCapturedPath()
        {
            string code = String.Format(
@"const x = p`{0}`;
export const r = `x is ${{x}}`;", m_absolutePath);

            var result = EvaluateExpressionWithNoErrors(code, "r");

            Assert.Equal(String.Format("x is p`{0}`", m_absolutePath), (string)result, ignoreCase: true);
        }

        [Fact]
        public void InterpolatedStringWithCapturedDirectory()
        {
            string code = String.Format(
@"const x = d`{0}`;
export const r = `x is ${{x}}`;", m_absolutePath);

            var result = EvaluateExpressionWithNoErrors(code, "r");

            Assert.Equal(String.Format("x is d`{0}`", m_absolutePath), (string)result, ignoreCase: true);
        }

        [Fact]
        public void InterpolatedStringWithCapturedStringVariable()
        {
            string code =
@"const x = ""answer is "";
export const r = `${x}42`;";

            var result = EvaluateExpressionWithNoErrors(code, "r");

            Assert.Equal("answer is 42", result);
        }

        [Fact]
        public void InterpolatedStringWithCapturedNumberVariable()
        {
            string code =
@"const x = ""answer is "";
const answer = 42;
export const r = `${x}${answer}`;";

            var result = EvaluateExpressionWithNoErrors(code, "r");

            Assert.Equal("answer is 42", result);
        }

        [Fact]
        public void InterpolatedStringWithCapturedUndefined()
        {
            string code =
@"export const r = `answer is ${undefined}`;";

            var result = EvaluateExpressionWithNoErrors(code, "r");

            Assert.Equal("answer is undefined", result);
        }

        [Fact]
        public void InterpolatedStringWithCapturedEnum()
        {
            string code =
@"
const enum Foo {
  value1 = 42,
}
const answer = Foo.value1;
export const r = `answer is ${answer}`;";

            // should be answer is 42, because enum should be converted to string representation with number
            // But currently enum.toString prints a name!
            var result = EvaluateExpressionWithNoErrors(code, "r");

            Assert.Equal("answer is value1", result);
        }

        [Fact]
        public void InterpolatedStringWithExpression()
        {
            string code =
@"
const x = ""some string"";
export const r = `answer is ${x.length + 1}`;"; // should be 12

            var result = EvaluateExpressionWithNoErrors(code, "r");

            Assert.Equal("answer is 12", result);
        }

        [Fact]
        public void InterpolatedStringWithFunctionInvocation()
        {
            string code =
@"
function answer() {return 42;}
export const r = `answer is ${answer()}`;";

            var result = EvaluateExpressionWithNoErrors(code, "r");

            Assert.Equal("answer is 42", result);
        }

        [Fact]
        public void InterpolatedStringWithObjectLiteral()
        {
            string code =
@"
function answer() {return 42;}
export const r = `answer is ${{x: answer()}}`;";

            var result = EvaluateExpressionWithNoErrors(code, "r");

            Assert.Equal("answer is {x: 42}", result);
        }
    }
}
