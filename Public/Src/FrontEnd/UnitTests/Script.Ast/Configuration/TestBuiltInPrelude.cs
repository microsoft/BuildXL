// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Values;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Configuration
{
    public class TestBuiltInPrelude : DsTest
    {
        public TestBuiltInPrelude(ITestOutputHelper output) : base(output)
        {}

        [Fact]
        public void EmptyConfigurationFileUsesABuiltInPrelude()
        {
            var result = BuildWithoutDefautlLibraries()
                            .Configuration(@"config({});")
                            .AddSpec("build.dsc", "const x = 42;")
                            .EvaluateExpressionWithNoErrors("x");

            Assert.Equal(42, result);
        }

        [Fact]
        public void AnExplicitPreludeTrumpsTheBuiltInPrelude()
        {
            // TestPrelude is a namespace that only exists in the prelude
            // used by the test infrastructure - and it doesn't on the built-in one
            var result = Build()
                            .AddSpec("build.dsc", "const x : TestPrelude.TestType = undefined;")
                            .EvaluateExpressionWithNoErrors("x");

            Assert.Equal(UndefinedValue.Instance, result);
        }
    }
}
