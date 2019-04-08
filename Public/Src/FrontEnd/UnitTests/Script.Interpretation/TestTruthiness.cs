// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Globalization;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class TestTruthiness : DsTest
    {
        public TestTruthiness(ITestOutputHelper output) : base(output) { }
        
        [Theory]
        [InlineData("0", true)] // Difference from TypeScript! In DScript 0 is truthy, but in TypeScript - it's falsy
        [InlineData("\"\"", true)] // Difference from TypeScript! In DScript "" is truthy, but in TypeScript - is falsy
        [InlineData("false", false)]
        [InlineData("undefined", false)]

        // null and NaN are falsy in TypeScript/JavaScript but they're not supported in DScript
        [InlineData("\"false\"", true)] // string "false" is truthy
        [InlineData("1", true)] // string "false" is truthy
        [InlineData("{}", true)] // empty object is truthy
        // There is two possible falsy values in TypeScript/JavaScript: null and NaN, but both of them are not supported!
        public void Truthiness(string value, bool isTruthy)
        {
            string code = string.Format(CultureInfo.InvariantCulture, @"
function isTruthy<T>(x: T) {{
  return !!x;
}}

export const r = isTruthy({0});
", value);
            var result = EvaluateExpressionWithNoErrors(code, "r");

            Assert.Equal(isTruthy, result);
        }
    }
}
