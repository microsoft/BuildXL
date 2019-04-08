// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.DScriptV2
{
    /// <summary>
    /// Set of unit tests for <see cref="Workspace"/> computation.
    /// </summary>
    /// <remarks>
    /// These set of tests are more like end-to-end tests that runs agains real resolvers.
    /// This is the main difference between them and another set of tests that check workspace computation in more isolated scnenarios.
    /// </remarks>
    public class TestWorkspaces : SemanticBasedTests
    {
        public TestWorkspaces(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void MainConfigurationFileShouldBePartOfTheWorkspace()
        {
            string packageConfig1 = @"
module({
    name: 'Pack1',
    projects: [],
});
";

            string packageConfig2 = @"
module({
    name: 'Pack2',
    projects: [],
});
";
            
            var result =
                BuildLegacyConfigurationWithPrelude("config({modules: [f`Pack1/package.config.dsc`, f`Pack1/Pack2/package.config.dsc`, f`Sdk.Prelude/package.config.dsc`]});")
                    .AddSpec("Pack1/package.config.dsc", packageConfig1)
                    .AddSpec("Pack1/Pack2/package.config.dsc", packageConfig2)
                    .BuildWorkspace();

            var configurationModule = result.ConfigurationModule;
            Assert.NotNull(configurationModule);

            Assert.Equal(4, configurationModule.Specs.Count);
            Assert.True(configurationModule.Specs.Any(kv => kv.Value.ToDisplayStringV2().Contains("name: 'Pack1'")));
            Assert.True(configurationModule.Specs.Any(kv => kv.Value.ToDisplayStringV2().Contains("name: 'Pack2'")));
            Assert.True(configurationModule.Specs.Any(kv => kv.Value.ToDisplayStringV2().Contains("name: \"Sdk.Prelude\"")));
        }
    }
}
