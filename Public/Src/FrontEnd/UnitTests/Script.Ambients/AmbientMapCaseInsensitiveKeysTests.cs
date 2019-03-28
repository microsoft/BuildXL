// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Values;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public class AmbientMapCaseInsensitiveKeysTests : DsTest
    {
        public AmbientMapCaseInsensitiveKeysTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void MapAddDoesNotAddAnElementIfTheCasingIsDifferent()
        {
            var result = EvaluateExpressionWithNoErrors(@"
export const x = Map.emptyCaseInsensitive().add('a', 42);
export const y = x.add('A', 36); 
export const result = y.count();
    
", "result");

            Assert.Equal(1, result);
        }

        [Fact]
        public void MapAddDoesAddAnElementIfTheCasingIsDifferent()
        {
            var result = EvaluateExpressionWithNoErrors(@"
export const x = Map.emptyCaseInsensitive().add('a', 42);
export const y = x.add('B', 36); 
export const result = y.count();
    
", "result");

            Assert.Equal(2, result);
        }

        [Fact]
        public void MapContainsKeyWithDifferentRegistry()
        {
            var result = EvaluateExpressionsWithNoErrors(@"
export const x = Map.emptyCaseInsensitive().add('a', 42);
export const containsCapital = x.containsKey('A');
export const containsLower = x.containsKey('a');
    
", "containsCapital", "containsLower");

            Assert.Equal(true, result["containsCapital"]);
            Assert.Equal(true, result["containsLower"]);
        }

        [Fact]
        public void MapIndexerReturnsUndefinedInAllCases()
        {
            var result = EvaluateExpressionsWithNoErrors(@"
export const x = Map.emptyCaseInsensitive().add('a', 42);
export const indexerWithLowerA = x['a']; // undefined
export const indexerWithUpperA = x['A']; // undefined
    
", "indexerWithLowerA", "indexerWithUpperA");

            Assert.Equal(UndefinedValue.Instance, result["indexerWithLowerA"]);
            Assert.Equal(UndefinedValue.Instance, result["indexerWithUpperA"]);
        }

        [Fact]
        public void MapRemovesAKeyWithDifferentRegistry()
        {
            var result = EvaluateExpressionWithNoErrors(@"
export const x = Map.emptyCaseInsensitive().add('a', 42);
export const result = x.remove('A').count();
    
", "result");

            Assert.Equal(0, result);
        }
    }
}
