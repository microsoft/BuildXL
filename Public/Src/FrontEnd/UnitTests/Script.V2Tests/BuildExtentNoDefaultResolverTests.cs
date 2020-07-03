// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.DScriptV2
{
    public class BuildExtentNoDefaultResolverTests : BuildExtentBase
    {
        public BuildExtentNoDefaultResolverTests(ITestOutputHelper output) : base(output)
        {
        }

        protected override bool DisableDefaultSourceResolver => true; 

        protected override FrontEndConfiguration GetFrontEndConfiguration(bool isDebugged)
        {
            var config = CreateV2FrontEndConfiguration(isDebugged);
            config.EnableIncrementalFrontEnd = true;
            return config;
        }

        /// <summary>
        /// Default source resolver is disabled --> all known modules should be evaluated
        /// Concretely, the only known modules are 'Lib' and 'Sdk.Prelude', so only 'file1' and 'file2' from "Lib" should be evaluated
        /// </summary>
        [Theory]
        [InlineData(ConfigTemplateWithPackages)]
        [InlineData(ConfigTemplateWithProjects)]
        public void TestNonFilteredEvaluationWithDefaultSourceResolverDisabled(string configTemplate)
        {
            var builder = CreateBuilder(configTemplate).RootSpec("config.dsc");
            builder.EvaluateWithNoErrors();

            var pips = GetPipsWithoutModuleAndSpec();
            XAssert.AreEqual(4, pips.Length);
            AssertPipTypeCount(pips, PipType.WriteFile, 2);
            AssertPipTypeCount(pips, PipType.Value, 2);
            XAssert.ArrayEqual(new[] { "file1", "file2" }, GetOrderedValuePipValues(pips));
        }

        [Theory]
        [InlineData(ConfigTemplateWithPackages)]
        [InlineData(ConfigTemplateWithProjects)]
        public void TestFilteredEvaluationWithDefaultSourceResolverDisabled(string configTemplate)
        {
            var builder = CreateBuilder(configTemplate).RootSpec(@"lib/package.dsc");
            builder.EvaluateWithNoErrors();

            var pips = GetPipsWithoutModuleAndSpec();
            XAssert.AreEqual(4, pips.Length);
            AssertPipTypeCount(pips, PipType.WriteFile, 2);
            AssertPipTypeCount(pips, PipType.Value, 2);
            XAssert.ArrayEqual(new[] { "file1", "file2" }, GetOrderedValuePipValues(pips));
        }
    }
}
