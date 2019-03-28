// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.DScript.Ast.DScriptV2;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestForbidNonVariableDeclarationRule : SemanticBasedTests
    {
        public TestForbidNonVariableDeclarationRule(ITestOutputHelper output)
            : base(output)
        { }

        [Theory]
        [InlineData("export function foo(){ function qualifier() {} }")] // Top level function 'qualifier' would be an error because it clashes with generated top-level 'qualifier'
        [InlineData("export function foo(qualifier: string){}")]
        [InlineData("export const enum MyEnum {qualifier}")]

        // TODO: Uncomment when the casing restriction rule is relaxed. The cases below are currently rejected but for casing reasons.
        // [InlineData("export const enum qualifier {aValue}")]
        // [InlineData("namespace qualifier { export const x = 42;}")]
        // [InlineData("interface qualifier { }")]
        public void QualifierShouldNotBeDeclaredOutsideOfAVariableDeclaration(string code)
        {
            string boo = "export const x = 1;";

            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec("foo.dsc", code)
                .RootSpec("foo.dsc")
                .AddSpec("boo.dsc", boo)
                .EvaluateWithDiagnostics();

            result.ExpectErrorCode(LogEventId.QualifierNameCanOnlyBeUsedInVariableDeclarations);
        }
   }
}
