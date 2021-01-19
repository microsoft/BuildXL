// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.DScriptV2
{
    public class ModuleDefinedMountTests : SemanticBasedTests
    {
        public ModuleDefinedMountTests(ITestOutputHelper output) : base(output)
        { 
        }

        [Fact]
        public void ModuleDefinedMountsAreEffective()
        {
            Build().Configuration(@"
config({
    resolvers: [{
        kind: 'DScript',
        modules: [ f`module.config.dsc`],
    }]
});")
                .AddSpec("module.config.dsc", "module({name: 'Test', mounts:[{name: 'module-mount', path: p`.`, isWritable: false}]});")
                .AddSpec("test.dsc", "const x  = Context.getMount('module-mount');")
                .RootSpec("test.dsc")
                .EvaluateWithNoErrors(); 
        }
    }
}
