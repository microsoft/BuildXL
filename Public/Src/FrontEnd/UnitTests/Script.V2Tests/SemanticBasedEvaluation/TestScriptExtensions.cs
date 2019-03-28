// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Xunit;
using Xunit.Abstractions;

using static Test.BuildXL.FrontEnd.Core.ModuleConfigurationBuilder;

namespace Test.DScript.Ast.DScriptV2
{
    public class TestScriptExtensions : SemanticBasedTests
    {
        public TestScriptExtensions(ITestOutputHelper output) : base(output)
        { }

        [Fact]
        public void TestConfigExtension()
        {
            BuildWithPrelude().AddSpec("const x = 42;").EvaluateWithNoErrors();
        }

        [Fact]
        public void TestModuleExtension()
        {
            BuildWithPrelude()
                .AddSpec("APackage/module.config.bm", V2Module("APackage").WithProjects("spec.dsc"))
                .AddSpec("APackage/spec.dsc", "const x = 42;")
                .RootSpec("APackage/spec.dsc")
                .EvaluateWithNoErrors();
        }

        [Fact]
        public void TestProjectExtension()
        {
            BuildWithPrelude()
                .AddSpec("APackage/module.config.bm", 
                    V2Module("APackage").WithProjects("spec.bp", "spec.bxt"))
                .AddSpec("APackage/spec.bp", "const x = 42;")
                .AddSpec("APackage/spec.bxt", "function g(){}")
                .RootSpec("APackage/spec.bp")
                .EvaluateWithNoErrors();
        }

        [Fact]
        public void TestAllProjectsAreImplicitlyPickedUpByModule()
        {
            var result = BuildWithPrelude()
                .AddSpec("APackage/module.config.bm", V2Module("APackage"))
                .AddSpec("APackage/spec.bp", "const x = g(42);")
                .AddSpec("APackage/spec.bxt", "export function g(n){return n;}")
                .RootSpec("APackage/spec.bp")
                .EvaluateExpressionWithNoErrors("x");

            Assert.Equal(42, result);
        }
    }
}
