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
    public class BuildExtentTests : BuildExtentBase
    {
        public BuildExtentTests(ITestOutputHelper output) : base(output)
        {
        }

        protected override FrontEndConfiguration GetFrontEndConfiguration(bool isDebugged)
        {
            var config = CreateV2FrontEndConfiguration(isDebugged);
            config.EnableIncrementalFrontEnd = true;
            return config;
        }

        /// <summary>
        /// Since UseLegacyOfficeLogic is not enabled here, the expected behavior is that all modules defined in the config (regardless of whether they come from a default
        /// sourcer resolver or any other resolver) are part of the evaluation goals
        /// </summary>
        [Theory]
        [InlineData(ConfigTemplateWithPackages)]
        [InlineData(ConfigTemplateWithProjects)]
        public void TestNonFilteredEvaluation(string configTemplate)
        {
            var builder = CreateBuilder(configTemplate).RootSpec("config.dsc");
            builder.EvaluateWithNoErrors();

            var pips = GetPipsWithoutModuleAndSpec();
            XAssert.AreEqual(6, pips.Count());
            AssertPipTypeCount(pips, PipType.WriteFile, 2);
            AssertPipTypeCount(pips, PipType.CopyFile, 1);
            AssertPipTypeCount(pips, PipType.Value, 3);
            XAssert.ArrayEqual(new[] { "copy1", "file1", "file2" }, GetOrderedValuePipValues(pips));
        }

        [Theory]
        [InlineData(ConfigTemplateWithPackages)]
        [InlineData(ConfigTemplateWithProjects)]
        public void TestFilteredEvaluationOfExternalModuleShouldIncludeAllPipsFromThatModule(string configTemplate)
        {
            var builder = CreateBuilder(configTemplate).RootSpec(@"lib/package.dsc");
            builder.EvaluateWithNoErrors();

            var pips = GetPipsWithoutModuleAndSpec();
            XAssert.AreEqual(4, pips.Length);
            AssertPipTypeCount(pips, PipType.WriteFile, 2);
            AssertPipTypeCount(pips, PipType.Value, 2);
            XAssert.ArrayEqual(new[] { "file1", "file2" }, GetOrderedValuePipValues(pips));
        }

        /// <summary>
        /// Same as <see cref="TestNonFilteredEvaluation"/>
        /// </summary>
        [Theory]
        [InlineData(ConfigTemplateWithPackages)]
        [InlineData(ConfigTemplateWithProjects)]
        public void TestFilteredEvaluationOfOwnModuleShouldNotIncludeUnusedValuesFromExternalModules(string configTemplate)
        {
            var builder = CreateBuilder(configTemplate).RootSpec(@"src/package.dsc");

            builder.EvaluateWithNoErrors();

            var pips = GetPipsWithoutModuleAndSpec();
            XAssert.AreEqual(4, pips.Length);
            AssertPipTypeCount(pips, PipType.CopyFile, 1);
            AssertPipTypeCount(pips, PipType.WriteFile, 1);
            AssertPipTypeCount(pips, PipType.Value, 2);

            var valuePipValues = GetOrderedValuePipValues(pips);
            XAssert.ArrayEqual(valuePipValues, new[] { "copy1", "file1" });
        }
    }
}
