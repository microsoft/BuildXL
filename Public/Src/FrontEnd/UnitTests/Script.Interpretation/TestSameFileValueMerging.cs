// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class TestSameFileValueMerging : DsTest
    {
        public TestSameFileValueMerging(ITestOutputHelper output)
            : base(output)
        {}

        [Fact]
        public void NamespaceCanMerge()
        {
            string spec = @"
namespace M {
    export const x = 1;
}

namespace M {
    export const y = 2;
}

export const b = M.x + M.y;
";

            var result = Build()
                .AddSpec("spec.dsc", spec)
                .EvaluateExpressionWithNoErrors("spec.dsc", "b");
            Assert.Equal(3, result);
        }

        [Fact]
        public void InterfacesCanMerge()
        {
            string spec = @"
interface I {
    x : number;
}

interface I {
    y : number;
}

export const b : I = {x : 1, y: 2};
export const c = b.x + b.y;
";

            var result = Build()
                .AddSpec("spec.dsc", spec)
                .EvaluateExpressionWithNoErrors("spec.dsc", "c");
            Assert.Equal(3, result);
        }

        [Fact]
        public void TypesCannotMerge()
        {
            string spec = @"
export type T = number;
export type T = bool;

export const a : T = undefined;
";

            Build()
                .AddSpec("spec.dsc", spec)
                .EvaluateWithDiagnosticId(LogEventId.TypeScriptBindingError);
        }

        [Fact]
        public void SameNameInterfaceAndNamespaceCanCoexist()
        {
            string spec = @"
export interface T {
    x : number;
}

export namespace T {
    export const x = 1;
}

export const t : T = {x: 2};
export const a = t.x;
export const b = T.x;
";

            var result = Build()
                .AddSpec("spec.dsc", spec)
                .RootSpec("spec.dsc")
                .EvaluateExpressionsWithNoErrors("a", "b");

            Assert.Equal(2, result["a"]);
            Assert.Equal(1, result["b"]);
        }
    }
}
