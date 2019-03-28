// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.FrontEnd.Workspaces;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.FrontEnd.Sdk.FileSystem;
using Test.BuildXL.TestUtilities.Xunit;
using Test.DScript.Workspaces.Utilities;
using TypeScript.Net.DScript;
using Xunit;

namespace Test.DScript.Workspaces
{
    public class WorkspaceTestBase
    {
        public PathTable PathTable { get; }

        public IFileSystem FileSystem { get; }

        private string PreludeName { get; }

        private readonly NameResolutionSemantics m_nameResolutionSemantics;
        private readonly CancellationToken? m_cancellationToken;
        private static readonly string s_preludeContent = 
            File.ReadAllText("Libs/lib.core.d.ts") +
            File.ReadAllText("Libs/Prelude.IO.ts");

        public WorkspaceTestBase(
            PathTable pathTable = null,
            string preludeName = null,
            NameResolutionSemantics nameResolutionSemantics = NameResolutionSemantics.ExplicitProjectReferences,
            CancellationToken? cancellationToken = null)
        {
            PathTable = pathTable ?? new PathTable();
            FileSystem = new InMemoryFileSystem(PathTable);
            PreludeName = preludeName;
            m_nameResolutionSemantics = nameResolutionSemantics;
            m_cancellationToken = cancellationToken;
        }

        /// <summary>
        /// Gives a default file system, strictly based on the provided content,
        /// to <see cref="CreateWorkspaceProviderFromContentWithFileSystem"/>
        /// </summary>
        /// <remarks>
        /// Consider that if modules are not unique across the array, the file system will be given the union of the specs
        /// that belong to the same module in some order.
        /// </remarks>
        public WorkspaceProvider CreateWorkspaceProviderFromContent(
            bool preserveTrivia = false,
            params ModuleRepository[] moduleRepositoryArray)
        {
            var fileSystem = new ModuleRepositoryFileSystem(PathTable, moduleRepositoryArray);
            return CreateWorkspaceProviderFromContentWithFileSystem(fileSystem, cancelOnFirstFailure: true, preserveTrivia: preserveTrivia, moduleRepositoryArray: moduleRepositoryArray);
        }

        /// <summary>
        /// Creates a semantic workspace from the given moduleRepositories. If <paramref name="initialModule"/> is specified,
        /// the workspace will contain only those modules required by the <paramref name="initialModule"/> module; otherwise,
        /// the workspace will contain all known modules.
        /// </summary>
        /// <remarks>
        /// Consider that if modules are not unique across the array, the file system will be given the union of the specs
        /// that belong to the same module in some order.
        /// </remarks>
        public async Task<Workspace> CreateSematicWorkspaceFromContent(
            ModuleDescriptor? initialModule,
            bool preserveTrivia = false,
            params ModuleRepository[] moduleRepositoryArray)
        {
            var workspaceProvider = CreateWorkspaceProviderFromContent(preserveTrivia: preserveTrivia, moduleRepositoryArray: moduleRepositoryArray);
            var workspace = initialModule != null
                ? await workspaceProvider.CreateWorkspaceFromModuleAsync(initialModule.Value)
                : await workspaceProvider.CreateWorkspaceFromAllKnownModulesAsync();

            if (!workspace.Succeeded)
            {
                Assert.True(
                    false,
                    "Error building workspace:" +
                    Environment.NewLine +
                    string.Join(
                        Environment.NewLine,
                        workspace.Failures.Select(
                            f =>
                            {
                                var parseFail = f as ParsingFailure;
                                if (parseFail != null)
                                {
                                    return f.Describe() + string.Join(Environment.NewLine, parseFail.ParseDiagnostics.Select(d => d.ToString()));
                                }

                                return f.Describe();
                            })));
            }

            var sematicWorkspaceProvider = new SemanticWorkspaceProvider(workspaceProvider.Statistics, workspaceProvider.Configuration);
            var sematicWorkspace = await sematicWorkspaceProvider.ComputeSemanticWorkspaceAsync(PathTable, workspace);
            return sematicWorkspace;
        }

        /// <summary>
        /// Each element in <param name="moduleRepositoryArray"/> represents the extent of a resolver,
        /// containing module references (keys) to spec content (values). So a workspace is created accordingly: so many
        /// resolvers as array elements.
        /// </summary>
        public WorkspaceProvider CreateWorkspaceProviderFromContentWithFileSystem(
            IFileSystem fileSystem,
            bool cancelOnFirstFailure,
            bool preserveTrivia = false,
            params ModuleRepository[] moduleRepositoryArray)
        {
            var resolverSettings = new List<IResolverSettings>();
            foreach (var modulesWithContent in moduleRepositoryArray)
            {
                resolverSettings.Add(CreateResolverSettingsFromModulesWithContent(modulesWithContent, fileSystem));
            }

            var workspaceConfiguration = new WorkspaceConfiguration(
                resolverSettings,
                constructFingerprintDuringParsing: false,
                maxDegreeOfParallelismForParsing: DataflowBlockOptions.Unbounded,
                parsingOptions: ParsingOptions.DefaultParsingOptions.WithTrivia(preserveTrivia),
                maxDegreeOfParallelismForTypeChecking: 1,
                cancelOnFirstFailure: cancelOnFirstFailure,
                includePreludeWithName: PreludeName,
                cancellationToken: m_cancellationToken);

            var factory = new WorkspaceResolverFactory<IWorkspaceModuleResolver>();
            factory.RegisterResolver(KnownResolverKind.DScriptResolverKind, conf => new SimpleWorkspaceSourceModuleResolver(conf));

            var result = WorkspaceProvider.TryCreate(
                mainConfigurationWorkspace: null,
                workspaceStatistics: new WorkspaceStatistics(),
                workspaceResolverFactory: factory,
                configuration: workspaceConfiguration,
                pathTable: PathTable,
                symbolTable: new SymbolTable(),
                useDecorator: false,
                addBuiltInPreludeResolver: false,
                workspaceProvider: out var workspaceProvider,
                failures: out var failures);

            // We assume workspace provider does not fail here
            Contract.Assert(result);

            return (WorkspaceProvider) workspaceProvider;
        }

