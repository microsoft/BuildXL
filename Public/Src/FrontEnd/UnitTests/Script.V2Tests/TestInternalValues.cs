// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Xunit;
using Xunit.Abstractions;

using static Test.BuildXL.FrontEnd.Core.ModuleConfigurationBuilder;

namespace Test.DScript.Ast.DScriptV2
{
    public sealed class TestInternalValues : DScriptV2Test
    {
        public TestInternalValues(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void InternalValuesAreVisibleToLocalFile()
        {
            var code = @"
export const x = 42;
const y = x;";

            BuildLegacyConfigurationWithPrelude()
                .AddSpec(@"APackage/package.config.dsc", V2Module("APackage"))
                .AddSpec(@"APackage/package.dsc", code)
                .RootSpec(@"APackage/package.dsc")
                .ParseWithNoErrors();
        }

        [Fact]
        public void InternalValuesAreVisibileWithinModule()
        {
            BuildLegacyConfigurationWithPrelude()
                .AddSpec(@"APackage/package.config.dsc", V2Module("APackage"))
                .AddSpec(@"APackage/package.dsc", "export const x = 42;")
                .AddSpec(@"APackage/file.dsc", "export const y = x;")
                .RootSpec(@"APackage/file.dsc")
                .ParseWithNoErrors();
        }

        [Fact]
        public void InternalValuesAreNotVisibileAcrossModules()
        {
            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec(@"APackage/package.config.dsc", V2Module("APackage"))
                .AddSpec(@"APackage/package.dsc", "export const x = 42;")
                .AddSpec(@"BPackage/package.config.dsc", CreatePackageConfig("BPackage", useImplicitReferenceSemantics: true))
                .AddSpec(@"BPackage/package.dsc", "export const y = x;")
                .RootSpec(@"BPackage/package.dsc")
                .ParseWithFirstError();

            Assert.Contains("Cannot find name 'x'", result.FullMessage);
        }

        [Fact]
        public void InternalValuesViaExportSpecifiersAreVisibileWithinModule()
        {
            BuildLegacyConfigurationWithPrelude()
                .AddSpec(@"APackage/package.config.dsc", V2Module("APackage"))
                .AddSpec(@"APackage/package.dsc", @"
const x = 42;
export {x};")
                .AddSpec(@"APackage/file.dsc", "export const y = x;")
                .RootSpec(@"APackage/file.dsc")
                .ParseWithNoErrors();
        }

        [Fact]
        public void InternalValuesViaImportExportSpecifiersAreVisibileWithinModule()
        {
            BuildLegacyConfigurationWithPrelude()
                .AddSpec(@"APackage/package.config.dsc", V2Module("APackage"))
                .AddSpec(@"APackage/package.dsc", @"
@@public
export const x = 42;
@@public
export interface I {};")
                .AddSpec(@"BPackage/package.config.dsc", CreatePackageConfig("BPackage", useImplicitReferenceSemantics: true))
                .AddSpec(@"BPackage/package1.dsc", @"
import * as APackage from 'APackage';
export {APackage};")
                .AddSpec(@"BPackage/package2.dsc", @"
export const x = APackage.x;
export const y: APackage.I = {};")
                .RootSpec(@"BPackage/package2.dsc")
                .ParseWithNoErrors();
        }

        [Fact]
        public void InternalValuesViaExportSpecifiersAreNotVisibleInSameSpec()
        {
            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec(@"APackage/package.config.dsc", V2Module("APackage"))
                .AddSpec(@"APackage/package.dsc", @"
const x = 42;
export {x as y};
const z = y;")
                .RootSpec(@"APackage/package.dsc")
                .ParseWithFirstError();

            Assert.Contains("Cannot find name 'y'", result.FullMessage);
        }

        [Fact]
        public void InternalValuesViaExportSpecifiersAreNotVisibileAcrossModules()
        {
            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec(@"APackage/package.config.dsc", V2Module("APackage"))
                .AddSpec(@"APackage/package.dsc", @"
const x = 42;
export {x};")
                .AddSpec(@"BPackage/package.config.dsc", CreatePackageConfig("BPackage", useImplicitReferenceSemantics: true))
                .AddSpec(@"BPackage/package.dsc", "export const y = x;")
                .RootSpec(@"BPackage/package.dsc")
                .ParseWithFirstError();

            Assert.Contains("Cannot find name 'x'", result.FullMessage);
        }

        [Fact]
        public void ModulesAreProperlyBoundAtRuntimeViaExportSpecifiers()
        {
            var result = BuildWithPrelude()
                .AddSpec(@"APackage/package.config.dsc", V2Module("APackage"))
                .AddSpec(@"APackage/package.dsc", @"
@@public
export const x = 42;")
                .AddSpec(@"BPackage/package.config.dsc", CreatePackageConfig("BPackage", useImplicitReferenceSemantics: true))
                .AddSpec(@"BPackage/package1.dsc", @"
import * as APackage from 'APackage';
export {APackage};")
                .AddSpec(@"BPackage/package2.dsc", @"
export const x = APackage.x;")
                .RootSpec(@"BPackage/package2.dsc")
                .EvaluateExpressionWithNoErrors("x");

            Assert.Equal(42, result);
        }
    }
}
