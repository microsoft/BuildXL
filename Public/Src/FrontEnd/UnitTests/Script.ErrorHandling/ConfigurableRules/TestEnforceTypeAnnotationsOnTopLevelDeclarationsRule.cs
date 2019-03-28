// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestEnforceTypesAnnotationsOnTopLevelDeclarationsRule : DsTest
    {
        private const string Configuration = @"
config({
  frontEnd: {
    enabledPolicyRules: [""RequireTypeAnnotationsOnDeclarations""],
  }
});";

        public TestEnforceTypesAnnotationsOnTopLevelDeclarationsRule(ITestOutputHelper output)
            : base(output)
        {
        }

        [Theory]
        [InlineData(@"const x = 1;")]
        [InlineData(@"namespace A { const x = 1; }")]
        public void TestTypeIsEnforcedForTopLevelVariables(string code)
        {
            BuildWithoutDefautlLibraries()
                .AddPrelude()
                .LegacyConfiguration(Configuration)
                .AddSpec("build.dsc", code)
                .ParseWithDiagnosticId(LogEventId.MissingTypeAnnotationOnTopLevelDeclaration);
        }

        [Fact]
        public void TestTypeAnyIsNotAllowed()
        {
            var code = "const x: any = 1;";

            BuildWithoutDefautlLibraries()
                .AddPrelude()
                .LegacyConfiguration(Configuration)
                .AddSpec("build.dsc", code)
                .ParseWithDiagnosticId(LogEventId.NotAllowedTypeAnyOnTopLevelDeclaration);
        }

        [Fact]
        public void TestRuleDoesntApplyOnNonTopLevelDeclarations()
        {
            var code = @"
function f() {
    let x = 1;
    return x;
}";

            BuildWithoutDefautlLibraries()
                .AddPrelude()
                .LegacyConfiguration(Configuration)
                .AddSpec("build.dsc", code)
                .ParseWithNoErrors();
        }
    }
}
