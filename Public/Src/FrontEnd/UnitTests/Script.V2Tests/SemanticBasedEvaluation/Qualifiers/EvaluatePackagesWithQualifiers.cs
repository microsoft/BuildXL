// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Xunit;
using Xunit.Abstractions;

using static Test.BuildXL.FrontEnd.Core.ModuleConfigurationBuilder;

namespace Test.DScript.Ast.DScriptV2
{
    public class EvaluatePackagesWithQualifiers : SemanticBasedTests
    {
        public EvaluatePackagesWithQualifiers(ITestOutputHelper output) : base(output)
        { }

        [Fact]
        public void EvaluateModuleWith3FilesAndOneSharedQualifierSpaceDeclaration()
        {
            string spec1 =
                @"export declare const qualifier: {x: '42', y: '36'};
export namespace X {
  export const v = qualifier.x;
}";

            string spec2 =
                @"export namespace Y {
   export const r = qualifier.y;
}";

            string rootSpec = @"
export const r1 = X.v; // '42'
export const r2 = Y.r; // '36'
";
            var result = BuildLegacyConfigurationWithPrelude()
                .Qualifier("{x: '42', y: '36'}")
                .AddSpec("root.dsc", rootSpec)
                .AddSpec("spec1.dsc", spec1)
                .AddSpec("spec2.dsc", spec2)
                .RootSpec("root.dsc")
                .EvaluateExpressionsWithNoErrors("r1", "r2");

            Assert.Equal("42", result["r1"]);
            Assert.Equal("36", result["r2"]);
        }

        [Fact]
        public void EvaluateModuleWithDifferentFilesAndTheSameNamespaceInDifferentFiles()
        {
            string spec1 =
@"export namespace X {
  export declare const qualifier: {x: '42', y: '36'};
  export const v = qualifier.x;
}";

            string spec2 =
@"export namespace X {
   // the qualifier defined in a different file
   export const r = qualifier.y;
}";

            string rootSpec = @"
const x = X.withQualifier({x: '42', y: '36'});
export const r1 = x.v; // '42'
export const r2 = x.r; // '36'
";
            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec("root.dsc", rootSpec)
                .AddSpec("spec1.dsc", spec1)
                .AddSpec("spec2.dsc", spec2)
                .RootSpec("root.dsc")
                .EvaluateExpressionsWithNoErrors("r1", "r2");

            Assert.Equal("42", result["r1"]);
            Assert.Equal("36", result["r2"]);
        }

        [Fact(Skip = "Work item - #1034601")]
        public void EvaluateQualifierViaDollarSign()
        {
            string spec1 =
@"export declare const qualifier: {x: '42'};
export const x = qualifier.x;";

            string rootSpec = @"
export const r = $.withQualifier({x: '42'}).x; // '42'
";
            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec("root.dsc", rootSpec)
                .AddSpec("spec1.dsc", spec1)
                .RootSpec("root.dsc")
                .EvaluateExpressionWithNoErrors("r");

            Assert.Equal("42", result);
        }

        [Fact]
        public void EvaluateModuleWithDifferentFilesAndNestedNamespaces()
        {
            string spec1 =
@"export declare const qualifier: {x: '42', y: '36'};
export namespace X {
  export const v = Y.Z.withQualifier({z: '5'}).r;
}";

            string spec2 =
@"export namespace Y {
   export namespace Z {
      export declare const qualifier: {z: '5'};
      export const r = qualifier.z;
   }
}";

            string rootSpec = @"
export const r = X.v; // '5'
";
            var result = BuildLegacyConfigurationWithPrelude()
                .Qualifier("{x: '42', y: '36'}")
                .AddSpec("root.dsc", rootSpec)
                .AddSpec("spec1.dsc", spec1)
                .AddSpec("spec2.dsc", spec2)
                .RootSpec("root.dsc")
                .EvaluateExpressionsWithNoErrors("r");

            Assert.Equal("5", result["r"]);
        }

        [Fact]
        public void EvaluateModuleWithDifferentFilesThatIsUsedFromAnotherModule()
        {
            string spec1 =
@"export namespace X {
  export declare const qualifier: {x: '42'|'1', y: '36'|'1'};
  @@public
  export const v = qualifier.x;
}";

            string spec2 =
