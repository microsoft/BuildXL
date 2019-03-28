// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class InterpretNamespaces : DsTest
    {
        public InterpretNamespaces(ITestOutputHelper output)
            : base(output)
        {}

        // Type casting doesn't change runtime behavior, so those tests are almost syntactical
        [Fact]
        public void TypeCastAndAsCastShouldBeSimilarForEnum()
        {
            // Currently, DScript doesn't support initializers that are not numbers.
            string spec = @"
namespace M {
    const enum Enum1 {
       value1 = 1,
    }

    export const v1 = <number>Enum1.value1;
    export const v2 = Enum1.value1 as number;
}";

            var result = EvaluateExpressionsWithNoErrors(spec, "M.v1", "M.v2");

            Assert.Equal(result["M.v1"], result["M.v2"]);
        }
    }
}
