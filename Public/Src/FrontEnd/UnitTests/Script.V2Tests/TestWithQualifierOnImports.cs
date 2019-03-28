// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.DScript.Ast.DScriptV2;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Workspaces
{
    public class TestWithQualifierOnImports : DScriptV2Test
    {
        public TestWithQualifierOnImports(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void WithQualifierIsGeneratedForModule()
        {
            string config = @"config({modules: globR(d`.`, 'package.config.dsc')});";
            BuildLegacyConfigurationWithPrelude(config)
               .AddSpec(@"APackage/package.config.dsc", CreatePackageConfig("APackage", useImplicitReferenceSemantics: true))
               .AddSpec(@"APackage/spec1.dsc", "export const x = 42;")
               .AddSpec(@"BPackage/package.config.dsc", CreatePackageConfig("BPackage", useImplicitReferenceSemantics: true))
               .AddSpec(@"BPackage/spec1.dsc", @"
import * as B from 'APackage';
export const b = B.withQualifier({});")
               .RootSpec(@"config.dsc")
               .ParseWithNoErrors();
        }

        [Fact]
        public void WithQualifierIsGeneratedForModuleWithTheRightReturnType()
        {
            BuildLegacyConfigurationWithPrelude()
               .AddSpec(@"APackage/package.config.dsc", CreatePackageConfig("APackage", useImplicitReferenceSemantics: true))
               .AddSpec(@"APackage/spec1.dsc", @"
@@public
export const x = 42;")
               .AddSpec(@"BPackage/package.config.dsc", CreatePackageConfig("BPackage", useImplicitReferenceSemantics: true))
               .AddSpec(@"BPackage/spec1.dsc", @"
import * as B from 'APackage';
export const x = B.withQualifier({}).x;")
               .RootSpec(@"config.dsc")
               .ParseWithNoErrors();
        }

        [Fact]
        public void WithQualifierReturnTypeContainsInternalValues()
        {
            var result = BuildLegacyConfigurationWithPrelude()
               .AddSpec(@"APackage/package.config.dsc", CreatePackageConfig("APackage", useImplicitReferenceSemantics: true))
               .AddSpec(@"APackage/spec1.dsc", @"
export const x = 42;
export const r = $.withQualifier({}).x;
")
               .RootSpec(@"APackage/spec1.dsc")
               .EvaluateExpressionWithNoErrors("r");

            Assert.Equal(42, result);
        }
    }
}
