// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.DScriptV2
{
    public sealed class LegacyBuildExtentTests : BuildExtentBase
    {
        public LegacyBuildExtentTests(ITestOutputHelper output) : base(output)
        {
        }

        /// <summary>
        /// Legacy DScript (Office flag) is turned on
        /// </summary>
        protected override FrontEndConfiguration GetFrontEndConfiguration(bool isDebugged)
        {
            var result = base.GetFrontEndConfiguration(isDebugged);
            result.UseLegacyOfficeLogic = true;

            return result;
        }

        /// <summary>
        /// This is an Office-related test. Default source resolver is not disabled --> only modules from it should be evaluated and only what is necessary from the imported modules
        /// Concretely, only 'copy1' must be evaluated, and for it only 'file1' is necessary ('file2' should not be evaluated)
        /// </summary>
        [Theory]
        [InlineData(ConfigTemplateWithPackages)]
        [InlineData(ConfigTemplateWithProjects)]
        public void TestNonFilteredLegacyEvaluation(string configTemplate)
        {
            var builder = CreateBuilder(configTemplate, disableDefaultSourceResolver: false).RootSpec("config.dsc");
            builder.EvaluateWithNoErrors();

            var pips = GetPipsWithoutModuleAndSpec();
            XAssert.AreEqual(4, pips.Count());
            AssertPipTypeCount(pips, PipType.WriteFile, 1);
            AssertPipTypeCount(pips, PipType.CopyFile, 1);
            AssertPipTypeCount(pips, PipType.Value, 2);
            XAssert.ArrayEqual(new[] { "copy1", "file1" }, GetOrderedValuePipValues(pips));
        }
    }
}
