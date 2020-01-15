// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.FrontEnd.Script.Tracing;
using Test.DScript.Ast.DScriptV2;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestForbidNonVariableTemplateRule : SemanticBasedTests
    {
        public TestForbidNonVariableTemplateRule(ITestOutputHelper output)
            : base(output)
        { }

        [Theory]
        [InlineData("export function foo(){ function template() {} }")] 
        [InlineData("export function foo(template: string){}")]
        [InlineData("export const enum MyEnum {template}")]
        [InlineData("export const enum template {aValue}")]
        [InlineData("namespace template { export const x = 42;}")]
        [InlineData("interface template { }")]
        public void TemplateShouldNotBeDeclaredOutsideOfAVariableDeclaration(string code)
        {
            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec("foo.dsc", code)
                .RootSpec("foo.dsc")
                .EvaluateWithDiagnostics();

            result.ExpectErrorCode(LogEventId.TemplateNameCanOnlyBeUsedInVariableDeclarations);
        }
   }
}
