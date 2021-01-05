// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.DScriptV2
{
    public class InBoxSDKResolverTests : SemanticBasedTests
    {
        public InBoxSDKResolverTests(ITestOutputHelper output) : base(output)
        { 
        }

        [Fact]
        public void InBoxSDKsAreImplicitlyIncluded()
        {
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
                .AddSpec("spec1.dsc", @"
import {Transformer} from 'Sdk.Transformers';
const x : Transformer.ToolDefinition = undefined;")
                .RootSpec("spec1.dsc")
                .EvaluateWithNoErrors();
        }

        [Fact]
        public void ExplicitlyDefinedModulesOverrideInBoxOnes()
        {
                Build().Configuration(@"
config({
    resolvers: [{
        kind: 'DScript',
        modules: [
            {
                moduleName: 'test', 
                projects: [f`spec1.dsc`]
            },
            {
                moduleName: 'Sdk.Transformers', 
                projects: [f`fakeTransformers.dsc`]
            },
        ],
    }]
});")
                .AddSpec("fakeTransformers.dsc", "namespace Transformer {@@public export interface FakeTransformers {}}")
                .AddSpec("spec1.dsc", @"
import {Transformer} from 'Sdk.Transformers';
const x : Transformer.FakeTransformers = undefined;")
                .RootSpec("spec1.dsc")
                .EvaluateWithNoErrors();
        }
    }
}
