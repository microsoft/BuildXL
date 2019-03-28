// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.DScriptV2
{
    public class InterpretPackagesWithSemanticNameResolution : SemanticBasedTests
    {
        public InterpretPackagesWithSemanticNameResolution(ITestOutputHelper output) : base(output)
        { }

        /// <summary>
        /// Default config-as-package is an explicit reference module
        /// </summary>
        protected override FrontEndConfiguration GetFrontEndConfiguration(bool isDebugged)
        {
            var conf = base.GetFrontEndConfiguration(isDebugged);
            conf.NameResolutionSemantics = NameResolutionSemantics.ExplicitProjectReferences;

            return conf;
        }

        [Fact]
        public void WorkspaceComputationFailsWhenTwoPackagesOwnTheSameSpec()
        {
            string packageConfig1 = @"
module({
    name: 'Pack1',
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
    projects: [f`Pack2/project.dsc`],
});
";

            string packageConfig2 = @"
module({
    name: 'Pack2',
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
    projects: [f`project.dsc`],
});
";

            string project = @"
export const x = 42;";

            var result =
                BuildLegacyConfigurationWithPrelude("config({modules: [f`Pack1/package.config.dsc`, f`Pack1/Pack2/package.config.dsc`, f`Sdk.Prelude/package.dsc`]});")
                .AddSpec("Pack1/package.config.dsc", packageConfig1)
                .AddSpec("Pack1/Pack2/package.config.dsc", packageConfig2)
                .AddSpec("Pack1/Pack2/project.dsc", project)
                .RootSpec("config.dsc")
                .ParseWithFirstError();

            string message = string.Join("\r\n", this.CapturedWarningsAndErrors.Select(x => x.FullMessage));
            Console.WriteLine(message);
            
            Assert.Contains("project.dsc' is owned by two modules", result.FullMessage);
            Assert.Equal(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.CannotBuildWorkspace, (global::BuildXL.FrontEnd.Core.Tracing.LogEventId)result.ErrorCode);
        }

        [Fact]
        public void WorkspaceComputationShouldntFailIfProjectsFieldHasDuplicateEntry()
        {
            // There was a bug in workspace computation that led to a nonresponsive process, when there is a duplicate file in the projects field.
            string packageConfig = @"
module({
    name: ""MyPack"",
    projects: [f`project.dsc`, f`project.dsc`, f`package.dsc`],
});";

            string project = @"
export const x = 42;";

            var result =
                BuildLegacyConfigurationWithPrelude()
                .AddSpec(Names.PackageConfigDsc, packageConfig)
                .AddSpec("package.dsc", "export const r = 42;")
                .AddSpec("project.dsc", project)
                .RootSpec("package.dsc").EvaluateExpressionWithNoErrors("r");

            Assert.Equal(42, result);
        }

        [Fact]
        public void ModuleConfigDscHasImplicitNameVisibility()
        {
            string packageConfig = @"
module({
    name: ""MyPack"",
    projects: [f`project1.dsc`, f`project2.dsc`],
});";

            string project1 = @"
export const x = 42;";

            string project2 = @"
export const y = x;";

            var result =
                BuildLegacyConfigurationWithPrelude()
                .AddSpec(Names.ModuleConfigDsc, packageConfig)
                .AddSpec("project1.dsc", project1)
                .AddSpec("project2.dsc", project2)
                .RootSpec("project2.dsc").EvaluateExpressionWithNoErrors("y");

            Assert.Equal(42, result);
        }
        
        [Fact]
        public void WorkspaceComputationFailsWhenProjectIsOutsideTheModuleRoot()
        {
            string packageConfig = @"
module({
    name: 'Pack1',
    projects: [f`../project.dsc`],
});
";
            // Ignore WarnForDeprecatedV1Modules
            IgnoreWarnings();

            var result =
                BuildLegacyConfigurationWithPrelude("config({modules: [f`Pack1/package.config.dsc`, f`Sdk.Prelude/package.config.dsc`]});")
                .AddSpec("Pack1/package.config.dsc", packageConfig)
                .AddSpec("project.dsc", "export const x = 42;")
                .RootSpec("config.dsc")
                .ParseWithFirstError();

            Assert.Contains("Project files should be physically within its module root", result.FullMessage);
            Assert.Equal(
                global::BuildXL.FrontEnd.Core.Tracing.LogEventId.CannotBuildWorkspace,
                (global::BuildXL.FrontEnd.Core.Tracing.LogEventId) result.ErrorCode);
        }
    }
}
