// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using BuildXL.FrontEnd.MsBuild;
using BuildXL.FrontEnd.MsBuild.Serialization;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Utilities.GenericProjectGraphResolver;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.TestUtilities.Xunit;
using Test.DScript.Ast.Scheduling;
using Xunit.Abstractions;
using ProjectWithPredictions = BuildXL.FrontEnd.MsBuild.Serialization.ProjectWithPredictions<BuildXL.Utilities.Core.AbsolutePath>;

namespace Test.BuildXL.FrontEnd.MsBuild.Infrastructure
{
    /// <summary>
    /// Base class for tests that programmatically add projects and verify the corresponding scheduled process
    /// done by <see cref="MsBuildResolver"/>
    /// </summary>
    /// <remarks>
    /// Meant to be used in conjunction with <see cref="MsBuildProjectBuilder"/>
    /// No pips are run by this class, the engine phase is set to <see cref="EnginePhases.Schedule"/>
    /// </remarks>
    [TestClassIfSupported(requiresWindowsBasedOperatingSystem: true)]
    public abstract class MsBuildPipSchedulingTestBase : PipSchedulingTestBase<ProjectWithPredictions, MsBuildResolverSettings>
    {
        // Keep the paths below in sync with Public\Src\FrontEnd\UnitTests\MsBuild\Test.BuildXL.FrontEnd.MsBuild.dsc
        private AbsolutePath FullframeworkMSBuild => AbsolutePath.Create(PathTable, TestDeploymentDir)
            .Combine(PathTable, "msbuild")
            .Combine(PathTable, "net472")
            .Combine(PathTable, "MSBuild.exe");
        private AbsolutePath DotnetCoreMSBuild => AbsolutePath.Create(PathTable, TestDeploymentDir)
            .Combine(PathTable, "msbuild")
            .Combine(PathTable, "dotnetcore")
            .Combine(PathTable, "MSBuild.dll");
        private AbsolutePath DotnetExe => AbsolutePath.Create(PathTable, TestDeploymentDir)
            .Combine(PathTable, "dotnet")
            .Combine(PathTable, OperatingSystemHelper.IsUnixOS ? "dotnet" : "dotnet.exe");

        /// <nodoc/>
        public MsBuildPipSchedulingTestBase(ITestOutputHelper output, bool usePassThroughFileSystem = false) : base(output, usePassThroughFileSystem)
        {
        }

        /// <summary>
        /// Starts the addition of projects
        /// </summary>
        /// <returns></returns>
        public override ProjectBuilder<ProjectWithPredictions, MsBuildResolverSettings> Start(
            MsBuildResolverSettings resolverSettings = null, 
            QualifierId currentQualifier = default, 
            QualifierId[] requestedQualifiers = default)
        {
            var settings = resolverSettings ?? new MsBuildResolverSettings();
            // Make sure the Root is set
            if (settings.Root == AbsolutePath.Invalid)
            {
                settings.Root = AbsolutePath.Create(PathTable, TestRoot);
            }

            return base.Start(settings, currentQualifier, requestedQualifiers);
        }

        /// <summary>
        /// Helper method to create a project with predictions rooted at the test root
        /// </summary>
        /// <returns></returns>
        public ProjectWithPredictions CreateProjectWithPredictions(
            string projectName = null, 
            IReadOnlyCollection<AbsolutePath> inputs = null, 
            IReadOnlyCollection<AbsolutePath> outputs = null, 
            IEnumerable<ProjectWithPredictions> references = null,
            GlobalProperties globalProperties = null,
            PredictedTargetsToExecute predictedTargetsToExecute = null,
            bool implementsTargetProtocol = true)
        {
            var projectNameRelative = RelativePath.Create(StringTable, projectName ?? "testProj.proj");

            // We need to simulate the project comes from MSBuild with /graph
            var properties = new Dictionary<string, string>(globalProperties ?? GlobalProperties.Empty);
            properties[PipConstructor.s_isGraphBuildProperty] = "true";

            var projectWithPredictions = new ProjectWithPredictions(
                TestPath.Combine(PathTable, projectNameRelative), 
                implementsTargetProtocol,
                new GlobalProperties(properties), 
                inputs ?? CollectionUtilities.EmptyArray<AbsolutePath>(), 
                outputs ?? CollectionUtilities.EmptyArray<AbsolutePath>(), 
                projectReferences: references?.ToArray() ?? null,
                predictedTargetsToExecute: predictedTargetsToExecute ?? PredictedTargetsToExecute.Create(new[] { "Build" }));

            return projectWithPredictions;
        }

        protected override IProjectToPipConstructor<ProjectWithPredictions> CreateProjectToPipConstructor(
            FrontEndContext context,
            FrontEndHost frontEndHost,
            ModuleDefinition moduleDefinition,
            MsBuildResolverSettings resolverSettings,
            IEnumerable<KeyValuePair<string, string>> userDefinedEnvironment,
            IEnumerable<string> userDefinedPassthroughVariables,
            IEnumerable<ProjectWithPredictions> allProjects)
        {
            return new PipConstructor(
                   context,
                   frontEndHost,
                   moduleDefinition,
                   resolverSettings,
                   resolverSettings.ShouldRunDotNetCoreMSBuild() ? DotnetCoreMSBuild : FullframeworkMSBuild,
                   resolverSettings.ShouldRunDotNetCoreMSBuild() ? DotnetExe : AbsolutePath.Invalid,
                   nameof(MsBuildFrontEnd),
                   userDefinedEnvironment,
                   userDefinedPassthroughVariables,
                   allProjects);
        }

        protected override SchedulingResult<ProjectWithPredictions> ScheduleAll(
            MsBuildResolverSettings resolverSettings,
            IEnumerable<ProjectWithPredictions> projects,
            QualifierId currentQualifier,
            QualifierId[] requestedQualifiers)
        {
            foreach(var project in projects)
            {
                // Is some tests projects are not finalized. Let's do it here.
                if (!project.IsDependenciesSet)
                {
                    project.SetDependencies(CollectionUtilities.EmptyArray<ProjectWithPredictions>());
                }
            }

            return base.ScheduleAll(resolverSettings, projects, currentQualifier, requestedQualifiers);
        }
    }
}
