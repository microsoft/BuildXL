// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Xunit;
using Xunit.Abstractions;

using static Test.BuildXL.FrontEnd.Core.ModuleConfigurationBuilder;

namespace Test.DScript.Ast.DScriptV2
{
    public sealed class TestValueMerging : DScriptV2Test
    {
        public TestValueMerging(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void InternalValuesMergeAsExpected()
        {
            BuildLegacyConfigurationWithPrelude()
              .AddSpec(@"APackage/package.config.dsc", V2Module("APackage"))
              .AddSpec(@"APackage/package.dsc", @"
export interface I {
    field1: string;
}")
              .AddSpec(@"APackage/file1.dsc", @"
export interface I {
    field2: string;
}")
              .AddSpec(@"APackage/file2.dsc", @"
export const x : I = undefined;
export const f1 = x.field1;
export const f2 = x.field2;")
              .RootSpec(@"APackage/file2.dsc")
              .ParseWithNoErrors();
        }

        [Fact]
        public void PublicValuesMergeAsExpected()
        {
            BuildLegacyConfigurationWithPrelude()
             .AddSpec(@"APackage/package.config.dsc", V2Module("APackage"))
             .AddSpec(@"BPackage/package.config.dsc", CreatePackageConfig("BPackage", useImplicitReferenceSemantics: true))
             .AddSpec(@"APackage/package.dsc", @"
@@public
export interface I {
    field1: string;
}")
             .AddSpec(@"APackage/file1.dsc", @"
@@public
export interface I {
    field2: string;
}")
            .AddSpec(@"BPackage/package.dsc", @"
import * as A from ""APackage"";

export const x : A.I = undefined;
export const f1 = x.field1;
export const f2 = x.field2;
")
             .RootSpec(@"BPackage/package.dsc")
             .ParseWithNoErrors();
        }

        [Fact]
        public void MergedDeclarationsShouldBeAllPublicOrAllNonPublicAcrossFiles()
        {
            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec(@"APackage/package.config.dsc",
                    V2Module("APackage"))
                .AddSpec(@"BPackage/package.config.dsc",
                    V2Module("BPackage"))
                .AddSpec(@"APackage/package.dsc", @"
@@public
export interface I {
    field1: string;
}")
                .AddSpec(@"APackage/file1.dsc", @"
export interface I {
    field2: string;
}")
                .AddSpec(@"BPackage/package.dsc", @"
import * as A from ""APackage"";

export const x : A.I = undefined;
export const f1 = x.field1;
export const f2 = x.field2;
")
                .RootSpec(@"BPackage/package.dsc")
                .ParseWithFirstError();

            Assert.True(result.FullMessage.Contains("must be all public or all non-public"));
        }

        [Fact]
        public void MergedDeclarationsShouldBeAllPublicOrAllNonPublicSameFile()
        {
            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec(@"APackage/package.config.dsc", V2Module("APackage"))
                .AddSpec(@"BPackage/package.config.dsc", V2Module("BPackage"))
                .AddSpec(@"APackage/package.dsc", @"
@@public
export interface I {
    field1: string;
}

export interface I {
    field2: string;
}")
                .AddSpec(@"BPackage/package.dsc", @"
import * as A from ""APackage"";

export const x : A.I = undefined;
export const f1 = x.field1;
export const f2 = x.field2;
")
                .RootSpec(@"BPackage/package.dsc")
                .ParseWithFirstError();

            Assert.True(result.FullMessage.Contains("must be all public or all non-public"));
        }
    }
}
