// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestForbidBindingPatternInDeclarationRule : DsTest
    {
        public TestForbidBindingPatternInDeclarationRule(ITestOutputHelper output)
            : base(output)
        {
        }

        [Theory]
        [InlineData("const {v1, v2} = {v1: 'hi', 'v2' : 'bye'};")]
        [InlineData("function f() {for (const {v1, v2} of [{v1: 'hi', v2: 'bye'}]) {}}")]
        public void TestBindingPatternInDeclarationRuleIsNotAllowed(string code)
        {
            ParseWithDiagnosticId(code, LogEventId.ReportBindingPatternInVariableDeclarationIsNowAllowed);
        }
    }
}
