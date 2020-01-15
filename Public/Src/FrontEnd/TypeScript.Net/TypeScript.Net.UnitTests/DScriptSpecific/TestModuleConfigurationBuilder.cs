// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using TypeScript.Net.DScript;
using Xunit;

namespace Test.DScript.DScriptSpecific
{
    public class TestModuleConfigurationBuilder
    {
        [Fact]
        public void NugetPackageModuleConfigGenerator()
        {
            var moduleConfig = new ModuleConfigurationBuilder()
                .Name("myModule")
                .Version("42")
                .NameResolution(implicitNameResolution: true)
                .Build();

            var text = moduleConfig.ToDisplayStringV2();
            Assert.Equal(
@"module({
    name: ""myModule"",
    version: ""42"",
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
});",
                text);
        }
    }
}
