// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.ProjectGraph;
using BuildXL.FrontEnd.Utilities.GenericProjectGraphResolver;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.FrontEnd.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit.Abstractions;
using static Test.BuildXL.TestUtilities.TestEnv;

namespace Test.DScript.Ast.Scheduling
{
    /// <summary>
    /// Base class for tests that programmatically add projects and verify the corresponding scheduled process
    /// done by a resolver
    /// </summary>
    /// <remarks>
    /// Meant to be used in conjunction with <see cref="ProjectBuilder{TProject, TResolverSettings}"/>
    /// No pips are run by this class, the engine phase is set to <see cref="EnginePhases.Schedule"/>
    /// </remarks>
    [TestClassIfSupported(requiresWindowsBasedOperatingSystem: true)]
    public abstract class PipSchedulingTestBase<TProject, TResolverSettings> : DsTestWithCacheBase 
        where TProject : IProjectWithDependencies<TProject>
        where TResolverSettings : class, IProjectGraphResolverSettings
    {
        private readonly ModuleDefinition m_testModule;
        private readonly AbsolutePath m_configFilePath;

        protected AbsolutePath TestPath { get; }

        /// <nodoc/>
        public PipSchedulingTestBase(ITestOutputHelper output, bool usePassThroughFileSystem = false) : base(output, usePassThroughFileSystem)
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

        protected override TestPipGraph GetPipGraph() => new TestPipGraph();

        protected abstract IProjectToPipConstructor<TProject> CreateProjectToPipConstructor(
            FrontEndContext context,
            FrontEndHost frontEndHost,
            ModuleDefinition moduleDefinition,
            TResolverSettings resolverSettings,
            IEnumerable<KeyValuePair<string, string>> userDefinedEnvironment,
            IEnumerable<string> userDefinedPassthroughVariables,
            IEnumerable<TProject> allProjects);

        /// <summary>
        /// Starts the addition of projects
        /// </summary>
        /// <returns></returns>
        public virtual ProjectBuilder<TProject, TResolverSettings> Start(TResolverSettings resolverSettings, QualifierId currentQualifier = default, QualifierId[] requestedQualifiers = default)
        {
            Contract.RequiresNotNull(resolverSettings);

            if (currentQualifier == default)
            {
                currentQualifier = FrontEndContext.QualifierTable.EmptyQualifierId;
            }

            if (requestedQualifiers == default)
            {
                requestedQualifiers = new QualifierId[] { FrontEndContext.QualifierTable .EmptyQualifierId };
            }

            if (resolverSettings.Name == null)
            {
                resolverSettings.SetName(resolverSettings.Kind ?? "test resolver");
            }

            return new ProjectBuilder<TProject, TResolverSettings>(this, resolverSettings, currentQualifier, requestedQualifiers);
        }

        /// <summary>
        /// Uses <see cref="CreateProjectToPipConstructor(FrontEndContext, FrontEndHost, ModuleDefinition, TResolverSettings, IEnumerable{KeyValuePair{string, string}}, IEnumerable{string})"/> 
        /// to schedule the specified projects and retrieves the result
        protected internal virtual SchedulingResult<TProject> ScheduleAll(TResolverSettings resolverSettings, IEnumerable<TProject> projects, QualifierId currentQualifier, QualifierId[] requestedQualifiers)
        {
            var moduleRegistry = new ModuleRegistry(FrontEndContext.SymbolTable);
            var frontEndFactory = CreateFrontEndFactoryForEvaluation(ParseAndEvaluateLogger);

            using (var controller = CreateFrontEndHost(GetDefaultCommandLine(), frontEndFactory, moduleRegistry, AbsolutePath.Invalid, out _, out _, requestedQualifiers))
            {
                resolverSettings.ComputeEnvironment(out var trackedEnv, out var passthroughVars, out _);

                var pipConstructor = CreateProjectToPipConstructor(
                    FrontEndContext,
                    controller,
                    m_testModule,
                    resolverSettings,
                    trackedEnv,
                    passthroughVars,
                    projects);

                var schedulingResults = new Dictionary<TProject, (bool, string, Process)>();

                foreach (var rushProject in projects)
                {
                    var result = pipConstructor.TrySchedulePipForProject(rushProject, currentQualifier);
                    schedulingResults[rushProject] = (result.Succeeded, result.Succeeded? null : result.Failure.Describe(), result.Succeeded? result.Result : null);
                }

                return new SchedulingResult<TProject>(controller.PipGraph, schedulingResults, controller.Configuration);
            }
        }

        protected static void AssertDependencyAndDependent(TProject dependency, TProject dependent, SchedulingResult<TProject> result)
        {
            XAssert.IsTrue(IsDependencyAndDependent(dependency, dependent, result));
        }

        protected static bool IsDependencyAndDependent(TProject dependency, TProject dependent, SchedulingResult<TProject> result)
        {
            // Unfortunately the test pip graph we are using doesn't keep track of dependencies/dependents. So we check there is a directory output of the dependency 
            // that is a directory input for a dependent
            var dependencyProcess = result.RetrieveSuccessfulProcess(dependency);
            var dependentProcess = result.RetrieveSuccessfulProcess(dependent);

            return dependencyProcess.DirectoryOutputs.Any(directoryOutput => dependentProcess.DirectoryDependencies.Any(directoryDependency => directoryDependency == directoryOutput));
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
                        LogsDirectory = m_configFilePath.GetParent(PathTable).GetParent(PathTable).Combine(PathTable, "Out").Combine(PathTable, "Logs"),
                        RedirectedLogsDirectory = m_configFilePath.GetParent(PathTable).GetParent(PathTable).Combine(PathTable, "Out").Combine(PathTable, "Logs")
                    }
            };
        }
    }
}
