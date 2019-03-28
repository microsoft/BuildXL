// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class InterpretTypeCasting : DsTest
    {
        public InterpretTypeCasting(ITestOutputHelper output)
            : base(output)
        {}

        [Fact]
        public void NestedNamespacesReference()
        {
            string spec = @"
namespace M {
    namespace N {
        export const a = 1;
    }
}
export const b = M.N.a;
";

            var result = Build()
                .AddSpec("spec.dsc", spec)
                .EvaluateExpressionWithNoErrors("spec.dsc", "M.N.a");
            Assert.Equal(1, result);
        }

        [Fact]
        public void ExportTwoInterfaceWithTheSameNameFromDifferentNamespaces()
        {
            string code = @"
namespace X {
  export interface Foo { x: number; }
}

namespace Y {
  export interface Foo { x: number; }
}

namespace M {
  export const r1 = (<X.Foo>{x: 42}).x;
  export const r2 = (<Y.Foo>{x: 43}).x;
}";

            var result = EvaluateExpressionsWithNoErrors(code, "M.r1", "M.r2");
            Assert.Equal(42, result["M.r1"]);
            Assert.Equal(43, result["M.r2"]);
        }
    }
}
