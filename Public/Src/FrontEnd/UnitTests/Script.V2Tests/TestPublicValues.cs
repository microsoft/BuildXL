// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.DScriptV2
{
    public sealed class TestPublicValues : DScriptV2Test
    {
        public TestPublicValues(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void PublicValuesAreVisibleToLocalFile()
        {
            BuildLegacyConfigurationWithPrelude()
               .AddSpec(@"APackage/package.config.dsc", CreatePackageConfig("APackage", useImplicitReferenceSemantics: true))
               .AddSpec(@"APackage/package.dsc", @"
@@public
export const x = 42;
const y = x;")
               .RootSpec(@"APackage/package.dsc")
               .ParseWithNoErrors();
        }

        [Fact]
        public void PublicValuesAreVisibleWithinModule()
        {
            BuildLegacyConfigurationWithPrelude()
               .AddSpec(@"APackage/package.config.dsc", CreatePackageConfig("APackage", useImplicitReferenceSemantics: true))
               .AddSpec(@"APackage/package.dsc", @"
@@public
export const x = 42;
const y = x;")
               .AddSpec(@"APackage/file1.dsc", "export const y = x;")
               .RootSpec(@"APackage/file1.dsc")
               .ParseWithNoErrors();
        }

        [Theory]
        [InlineData(@"namespace N { @@public export const x = 42; }", "A.N.x")]
        [InlineData(@"@@public export const x = 42;", "A.x")]
        public void PublicValuesAreVisibleAcrossModulesWhenImported(string definition, string reference)
        {
            BuildLegacyConfigurationWithPrelude()
               .AddSpec(@"APackage/package.config.dsc", CreatePackageConfig("APackage", useImplicitReferenceSemantics: true))
               .AddSpec(@"BPackage/package.config.dsc", CreatePackageConfig("BPackage", useImplicitReferenceSemantics: true))
               .AddSpec(@"APackage/package.dsc", definition)
               .AddSpec(@"BPackage/package.dsc", $@"
import * as A from ""APackage"";
export const y = {reference};")
               .RootSpec(@"BPackage/package.dsc")
               .ParseWithNoErrors();
        }

        [Fact]
        public void PublicValuesAreNotVisibleAcrossModulesIfNotImported()
        {
            var result = BuildLegacyConfigurationWithPrelude()
              .AddSpec(@"APackage/package.config.dsc", CreatePackageConfig("APackage", useImplicitReferenceSemantics: true))
              .AddSpec(@"BPackage/package.config.dsc", CreatePackageConfig("BPackage", useImplicitReferenceSemantics: true))
              .AddSpec(@"APackage/package.dsc", @"
@@public
export const x = 42;")
              .AddSpec(@"BPackage/package.dsc", @"
export const y = x;")
              .RootSpec(@"BPackage/package.dsc")
              .ParseWithFirstError();

            Assert.Contains("Cannot find name 'x'", result.FullMessage);
        }

        [Theory]
        [InlineData(@"namespace N { export const x = 42; }", "A.N.x")]
        [InlineData(@"export const x = 42;", "A.x")]
        public void ValuesAreNotVisibleAcrossModulesIfNotPublic(string definition, string reference)
        {
            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec(@"APackage/package.config.dsc", CreatePackageConfig("APackage", useImplicitReferenceSemantics: true))
                .AddSpec(@"BPackage/package.config.dsc", CreatePackageConfig("BPackage", useImplicitReferenceSemantics: true))
                .AddSpec(@"APackage/package.dsc", definition)
                .AddSpec(@"BPackage/package.dsc", $@"
import * as A from ""APackage"";
export const y = {reference};")
                .RootSpec(@"BPackage/package.dsc")
                .ParseWithFirstError();

            Assert.Contains("Property 'x' does not exist on type", result.FullMessage);
        }

        [Fact]
        public void OnePublicValueDoesNotMakeASiblingPublic()
        {
            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec(@"APackage/package.config.dsc", CreatePackageConfig("APackage", useImplicitReferenceSemantics: true))
                .AddSpec(@"BPackage/package.config.dsc", CreatePackageConfig("BPackage", useImplicitReferenceSemantics: true))
                .AddSpec(@"APackage/package.dsc", @"
@@public
export const x = 42;

export const y = 43;")
                .AddSpec(@"BPackage/package.dsc", @"
import * as A from ""APackage"";
export const z = A.y;")
                .RootSpec(@"BPackage/package.dsc")
                .ParseWithFirstError();

            Assert.Contains("Property 'y' does not exist on type", result.FullMessage);
        }

    }
}
