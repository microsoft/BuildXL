// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.Utilities.Collections;
using Test.DScript.Ast.DScriptV2;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Utilities.FormattableStringEx;

namespace Test.DScript.Ast.ModuleDependencies
{
    public class ModuleCycleTests : DScriptV2Test
    {
        public ModuleCycleTests(ITestOutputHelper output)
            : base(output)
        {}

        [Theory]
        [InlineData("A -> A")]
        [InlineData("A -> B -> A")]
        [InlineData("A -> B -> C -> B")]
        [InlineData("A -> B -> C -> C")]
        [InlineData("A -> A -> B -> C")]
        public void SimpleCyclesAreDetected(string moduleDependencyChain)
        {
            var diagnostic = BuildWithPrelude()
                .AddModuleDependency(moduleDependencyChain)
                .EvaluateWithFirstError();

            AssertCycleIsFound(diagnostic);
        }

        [Fact]
        public void OnlyFirstCycleIsDetectedWhenMultipleArePresent()
        {
            var diagnostics = BuildWithPrelude()
                .AddModuleDependency(
                    "A -> B -> C -> D -> A",
                    "D -> B")
                .EvaluateWithDiagnostics();

            Assert.Equal(1, diagnostics.Count);
            AssertCycleIsFound(diagnostics.First());
        }

        [Fact]
        public void DisconnectedCycleIsFound()
        {
            var diagnostic = BuildWithPrelude()
                .AddModuleDependency(
                    "A -> B -> C -> D",
                    "F -> G -> F")
                .EvaluateWithFirstError();

            AssertCycleIsFound(diagnostic);
        }

        [Fact]
        public void TwoIndependentCyclesAreFound()
        {
            var diagnostic = BuildWithPrelude()
                .AddModuleDependency(
                    "A -> B -> A",
                    "F -> G -> F")
                .EvaluateWithFirstError();

            AssertCycleIsFound(diagnostic);
        }

        private static void AssertCycleIsFound(Diagnostic diagnostic)
        {
            Assert.Contains("Module dependency graph forms a cycle", diagnostic.FullMessage);
        }
    }

    /// <nodoc/>
    internal static class ModuleCycleTestsHelper
    {
        /// <summary>
        /// Adds modules to the spec builder based on a string-based dependency chain. E.g. "A -> B -> C -> D". No actual projects are added to
        /// the modules
        /// </summary>
        internal static SpecEvaluationBuilder AddModuleDependency(this SpecEvaluationBuilder builder, params string[] dependencyChains)
        {
            var moduleChains = dependencyChains.Select(
                dependencyChain => dependencyChain.Split(new[] {"->"}, StringSplitOptions.RemoveEmptyEntries).Select(name => name.Trim()).ToArray());

            // Collect all direct dependencies for each module
            var dependencies = new MultiValueDictionary<string, string>();
            foreach (var modules in moduleChains)
            {
                // We expect at least one dependency pair
                Contract.Assert(modules.Length >= 2);

                for (var i = 0; i < modules.Length - 1; i++)
                {
                    dependencies.Add(modules[i], modules[i + 1]);
                }

                // We add the last module with no dependencies so the set of modules is complete
                dependencies.Add(modules[modules.Length - 1]);
            }

            // Add one module per module name, with the corresponding allowed dependencies
            foreach (var moduleWithDependencies in dependencies)
            {
                var moduleName = moduleWithDependencies.Key;
                builder.AddFile(
                    I($"{moduleName}/package.config.dsc"),
                    DsTest.CreatePackageConfig(moduleName, useImplicitReferenceSemantics: true, allowedDependencies: moduleWithDependencies.Value.ToList()));
            }

            // We always want to evaluate everything
            builder.RootSpec(Names.ConfigBc);

            return builder;
        }
    }
}
