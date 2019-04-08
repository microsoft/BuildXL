// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Test.BuildXL.FrontEnd.Core;
using TypeScript.Net.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class InterpretObjectLiterals : DsTest
    {
        public InterpretObjectLiterals(ITestOutputHelper output) : base(output) { }
        
        [Fact]
        public void IndexerShouldHaveAnExpression()
        {
            string spec =
@"const x = [1,2,];
export const r = {r: x[]};
";
            EvaluateWithTypeCheckerDiagnostic(spec, Errors.Expression_expected);
        }
    }
}
