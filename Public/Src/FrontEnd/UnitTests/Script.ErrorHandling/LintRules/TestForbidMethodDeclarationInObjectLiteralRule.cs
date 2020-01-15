// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestForbidMethodDeclarationInObjectLiteralRule : DsTest
    {
        public TestForbidMethodDeclarationInObjectLiteralRule(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestInvalidMethodInLiteralElement()
        {
            string spec =
@"const x = {f(x: number): number { return x; } };";

            ParseWithDiagnosticId(spec, LogEventId.NotSupportedMethodDeclarationInEnumMember);
        }
    }
}
