// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Xunit;
using Xunit.Abstractions;

using static Test.BuildXL.FrontEnd.Core.ModuleConfigurationBuilder;

namespace Test.DScript.Ast.DScriptV2
{
    public sealed class TestPublicExports : DScriptV2Test
    {
        public TestPublicExports(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void PublicNamedExportsAreVisibleAcrossModules()
        {
            var result = BuildWithPrelude()
               .AddSpec(@"APackage/package.config.dsc", V2Module("APackage"))
               .AddSpec(@"APackage/package.dsc", @"
const x = 42;
@@public
export {x};")
                .AddSpec(@"BPackage/package.config.dsc", V2Module("BPackage"))
                .AddSpec(@"BPackage/package.dsc", @"
import * as A from 'APackage';

export const y = A.x;")
               .RootSpec(@"BPackage/package.dsc")
               .EvaluateExpressionWithNoErrors("y");

            Assert.Equal(42, result);
        }

        [Fact]
        public void MultiplePublicNamedExportsAsAreVisibleAcrossModules()
        {
            var result = BuildWithPrelude()
                .AddSpec(@"APackage/package.config.dsc", V2Module("APackage"))
                .AddSpec(@"APackage/package.dsc", @"
const x = 21;
const y = 21;
@@public
export {x as z, y};")
                .AddSpec(@"BPackage/package.config.dsc", V2Module("BPackage"))
                .AddSpec(@"BPackage/package.dsc", @"
import * as A from 'APackage';

export const y = A.z + A.y;")
                .RootSpec(@"BPackage/package.dsc")
                .EvaluateExpressionWithNoErrors("y");

            Assert.Equal(42, result);
        }

        [Fact]
        public void ExportStarFromIsVisibleAcrossModules()
        {
            var result = BuildWithPrelude()
                .AddSpec(@"APackage/package.config.dsc", V2Module("APackage"))
                .AddSpec(@"APackage/package.dsc", @"
@@public
export const x = 42;")
                .AddSpec(@"BPackage/package.config.dsc", V2Module("BPackage"))
                .AddSpec(@"BPackage/package.dsc", @"
@@public
export * from 'APackage';")
                .AddSpec(@"CPackage/package.config.dsc", CreatePackageConfig("CPackage", useImplicitReferenceSemantics: true))
                .AddSpec(@"CPackage/package.dsc", @"
import * as B from 'BPackage';
export const y = B.x;")
                .RootSpec(@"CPackage/package.dsc")
                .EvaluateExpressionWithNoErrors("y");

            Assert.Equal(42, result);
        }

        [Fact]
        public void NamedExportFromIsVisibleAcrossModules()
        {
            var result = BuildWithPrelude()
                .AddSpec(@"APackage/package.config.dsc", V2Module("APackage"))
                .AddSpec(@"APackage/package.dsc", @"
@@public
export const x = 21;
@@public
export const y = 21;")
                .AddSpec(@"BPackage/package.config.dsc", V2Module("BPackage"))
                .AddSpec(@"BPackage/package.dsc", @"
@@public
export {x, y as z} from 'APackage';")
                .AddSpec(@"CPackage/package.config.dsc", V2Module("CPackage"))
                .AddSpec(@"CPackage/package.dsc", @"
import * as B from 'BPackage';
export const y = B.x + B.z;")
                .RootSpec(@"CPackage/package.dsc")
                .EvaluateExpressionWithNoErrors("y");

            Assert.Equal(42, result);
        }
    }
}
