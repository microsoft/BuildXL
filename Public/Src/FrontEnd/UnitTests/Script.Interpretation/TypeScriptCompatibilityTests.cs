// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.FrontEnd.Script.Values;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public class TypeScriptCompatibilityTests : DsTest
    {
        public TypeScriptCompatibilityTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Theory]
        [MemberData(nameof(TestCases))]
        public void IndexerOnPrimitiveReturnsUndefined(string code)
        {
            var result = EvaluateExpressionWithNoErrors(code, "r");
            Assert.Equal(result, UndefinedValue.Instance);
        }

        public static IEnumerable<object[]> TestCases()
        {
            yield return new[]
            {
                @"
const n = 42;
// r is of type any
export const r = n['fooBar'];
"};

            yield return new[]
            {
                @"
const n = [42];
// r is of type any
export const r = n['fooBar'];
"};

            yield return new[]
            {
                @"
const n = {x: 42};
// Function to check typechecker ability to get a correct type from the indexer
function foo(n: number) {}
// x is of type 'number'
const x = n['x'];
const tmp = foo(x); // type checker won't fail here
// but here the result is undefined
export const r = n['y'];
"};
        }
    }
}
