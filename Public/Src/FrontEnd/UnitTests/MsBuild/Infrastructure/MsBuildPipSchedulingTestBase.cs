// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Qualifier;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.MsBuild;
using BuildXL.FrontEnd.MsBuild.Serialization;
using BuildXL.FrontEnd.Sdk;
using Test.DScript.Ast;
using Test.BuildXL.FrontEnd.Core;
using Xunit.Abstractions;
using static Test.BuildXL.TestUtilities.TestEnv;
using ProjectWithPredictions = BuildXL.FrontEnd.MsBuild.Serialization.ProjectWithPredictions<BuildXL.Utilities.AbsolutePath>;

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
    public abstract class MsBuildPipSchedulingTestBase : DsTestWithCacheBase
    {
        private readonly ModuleDefinition m_testModule;
        private readonly AbsolutePath m_configFilePath;

        protected AbsolutePath TestPath { get; }

        /// <nodoc/>
        public MsBuildPipSchedulingTestBase(ITestOutputHelper output, bool usePassThroughFileSystem = false) : base(output, usePassThroughFileSystem)
        {
            TestPath = AbsolutePath.Create(PathTable, TestRoot);

            m_testModule = ModuleDefinition.CreateModuleDefinitionWithImplicitReferences(
                ModuleDescriptor.CreateForTesting("Test"),
                TestPath,
                TestPath.Combine(PathTable, "module.config.bm"),
                new[] { TestPath.Combine(PathTable, "spec.dsc") },
                allowedModuleDependencies: null,
                cyclicalFriendModules: null);

            m_configFilePath = TestPath.Combine(PathTable, "config.dsc");

            PopulateMainConfigAndPrelude();
        }

        protected override IPipGraph GetPipGraph() => new TestPipGraph();

        /// <summary>
        /// Starts the addition of projects
        /// </summary>
        /// <returns></returns>
        public MsBuildProjectBuilder Start(MsBuildResolverSettings resolverSettings = null, QualifierId currentQualifier = default, QualifierId[] requestedQualifiers = default)
        {
            var settings = resolverSettings ?? new MsBuildResolverSettings();
            // Make sure the Root is set
            if (settings.Root == AbsolutePath.Invalid)
            {
                settings.Root = AbsolutePath.Create(PathTable, TestRoot);
            }

            if (currentQualifier == default)
            {
                currentQualifier = FrontEndContext.QualifierTable.EmptyQualifierId;
            }

            if (requestedQualifiers == default)
            {
                requestedQualifiers = new QualifierId[] { FrontEndContext.QualifierTable .EmptyQualifierId };
            }

            return new MsBuildProjectBuilder(this, settings, currentQualifier, requestedQualifiers);
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

            var projectWithPredictions = new ProjectWithPredictions(
                TestPath.Combine(PathTable, projectNameRelative), 
                implementsTargetProtocol,
                globalProperties ?? GlobalProperties.Empty, 
                inputs ?? CollectionUtilities.EmptyArray<AbsolutePath>(), 
                outputs ?? CollectionUtilities.EmptyArray<AbsolutePath>(), 
                projectReferences: references?.ToArray() ?? CollectionUtilities.EmptyArray<ProjectWithPredictions>(),
                predictedTargetsToExecute: predictedTargetsToExecute ?? PredictedTargetsToExecute.Create(new[] { "Build" }));

            return projectWithPredictions;
        }

        /// <summary>
        /// Uses <see cref="MsBuildPipConstructor"/> to schedule the specified projects and retrieves the result
        internal MsBuildSchedulingResult ScheduleAll(MsBuildResolverSettings resolverSettings, IEnumerable<ProjectWithPredictions> projectsWithPredictions, QualifierId currentQualifier, QualifierId[] requestedQualifiers)
        {
            var moduleRegistry = new ModuleRegistry(FrontEndContext.SymbolTable);
            var workspaceFactory = CreateWorkspaceFactoryForTesting(FrontEndContext, ParseAndEvaluateLogger);
            var frontEndFactory = CreateFrontEndFactoryForEvaluation(workspaceFactory, ParseAndEvaluateLogger);

            using (var controller = CreateFrontEndHost(GetDefaultCommandLine(), frontEndFactory, workspaceFactory, moduleRegistry, AbsolutePath.Invalid, out _, out _, requestedQualifiers))
            {
                var pipConstructor = new PipConstructor(
                    FrontEndContext,
                    controller,
                    m_testModule,
                    resolverSettings,
                    AbsolutePath.Create(PathTable, TestDeploymentDir).Combine(PathTable, "MSBuild.exe"),
                    nameof(MsBuildFrontEnd));

                var schedulingResults = new Dictionary<ProjectWithPredictions, (bool, string, Process)>();

                foreach (var projectWithPredictions in projectsWithPredictions)
                {
                    var result = pipConstructor.TrySchedulePipForFile(projectWithPredictions, currentQualifier, out string failureDetail, out Process process);
                    schedulingResults[projectWithPredictions] = (result, failureDetail, process);
                }

                return new MsBuildSchedulingResult(PathTable, controller.PipGraph, schedulingResults);
            }
        }
        private void PopulateMainConfigAndPrelude()
        {
            FileSystem.WriteAllText(m_configFilePath, "config({});");

            var preludeDir = TestPath.Combine(PathTable, FrontEndHost.PreludeModuleName);
            FileSystem.CreateDirectory(preludeDir);
            var preludeModule = ModuleConfigurationBuilder.V1Module(FrontEndHost.PreludeModuleName, mainFile: "Prelude.dsc");
            FileSystem.WriteAllText(preludeDir.Combine(PathTable, "package.config.dsc"), preludeModule.ToString());
            FileSystem.WriteAllText(preludeDir.Combine(PathTable, "Prelude.dsc"), SpecEvaluationBuilder.FullPreludeContent);
        }

        private CommandLineConfiguration GetDefaultCommandLine()
        {
            return new CommandLineConfiguration
            {
                Startup =
                    {
                        ConfigFile = m_configFilePath,
                    },
                FrontEnd = new FrontEndConfiguration
                    {
                        ConstructAndSaveBindingFingerprint = false,
                        EnableIncrementalFrontEnd = false,
                    },
                Engine =
                    {
                        TrackBuildsInUserFolder = false,
                        Phase = EnginePhases.Schedule,
                    },
                Schedule =
                    {
                        MaxProcesses = DegreeOfParallelism
                    },
                Layout =
                    {
                        SourceDirectory = m_configFilePath.GetParent(PathTable),
                        OutputDirectory = m_configFilePath.GetParent(PathTable).GetParent(PathTable).Combine(PathTable, "Out"),
                        PrimaryConfigFile = m_configFilePath,
                        BuildEngineDirectory = TestPath.Combine(PathTable, "bin")
                    },
                Cache =
                    {
                        CacheSpecs = SpecCachingOption.Disabled
                    },
                Logging =
                    {
                        LogsDirectory = m_configFilePath.GetParent(PathTable).GetParent(PathTable).Combine(PathTable, "Out").Combine(PathTable, "Logs")
                    }
            };
        }
    }
}
