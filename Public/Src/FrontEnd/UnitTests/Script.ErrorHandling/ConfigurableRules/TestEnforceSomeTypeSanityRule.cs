// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestEnforceSomeTypeSanityRule : DsTest
    {
        private const string Configuration = @"
config({
  frontEnd: {
    enabledPolicyRules: [""EnforceSomeTypeSanity""],
  }
});";

        public TestEnforceSomeTypeSanityRule(ITestOutputHelper output)
            : base(output)
        {
        }

        [Theory]
        [InlineData(@"export const x : any = 1;")]
        [InlineData(@"export const x : any = {a : 42};")]
        [InlineData(@"export namespace A {export const x : any = {a : 42};}")]
        [InlineData(@"export const x : any[] = [];")]
        [InlineData(@"export const x : Map<any, any> = undefined;")]
        [InlineData(@"export const x : Map<number, any> = undefined;")]
        [InlineData(@"export const x : Map<any, number> = undefined;")]
        public void TestAnyIsNotAllowedInExportedTopLevelValues(string code)
        {
            var result = Build()
                .LegacyConfiguration(Configuration)
                .AddSpec("build.dsc", code)
                .ParseWithFirstError();

            Assert.Equal((int)LogEventId.NotAllowedTypeAnyOnTopLevelDeclaration, result.ErrorCode);
        }

        [Fact]
        public void TestFunctionsDeclareAReturnType()
        {
            var code = "function foo() {}";

            var result = Build()
                .LegacyConfiguration(Configuration)
                .AddSpec("build.dsc", code)
                .ParseWithFirstError();

            Assert.Equal((int)LogEventId.FunctionShouldDeclareReturnType, result.ErrorCode);
        }

        [Fact]
        public void NoWarningForNestedFunctionWithNoDeclaredReturnType()
        {
            var code = "function foo() {}";

            var result = Build()
                .LegacyConfiguration(Configuration)
                .AddSpec("build.dsc", code)
                .ParseWithFirstError();

            Assert.Equal((int)LogEventId.FunctionShouldDeclareReturnType, result.ErrorCode);
        }

        [Theory]
        [InlineData(@"export const x = {foo : 42};")]
        [InlineData(@"export namespace A {export const x = {foo : 42};}")]
        public void TestObjectLiteralAssignmentsNeedTypeAnnotationOnTopLevelDeclarations(string code)
        {
            var result = Build()
                .LegacyConfiguration(Configuration)
                .AddSpec("build.dsc", code)
                .ParseWithFirstError();

            Assert.Equal((int)LogEventId.MissingTypeAnnotationOnTopLevelDeclaration, result.ErrorCode);
        }

        [Theory]
        [InlineData(@"function x(): void { const x = {foo : 42}; }")]
        [InlineData(@"const x = {foo : 42};")]
        [InlineData(@"const x : any = 1;")]
        [InlineData(@"namespace A {const x = {foo : 42};}")]
        [InlineData(@"namespace A {const x : any = {a : 42};}")]
        [InlineData(@"export namespace A {const x : any = {a : 42};}")]
        public void TestNonExportedTopLevelValuesDoNotApplyForThisRule(string code)
        {
            Build()
               .LegacyConfiguration(Configuration)
               .AddSpec("build.dsc", code)
               .ParseWithNoErrors();
        }
    }
}
