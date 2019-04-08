// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.DScript.Ast.DScriptV2;
using Xunit;
using Xunit.Abstractions;

using static Test.BuildXL.FrontEnd.Core.ModuleConfigurationBuilder;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class TestNamespaceVisibility : SemanticBasedTests
    {
        public TestNamespaceVisibility(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void TemplateInstanceShouldBeVisibleFromNamespaceWithoutExposedMembers()
        {
            var spec1 = @"
namespace X {
  export declare const template : {x: number} = {x: 42};
}

export const r = 42; // Fake value
";

            // Even though the namespace has only private value,
            // it still should be merged with the namespace X from another file.
            var spec2 = @"
namespace X {
   const v = template.x;
}
";

            var result = BuildWithPrelude()
                .AddSpec("spec1.dsc", spec1)
                .AddSpec("spec2.dsc", spec2)
                .RootSpec("spec1.dsc")
                .EvaluateExpressionWithNoErrors("r");

            Assert.Equal(42, result);
        }

        [Fact]
        public void NamespacesArePublicAlways()
        {
            BuildLegacyConfigurationWithPrelude()
                .AddSpec(@"APackage/package.config.dsc", CreatePackageConfig("APackage", useImplicitReferenceSemantics: true))
                .AddSpec(@"BPackage/package.config.dsc", CreatePackageConfig("BPackage", useImplicitReferenceSemantics: true))
                .AddSpec(@"APackage/package.dsc", @"
namespace X {}")
                .AddSpec(@"BPackage/package.dsc", @"
import * as A from ""APackage"";
// namespace X should be accessible, even though it is empty
export const y = A.X;")
                .RootSpec(@"BPackage/package.dsc")
                .ParseWithNoErrors();
        }

        [Fact]
        public void QualifierInstanceShouldBeVisibleFromNamespaceWithoutExposedMembers()
        {
            var spec1 = @"
namespace X {
  export declare const qualifier : {x: 'foo'};
}

export const r = 42; // Fake value
";

            // Even though the namespace has only private value,
            // it still should be merged with the namespace X from another file.
            var spec2 = @"
namespace X {
   const v = qualifier.x;
}
";

            var result = BuildWithPrelude()
                .AddSpec("spec1.dsc", spec1)
                .AddSpec("spec2.dsc", spec2)
                .RootSpec("spec1.dsc")
                .EvaluateExpressionWithNoErrors("r");

            Assert.Equal(42, result);
        }
    }
}
