// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.DScript.Ast.DScriptV2;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestForbidLogicInProjectsRule : SemanticBasedTests
    {
        public TestForbidLogicInProjectsRule(ITestOutputHelper output)
            : base(output)
        {
        }

        [Theory]
        [InlineData("interface I {}")]
        [InlineData("type T = number;")]
        [InlineData("enum E { one }")]
        [InlineData("function f() {}")]
        public void TestLogicDeclarationsAreNotAllowed(string declaration)
        {
            BuildWithPrelude()
                .AddSpec("project.bp", declaration)
                .EvaluateWithDiagnosticId(LogEventId.NoBuildLogicInProjects);
        }

        [Fact]
        public void ExportingLambdasIsNotAllowed()
        {
            BuildWithPrelude()
                .AddSpec("project.bp", "export const x = (() => 1);")
                .EvaluateWithDiagnosticId(LogEventId.NoExportedLambdasInProjects);
        }
    }
}
