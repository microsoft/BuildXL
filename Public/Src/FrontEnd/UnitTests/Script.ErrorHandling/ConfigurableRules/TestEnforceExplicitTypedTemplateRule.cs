// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.DScript.Ast.DScriptV2;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling.ConfigurableRules
{
    public class TestEnforceExplicitTypedTemplateRule : SemanticBasedTests
    {
        private const string Configuration = @"
config({
  frontEnd: {
    enabledPolicyRules: [""TypedTemplates""],
  }
});";

        public TestEnforceExplicitTypedTemplateRule(ITestOutputHelper output) : base(output)
        {}

        [Fact]
        public void TemplateShouldHaveAType()
        {
            string code = "export declare const template = {};";

            var result = BuildLegacyConfigurationWithPrelude(Configuration)
                .AddSpec("foo.dsc", code)
                .RootSpec("foo.dsc")
                .EvaluateWithDiagnostics();

            result.ExpectErrorCode(LogEventId.TemplateDeclarationShouldHaveAType);
        }

        [Fact]
        public void TemplateShouldntHaveAnyType()
        {
            string code = "export declare const template : any = {};";

            var result = BuildLegacyConfigurationWithPrelude(Configuration)
                .AddSpec("foo.dsc", code)
                .RootSpec("foo.dsc")
                .EvaluateWithDiagnostics();

            result.ExpectErrorCode(LogEventId.TemplateDeclarationShouldNotHaveAnyType);
        }
    }
}
