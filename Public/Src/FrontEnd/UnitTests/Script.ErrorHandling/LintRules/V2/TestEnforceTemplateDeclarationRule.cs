// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.DScript.Ast.DScriptV2;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestEnforceTemplateDeclarationRule : SemanticBasedTests
    {
        public TestEnforceTemplateDeclarationRule(ITestOutputHelper output)
            : base(output)
        { }

        [Fact]
        public void TemplateShouldBeAlone()
        {
            string code = "export declare const template = {}, myVar;";

            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec("foo.dsc", code)
                .RootSpec("foo.dsc")
                .EvaluateWithDiagnostics();

            result.ExpectErrorCode(LogEventId.TemplateDeclarationShouldBeAloneInTheStatement);
        }

        [Fact]
        public void TemplateShouldBeNamespaceOrTopLevelDeclaration()
        {
            string code = @"
function foo() {
    const template = {};
}";

            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec("foo.dsc", code)
                .RootSpec("foo.dsc")
                .EvaluateWithDiagnostics();

            result.ExpectErrorCode(LogEventId.TemplateDeclarationShouldBeTopLevel);
        }

        [Theory]
        [InlineData("const template = {};")]
        [InlineData("declare const template : {} = {};")]
        [InlineData("export const template : {} = {};")]
        public void TemplateShouldHaveTheRightModifiers(string code)
        {
            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec("foo.dsc", code)
                .RootSpec("foo.dsc")
                .EvaluateWithDiagnostics();

            result.ExpectErrorCode(LogEventId.TemplateDeclarationShouldBeConstExportAmbient);
        }

        [Fact]
        public void TemplateShouldHaveInitializer()
        {
            string code = "export declare const template;";

            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec("foo.dsc", code)
                .RootSpec("foo.dsc")
                .EvaluateWithDiagnostics();

            result.ExpectErrorCode(LogEventId.TemplateDeclarationShouldHaveInitializer);
        }

        [Theory]
        [InlineData(@"
const x = 42;
export declare const template = {};
")]
        [InlineData(@"
namespace A {
    const x = 42;
    export declare const template = {};
}
")]
        public void TemplateShouldBeFirstStatementInTheBlock(string code)
        {
            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec("foo.dsc", code)
                .RootSpec("foo.dsc")
                .EvaluateWithDiagnostics();

            result.ExpectErrorCode(LogEventId.TemplateDeclarationShouldBeTheFirstStatement);
        }
    }
}