@"export namespace X {
   // the qualifier defined in a different file
   @@public
   export const r = qualifier.y;
}";

            string rootSpec = @"
const x = X.withQualifier({x: '42', y: '36'});
@@public
export const r1 = x.v; // '42'
@@public
export const r2 = x.r; // '36'
";

            string appSpec = @"
import * as pack from 'Pack1';
export const r1 = pack.r1; // '42'
export const r2 = pack.r2; // '36'

export const r3 = pack.X.withQualifier({x: '1', y: '1'}).v; // '1'
";

            string config = @"
config({
  modules: globR(d`.`, 'package.config.dsc')
});";

            // TODO: Due to some issues with our front-end logic, all the projects need to be listed manually.
            // See work item 924822

            var result = BuildLegacyConfigurationWithPrelude(config)
                .AddSpec("Pack1/package.dsc", rootSpec)
                .AddSpec("Pack1/spec1.dsc", spec1)
                .AddSpec("Pack1/package.config.dsc", CreatePackageConfig("Pack1", true, "package.dsc", "spec1.dsc", "spec2.dsc"))
                .AddSpec("Pack1/spec2.dsc", spec2)
                .AddSpec("App/package.config.dsc", CreatePackageConfig("App", true, "package.dsc"))
                .AddSpec("App/package.dsc", appSpec)
                .RootSpec("App/package.dsc")
                .EvaluateExpressionsWithNoErrors("r1", "r2", "r3");

            Assert.Equal("42", result["r1"]);
            Assert.Equal("36", result["r2"]);
            Assert.Equal("1", result["r3"]);
        }

        [Fact]
        public void EvaluateModuleWithDifferentFilesThatIsUsedFromAnotherModuleWithoutNamepsace()
        {
            string pack1 =
@"export declare const qualifier: {x: '42'|'1'};
  @@public
  export const v = qualifier.x;";

            string appSpec = @"
import * as pack from 'Pack1';
export const r = pack.withQualifier({x: '1'}).v; // '1'
";

            string config = @"
config({
  modules: globR(d`.`, 'package.config.dsc')
});";

            var result = BuildLegacyConfigurationWithPrelude(config)
                .AddSpec("Pack1/package.dsc", pack1)
                .AddSpec("Pack1/package.config.dsc", CreatePackageConfig("Pack1", true, "package.dsc"))
                .AddSpec("App/package.config.dsc", CreatePackageConfig("App", true, "package.dsc"))
                .AddSpec("App/package.dsc", appSpec)
                .RootSpec("App/package.dsc")
                .EvaluateExpressionWithNoErrors("r");

            Assert.Equal("1", result);
        }

        // This is a good candidate for documentation, because it shows (potential) pattern that can be used in V2 evaluation model.
        [Fact]
        public void UseQualifierTypeImportedViaImportFrom()
        {
            // Please note, that there is no rules right now that makes sure, that QualifierType is actually used as a qualifier type.
            string sdkSpec =
@"export namespace Sdk {
  @@public
  export interface QualifierType extends Qualifier {x: '42' | '1'; y: '36' | '1';};

  export declare const qualifier: QualifierType;
}";

            string appSpec = @"
import * as sdk from 'Sdk';
export declare const qualifier: sdk.Sdk.QualifierType;
export const r = qualifier.x; // '42'
";

            string config = @"
config({
  modules: globR(d`.`, 'package.config.dsc')
});";

            var result = BuildLegacyConfigurationWithPrelude(config)
                .Qualifier("{x: '42', y: '1'}")
                .AddSpec("Sdk/package.dsc", sdkSpec)
                .AddSpec("Sdk/package.config.dsc", V2Module("Sdk"))
                .AddSpec("appSpec.dsc", appSpec)
                .RootSpec("appSpec.dsc")
                .EvaluateExpressionsWithNoErrors("r");

            Assert.Equal("42", result["r"]);
        }
    }
}
