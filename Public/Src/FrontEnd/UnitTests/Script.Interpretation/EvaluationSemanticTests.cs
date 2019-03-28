// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    /// <summary>
    /// Set of integration tests for evaluating enums.
    /// </summary>
    /// <remarks>
    /// Currently DScript doesn't support runtime behavior for type casting, that's why every test case
    /// has explicit cast from enum member to a number or use custom equality comparer.
    /// </remarks>
    public sealed class EvaluationSemanticTests : DsTest
    {
        public EvaluationSemanticTests(ITestOutputHelper output)
            : base(output)
        {}

        [Fact]
        public void BooleanAndOperatorShouldHaveShortCircuitBehavior()
        {
            string spec = @"
function returnTruthy() {
  return true;
}

function emitError() {
  Assert.fail(""foo invocation"");
  return false;
}

export const r = returnTruthy() || emitError();
";

            var result = EvaluateExpressionWithNoErrors(spec, "r");

            Assert.Equal(true, result);
        }

        [Fact]
        public void BooleanOrOperatorShouldHaveShortCircuitBehavior()
        {
            string spec = @"
function returnFalsy() {
  return false;
}

function emitError() {
  Assert.fail(""foo invocation"");
  return false;
}

export const r = returnFalsy() && emitError();
";

            var result = EvaluateExpressionWithNoErrors(spec, "r");

            Assert.Equal(false, result);
        }
    }
}
