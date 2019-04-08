// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.DScriptV2
{
    public sealed class TestNamespacesImplicitVisibility : DScriptV2Test
    {
        public TestNamespacesImplicitVisibility(ITestOutputHelper output) : base(output)
        {}

        [Fact]
        public void PreludeNamespacesAreNeverExportedAutomatically()
        {
            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec(@"Sdk.Prelude/test.dsc", @"
namespace N {
    namespace M {
        export const x = 42;
    }
}

const z = N.M.x;")
                .RootSpec(@"Sdk.Prelude/test.dsc")
                .EvaluateWithFirstError("z");

            Assert.Contains("Property 'M' does not exist on type 'typeof N'", result.FullMessage);
        }

        /// <summary>
        /// This test acts as a sort of smoke integration test. More finer-grained tests for implicitly exported namespaces can
        /// be found under TypeScript.Net test cases
        /// </summary>
        [Fact]
        public void NamespacesAreExportedAutomatically()
        {
            BuildLegacyConfigurationWithPrelude()
                .AddSpec(@"test.dsc", @"
namespace N {
    namespace M {
        export const x = 42;
    }
}

const z = N.M.x;")
                .RootSpec(@"test.dsc")
                .EvaluateExpressionWithNoErrors("z");
        }
    }
}
