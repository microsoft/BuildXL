// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public class AmbientPathTests : DsTest
    {
        public AmbientPathTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void GetToDiangosticString()
        {
            var spec = @"
const result: string = p`a/b/foo.cs`.toDiagnosticString();
";
            var result = (string)Build().AddSpec(spec).EvaluateExpressionWithNoErrors("result");
            AssertCanonicalEquality(@"a\b\foo.cs", result);
        }
    }
}
