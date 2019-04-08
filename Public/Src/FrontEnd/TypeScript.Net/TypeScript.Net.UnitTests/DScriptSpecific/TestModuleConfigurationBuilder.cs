// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