        public ModuleDefinition GetModuleDefinitionFromContent(
            ModuleDescriptor moduleDescriptor,
            ModuleRepository moduleRepository)
        {
            if (m_nameResolutionSemantics == NameResolutionSemantics.ImplicitProjectReferences && moduleDescriptor.Name != PreludeName)
            {
                var moduleConfigurationPath = moduleRepository.RootDir.Combine(moduleRepository.PathTable, Names.ModuleConfigBm);

                return ModuleDefinition.CreateModuleDefinitionWithImplicitReferences(
                    moduleDescriptor,
                    moduleRepository.RootDir,
                    moduleConfigurationPath,
                    moduleRepository.GetAllPathsForModule(moduleDescriptor),
                    allowedModuleDependencies: null,
                    cyclicalFriendModules: moduleRepository.GetAllModules().Select(
                        descriptor => ModuleReferenceWithProvenance.FromNameAndPath(descriptor.Name, moduleConfigurationPath.ToString(PathTable))));
            }

            return ModuleDefinition.CreateModuleDefinitionWithExplicitReferencesWithEmptyQualifierSpace(
                moduleDescriptor,
                moduleRepository.RootDir.Combine(moduleRepository.PathTable, "FakeMainFile.dsc"),
                moduleRepository.RootDir.Combine(moduleRepository.PathTable, Names.ModuleConfigBm),
                moduleRepository.GetAllPathsForModule(moduleDescriptor),
                PathTable);
        }

        public ModuleRepository CreateEmptyContent()
        {
            return new ModuleRepository(PathTable);
        }

        public ModuleRepository CreateWithPrelude(string preludeContent = null)
        {
            return CreateEmptyContent().AddContent("Sdk.Prelude", preludeContent ?? s_preludeContent);
        }

        /// <summary>
        /// Creates a parsing queue from a set of module names with content.
        /// </summary>
        public ModuleParsingQueue CreateParsingQueueFromContent(
            ModuleRepository[] moduleRepositoryArray,
            IFileSystem fileSystem = null)
        {
            if (fileSystem == null)
            {
                fileSystem = new ModuleRepositoryFileSystem(PathTable, moduleRepositoryArray);
            }

            var workspaceProvider = CreateWorkspaceProviderFromContentWithFileSystem(fileSystem, cancelOnFirstFailure: false, preserveTrivia:false, moduleRepositoryArray);

            return new ModuleParsingQueue(
                workspaceProvider,
                workspaceProvider.Configuration,
                new ModuleReferenceResolver(fileSystem.GetPathTable()),
                designatedPrelude: null,
                configurationModule: null);
        }

        /// <summary>
        /// Asserts that given <paramref name="workspace"/> is not null and that it
        /// contains no failures (<see cref="Workspace.Failures"/>).
        /// </summary>
        public static void AssertNoWorkspaceFailures(Workspace workspace)
        {
            XAssert.IsNotNull(workspace, "Workspace is null");
            if (!workspace.Succeeded)
            {
                XAssert.Fail(string.Join(Environment.NewLine, workspace.Failures.Select(f => f.DescribeIncludingInnerFailures())));
            }
        }

        /// <summary>
        /// Asserts that given <paramref name="semanticModel"/> is not null and that it
        /// contains no failures <see cref="ISemanticModel.GetAllSemanticDiagnostics"/>
        /// </summary>
        public static void AssertNoSemanticErrors(ISemanticModel semanticModel)
        {
            XAssert.IsNotNull(semanticModel, "Semantic model is null");
            var diagnostics = semanticModel.GetAllSemanticDiagnostics().ToList();
            if (diagnostics.Any())
            {
                XAssert.Fail(string.Join(Environment.NewLine, diagnostics.Select(d => d.ToString())));
            }
        }

        public static ParsedModule AssertSuccessAndGetFirstModule(Workspace workspace)
        {
            AssertNoWorkspaceFailures(workspace);
            XAssert.IsTrue(workspace.SpecModules.Count > 0);

            return workspace.SpecModules.First();
        }

        public static IReadOnlyCollection<ParsedModule> AssertSuccessAndGetAllModules(Workspace workspace)
        {
            AssertNoWorkspaceFailures(workspace);
            return workspace.SpecModules;
        }

        public static Failure AssertSingleFailureAndGetIt(Workspace workspace)
        {
            XAssert.AreEqual(1, workspace.Failures.Count);

            return workspace.Failures.First();
        }

        public static IReadOnlyCollection<Failure> AssertFailuresAndGetAll(Workspace workspace)
        {
            XAssert.IsFalse(workspace.Succeeded);
            return workspace.Failures;
        }

        private SimpleSourceResolverSettings CreateResolverSettingsFromModulesWithContent(
            ModuleRepository moduleRepository, IFileSystem fileSystem)
        {
            var moduleDefinitions = new Dictionary<ModuleDescriptor, ModuleDefinition>();

            foreach (var moduleName in moduleRepository.GetAllModules())
            {
                moduleDefinitions[moduleName] = GetModuleDefinitionFromContent(moduleName, moduleRepository);
            }

            return new SimpleSourceResolverSettings(moduleDefinitions, fileSystem);
        }
    }
}
