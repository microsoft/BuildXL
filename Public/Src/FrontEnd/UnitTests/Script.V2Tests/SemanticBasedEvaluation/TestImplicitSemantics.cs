// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Values;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.DScriptV2
{
    public class TestImplicitSemantics : SemanticBasedTests
    {
        public TestImplicitSemantics(ITestOutputHelper output) : base(output)
        { }

        [Fact]
        public void InterpretSpecsWithImplicitVisibility()
        {
            string spec1 =
@"export namespace X {
  export const v = 42;
}";

            string spec2 =
@"export namespace Y {
   export const r = X.v;
}";
            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec("spec1.dsc", spec1)
                .AddSpec("spec2.dsc", spec2)
                .RootSpec("spec2.dsc")
                .EvaluateExpressionWithNoErrors("Y.r");
            Assert.Equal(42, result);
        }

        [Fact]
        public void InterpretSpecsWithImplicitVisibilityAndTheSameRootNamespace()
        {
            string spec0 =
@"export namespace X {
}";

            string spec1 =
@"export namespace X {
  export const v = 42;
}";

            string spec2 =
@"export namespace X {
   export const r = X.v;
}";
            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec("spec0.dsc", spec0)
                .AddSpec("spec1.dsc", spec1)
                .AddSpec("spec2.dsc", spec2)
                .RootSpec("spec2.dsc")
                .EvaluateExpressionWithNoErrors("X.r");

            Assert.Equal(42, result);
        }

        [Fact]
        public void InterpretSpecWithFiltering()
        {
            string spec1 = @"
namespace Dummy {
    export const value = 42;
}

namespace X.Y {
    export const value = 42;
}
";
            string spec2 = @"
// It is very important, that this file declares a qualifier,
// otherwise implicit dependency between this spec and the other can be introduced.
export declare const qualifier: {};
function returnsNamespace() {
    return Dummy;
}

function returnsNamespace2() {
    return X.Y;
}


function foo(x: typeof Dummy) {
    return x;
}

function bar(x: typeof X.Y) {
    return x;
}


export const r = foo(returnsNamespace());
";

            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec("spec1.dsc", spec1)
                .AddSpec("spec2.dsc", spec2)
                .RootSpec("spec2.dsc")
                .UseSerializedAst(true)
                .EvaluateExpressionWithNoErrors("r");

            // This is very important test that shows the coupling between
            // filtering, spec2spec mapping and evaluation.
            // If the namespace was just referenced, but never dereferenced,
            // then the result of evaluation is a special namespace - EmptyNamespace.
            Assert.Equal(TypeOrNamespaceModuleLiteral.EmptyInstance, result);
        }

        [Fact]
        public void InterpetWithSpecFilteringForImportedModules()
        {
            // This test case is very subtle.
            // The test case has two modules: PackageB that references PackageA.
            // PackageA has two files, but values only from one file are actually used.
            // In this case, the filtering logic filters the other file out
            // even though the only referenced entities are namespaces.
            // This case caused invalid cast in the past.
            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec(@"APackage/package.config.dsc", CreatePackageConfig("APackage", useImplicitReferenceSemantics: true))
                .AddSpec(@"BPackage/package.config.dsc", CreatePackageConfig("BPackage", useImplicitReferenceSemantics: true))
                .AddSpec(@"APackage/package_2.dsc", @"
namespace X {
  @@public
  export const z = 42;
}")
                .AddSpec(@"APackage/package.dsc", @"
namespace A.B {
  @@public
  export const x = 42;
}
")
                .AddSpec(@"BPackage/package.dsc", @"
// 'A' is never fully used and referenced in the type declaration only.
// That is considered to be a kind of a weak reference between files.
// This 'weak' relationship still allows the system to filter out
// file with the namespace A.
import {A, X} from 'APackage';

function foo(x: typeof A.B) {return 42; }

export const r = X.z;
")
                .RootSpec(@"BPackage/package.dsc")
                .EvaluateExpressionWithNoErrors("r");

            Assert.Equal(42, result);
        }

        protected override bool FilterWorkspaceForConversion => true;
    }
}
