// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Test.DScript.Ast.DScriptV2;
using Xunit;
using Xunit.Abstractions;

using static Test.BuildXL.FrontEnd.Core.ModuleConfigurationBuilder;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Test.DScript.Workspaces
{
    public class TestDefaultQualifierGeneration : DScriptV2Test
    {
        public TestDefaultQualifierGeneration(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void DefaultQualifierIsGeneratedWhenAbsent()
        {
            BuildLegacyConfigurationWithPrelude()
                .AddSpec(@"APackage/package.config.dsc", V2Module("APackage"))
                .AddSpec(@"APackage/package.dsc", "const x = qualifier;")
                .RootSpec(@"APackage/package.dsc")
                .ParseWithNoErrors();
        }

        [Fact]
        public void DefaultQualifierIsGeneratedWhenAbsentAcrossModule()
        {
            BuildLegacyConfigurationWithPrelude()
                .AddSpec(@"APackage/package.config.dsc", V2Module("APackage"))
                .AddSpec(@"APackage/spec1.dsc", "const x = qualifier;")
                .AddSpec(@"APackage/spec2.dsc", "const y = qualifier;")
                .RootSpec(@"APackage/spec1.dsc")
                .ParseWithNoErrors();
        }

        [Fact]
        public void GeneratedDefaultQualifierIsImmutable()
        {
            var diagnostic = BuildLegacyConfigurationWithPrelude()
                .AddSpec(@"APackage/package.config.dsc", V2Module("APackage"))
                .AddSpec(@"APackage/package.dsc", "export const x = (() => {qualifier = qualifier;})();")
                .RootSpec(@"APackage/package.dsc")
                .ParseWithFirstError();

            Assert.Contains("Left-hand side of assignment expression cannot be a constant", diagnostic.FullMessage);
        }

        [Fact]
        public void GeneratedDefaultQualifierHasEmptyType()
        {
            BuildLegacyConfigurationWithPrelude()
                .AddSpec(@"APackage/package.config.dsc", V2Module("APackage"))
                .AddSpec(@"APackage/package.dsc", "export const x : typeof qualifier = {};")
                .RootSpec(@"APackage/package.dsc")
                .ParseWithNoErrors();
        }

        [Fact]
        public void DefaultQualifierIsNotGeneratedWhenPresent()
        {
            var diagnostic = BuildLegacyConfigurationWithPrelude()
               .AddSpec(@"APackage/package.config.dsc", V2Module("APackage"))
               .AddSpec(@"APackage/package.dsc", @"
export declare const qualifier: {test: string};
export const x : typeof qualifier = {};")
               .RootSpec(@"APackage/package.dsc")
               .ParseWithFirstError();

            Assert.Contains("Type '{}' is not assignable to type '{ test: string; }", diagnostic.FullMessage);
        }

        [Fact]
        public void DefaultQualifierIsNotGeneratedWhenPresentAcrossModule()
        {
            var diagnostic = BuildLegacyConfigurationWithPrelude()
               .AddSpec(@"APackage/package.config.dsc", V2Module("APackage"))
               .AddSpec(@"APackage/spec1.dsc", "export declare const qualifier: {test: string};")
               .AddSpec(@"APackage/spec2.dsc", "export const x : typeof qualifier = {};")
               .RootSpec(@"APackage/spec1.dsc")
               .ParseWithFirstError();

            Assert.Contains("Type '{}' is not assignable to type '{ test: string; }", diagnostic.FullMessage);
        }
    }
}
