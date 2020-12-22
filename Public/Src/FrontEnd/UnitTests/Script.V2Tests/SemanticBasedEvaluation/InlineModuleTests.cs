// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.DScriptV2
{
    public class InlineModuleTests : SemanticBasedTests
    {
        public InlineModuleTests(ITestOutputHelper output) : base(output)
        { }

        [Fact]
        public void BasicInlineModuleDefinitionIsHonored()
        {
            var result =
                Build().Configuration(@"
config({
    resolvers: [{
        kind: 'DScript',
        modules: [
            {
                moduleName: 'test', 
                projects: [f`spec1.dsc`]
            },
        ],
    }]
});")
                .AddSpec("spec1.dsc", "const x = 42;")
                .RootSpec("spec1.dsc")
                .EvaluateExpressionWithNoErrors("x");

            Assert.Equal(42, result);
        }

        [Fact]
        public void MultipleInlineModuleDefinitionIsHonored()
        {
            var result =
                Build().Configuration(@"
config({
    resolvers: [{
        kind: 'DScript',
        modules: [
            {
                moduleName: 'test1', 
                projects: [f`spec1.dsc`]
            },
            {
                moduleName: 'test2', 
                projects: [f`spec2.dsc`]
            },
            f`module.config.dsc`
        ],
    }]
});")
                .AddSpec("spec1.dsc", "@@public export const x = 42;")
                .AddSpec("spec2.dsc", "@@public export const y = importFrom('test1').x + 1;")
                .AddSpec("module.config.dsc", "module({name: 'test3', projects: [f`spec3.dsc`]});")
                .AddSpec("spec3.dsc", "export const z = importFrom('test2').y + 1;")
                .RootSpec("spec3.dsc")
                .EvaluateExpressionWithNoErrors("z");

            Assert.Equal(44, result);
        }

        [Fact]
        public void AnonynmousInlineModuleDefinitionIsAllowed()
        {
            var result =
                Build().Configuration(@"
config({
    resolvers: [{
        kind: 'DScript',
        modules: [
            {
                projects: [f`spec1.dsc`]
            },
        ],
    }]
});")
                .AddSpec("spec1.dsc", "const x = 42;")
                .RootSpec("spec1.dsc")
                .EvaluateExpressionWithNoErrors("x");

            Assert.Equal(42, result);
        }
    }
}
