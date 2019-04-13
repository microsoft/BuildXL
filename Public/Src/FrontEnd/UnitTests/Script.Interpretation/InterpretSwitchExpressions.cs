// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Values;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class InterpretSwitchExpressions : DsTest
    {
        public InterpretSwitchExpressions(ITestOutputHelper output) : base(output) { }
        
        [Fact]
        public void EvaluateCase()
        {
            string spec =
@"export const r = 'a' switch { 'a': 42} ;";

            var result = EvaluateExpressionWithNoErrors(spec, "r");
            Assert.Equal(42, result);
        }

        [Fact]
        public void EvaluateCaseFirstWins()
        {
            string spec =
                @"export const r = 'a' switch { 'a': 42, 'a': 52} ;";

            var result = EvaluateExpressionWithNoErrors(spec, "r");
            Assert.Equal(42, result);
        }

        [Fact]
        public void MatchesSecond()
        {
            string spec =
                @"export const r = 'b' switch { 'a': 42, 'b': 52} ;";

            var result = EvaluateExpressionWithNoErrors(spec, "r");
            Assert.Equal(52, result);
        }

        [Fact]
        public void FallThroughIsUndefined()
        {
            string spec =
                @"export const r = 'b' switch { 'a': 42, 'a': 52} ;";

            var result = EvaluateExpressionWithNoErrors(spec, "r");
            Assert.Equal(UndefinedValue.Instance, result);
        }

        [Fact]
        public void EvaluateCaseWithNumber()
        {
            string spec =
                @"export const r = 1 switch { 0: 32, 1: 42, 2: 52} ;";

            var result = EvaluateExpressionWithNoErrors(spec, "r");
            Assert.Equal(42, result);
        }
        
        [Fact]
        public void EvaluateCaseWithNumberFromVariable()
        {
            string spec =
                @"const left = 1;
export const r = left switch { 0: 32, 1: 42, 2: 52} ;";

            var result = EvaluateExpressionWithNoErrors(spec, "r");
            Assert.Equal(42, result);
        }

        [Fact]
        public void EvaluateCaseWithDefault()
        {
            string spec =
                @"const left = 5;
export const r = left switch { 0: 32, 1: 42, default: 52} ;";

            var result = EvaluateExpressionWithNoErrors(spec, "r");
            Assert.Equal(52, result);
        }
    }
}
