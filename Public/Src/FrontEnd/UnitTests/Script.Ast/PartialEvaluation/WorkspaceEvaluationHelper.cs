// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Engine;
using BuildXL.Pips;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Qualifier;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.FrontEnd.Workspaces;
using BuildXL.FrontEnd.Workspaces.Core;
using JetBrains.Annotations;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.FrontEnd.Core;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.FileSystem;
using BuildXL.FrontEnd.Sdk.Mutable;
using Test.BuildXL.TestUtilities.Xunit;
using Test.DScript.Workspaces;
using Test.DScript.Workspaces.Utilities;
using Test.BuildXL.FrontEnd.Core;
using Interpreter = BuildXL.FrontEnd.Core.FrontEndHostController;

namespace Test.DScript.Ast.PartialEvaluation
{
    public sealed class WorkspaceEvaluationHelper
    {
        public global::BuildXL.FrontEnd.Script.Tracing.Logger AstLogger { get; }
        public global::BuildXL.FrontEnd.Core.Tracing.Logger FrontEndLogger { get; }

        public IFrontEndStatistics FrontEndStatistics { get; }
        public FrontEndContext FrontEndContext { get; }
        public FrontEndEngineAbstraction Engine { get; }
        public WorkspaceTestBase WorkspaceHelper { get; }
        public AbsolutePath SrcRoot { get; }
        public AbsolutePath ObjRoot { get; }

        public PathTable PathTable => FrontEndContext.PathTable;
        public StringTable StringTable => FrontEndContext.StringTable;
        public SymbolTable SymbolTable => FrontEndContext.SymbolTable;

        public IReadOnlyList<Diagnostic> Diagnostics => FrontEndLogger.CapturedDiagnostics.Concat(AstLogger.CapturedDiagnostics).ToList();

        public WorkspaceEvaluationHelper(string testOutputDirectory, FrontEndContext context = null, bool forTesting = false)
        {
            AstLogger = global::BuildXL.FrontEnd.Script.Tracing.Logger.CreateLogger(preserveLogEvents: true);
            FrontEndLogger = global::BuildXL.FrontEnd.Core.Tracing.Logger.CreateLogger(preserveLogEvents: true);

            var pathTable = new PathTable();
            var pathBasedFileSystem = new InMemoryFileSystem(pathTable);
            FrontEndContext = context ?? FrontEndContext.CreateInstanceForTesting(pathTable: pathTable, fileSystem: pathBasedFileSystem);

            FrontEndStatistics = new FrontEndStatistics();
            Engine = new BasicFrontEndEngineAbstraction(
                FrontEndContext.PathTable,
                FrontEndContext.FileSystem
                );
            WorkspaceHelper = new WorkspaceTestBase(
                PathTable,
                FrontEndHost.PreludeModuleName,
                NameResolutionSemantics.ImplicitProjectReferences,
                cancellationToken: FrontEndContext.CancellationToken);
            SrcRoot = AbsolutePath.Create(PathTable, Path.Combine(testOutputDirectory, "src"));
            ObjRoot = AbsolutePath.Create(PathTable, Path.Combine(testOutputDirectory, "obj"));
        }

        /// <summary>
        /// Builds and returns a <see cref="Workspace"/> from a given module repository (<see cref="ModuleRepository"/>).
        ///
        /// Any errors can be retrieved via the <see cref="CreateTestResult"/> method.
        /// </summary>
        public Task<Workspace> ParseNoErrorCheckAsync(ModuleRepository repo)
        {
            return WorkspaceHelper.CreateSematicWorkspaceFromContent(initialModule: null, moduleRepositoryArray: new[] { repo });
        }

        /// <summary>
        /// Builds and returns a <see cref="Workspace"/> from a given module repository (<see cref="ModuleRepository"/>).
        /// Before returning, asserts that the built workspace contains no failures.
        /// </summary>
        public async Task<Workspace> ParseAsync(ModuleRepository repo)
        {
            var workspace = await ParseNoErrorCheckAsync(repo);
            WorkspaceTestBase.AssertNoWorkspaceFailures(workspace);
            return workspace;
        }

        /// <summary>
        /// Typechecks a given <paramref name="workspace"/> (which must support
        /// semantic model, i.e., be of type <see cref="SemanticWorkspace"/>).
        /// </summary>
        public ISemanticModel Typecheck(Workspace workspace)
        {
            var semanticModel = workspace.GetSemanticModel();
            WorkspaceTestBase.AssertNoSemanticErrors(semanticModel);
            return semanticModel;
        }

        /// <summary>
        /// Calls <see cref="ConvertNoErrorCheckAsync(Workspace, PipGraph)"/> then asserts that no errors have been recorded.
        /// </summary>
        public async Task<Interpreter> ConvertAsync(Workspace workspace, [CanBeNull] PipGraph oldPipGraph)
        {
            var result = await ConvertNoErrorCheckAsync(workspace, oldPipGraph);
            XAssert.IsFalse(result.HasError, "Conversion failed: " + result);
            return result.Result;
        }

        /// <summary>
        /// Conceptually, converts a given <paramref name="workspace"/> into "evaluation AST" (which can next be evaluated/interpreted).
        ///
        /// In reality, this "evaluation AST" is so tightly coupled with the engine, so this method has no choice but to 
        /// create a big hairball of hosts/controllers/resolvers/contexts/configurations/etc to make evaluation possible.
        ///
        /// This method tries to bypass as much of the front-end stuff as possible.  For example, it doesn't start evaluation from 
        /// <see cref="FrontEndHost"/>, but instead it creates a single resolver (namely <see cref="DScriptSourceResolver"/>
        /// and uses that resolver directly to evaluate the AST.
        ///
        /// Any errors can be retrieved via the <see cref="CreateTestResult"/> method.
        /// </summary>
        public async Task<TestResult<Interpreter>> ConvertNoErrorCheckAsync(Workspace workspace, [CanBeNull] PipGraph oldPipGraph)
        {
            var nonPreludeModules = NonPreludeModules(workspace).ToArray();
            var moduleRegistry = new ModuleRegistry(SymbolTable);

            var configStringPath = Path.Combine(SrcRoot.ToString(PathTable), Names.ConfigDsc);

            var configuration = new ConfigurationImpl()
                                {
                                    FrontEnd =
                                    {
                                        EnableIncrementalFrontEnd = false,
                                        ReloadPartialEngineStateWhenPossible = false,
                                        UseSpecPublicFacadeAndAstWhenAvailable = false,
                                        ConstructAndSaveBindingFingerprint = false,
                                        UsePartialEvaluation = false,
                                    }
                                };
            var frontEndHost = FrontEndHostController.CreateForTesting(FrontEndContext, Engine, moduleRegistry, configStringPath, FrontEndLogger);
            var frontEnd = new DScriptFrontEnd(FrontEndStatistics, AstLogger, null);
            frontEnd.InitializeFrontEnd(frontEndHost, FrontEndContext, configuration);

            var resolver = (DScriptSourceResolver)frontEnd.CreateResolver(KnownResolverKind.DScriptResolverKind);
            var packages = nonPreludeModules.Select(module => CreatePackageForModule(module)).ToList();
            resolver.InitResolverForTesting("Test", packages);

            frontEndHost.InitializeResolvers(new[] { resolver });

            // convert all modules and assert it succeeds
            var convertTasks = nonPreludeModules.Select(module => frontEndHost.ConvertWorkspaceToEvaluationAsync(workspace));
            await Task.WhenAll(convertTasks);

            // prepare for evaluation
            var graphBuilder = new PipGraph.Builder(
                new PipTable(PathTable, SymbolTable, initialBufferSize: 16, maxDegreeOfParallelism: Environment.ProcessorCount, debug: false),
                new EngineContext(CancellationToken.None, PathTable, SymbolTable, new QualifierTable(PathTable.StringTable), FrontEndContext.FileSystem, new TokenTextTable()),
                global::BuildXL.Scheduler.Tracing.Logger.Log,
                FrontEndContext.LoggingContext,
                new ConfigurationImpl(),
                new MountPathExpander(PathTable));

            IPipGraph pipGraph = oldPipGraph != null
                ? new PatchablePipGraph(oldPipGraph.DataflowGraph, oldPipGraph.PipTable, graphBuilder, maxDegreeOfParallelism: Environment.ProcessorCount)
                : (IPipGraph)graphBuilder;

            frontEndHost.SetState(Engine, pipGraph, configuration);

            return new TestResult<Interpreter>(frontEndHost, Diagnostics);
        }

        /// <summary>
        /// Evaluates a given <paramref name="workspace"/> using a given <paramref name="interpreter"/>.
        /// The result of the evaluation is a <see cref="PipGraph"/>.
        /// </summary>
        public async Task<PipGraph> EvaluateAsync(Workspace workspace, Interpreter interpreter)
        {
            var testResult = await EvaluateNoErrorCheckAsync(workspace, interpreter);
            XAssert.IsFalse(testResult.HasError, "Evaluation failed");
            var pipGraphBuilder = (IPipGraphBuilder)interpreter.PipGraph;
            return pipGraphBuilder.Build();
        }

        /// <summary>
        /// Evaluates a given <paramref name="workspace"/> using a given <paramref name="interpreter"/>
        /// without checking for errors or building pip graph.  Any errors can be retrieved 
        /// via the <see cref="CreateTestResult"/> method.
        /// </summary>
        public async Task<TestResult<Interpreter>> EvaluateNoErrorCheckAsync(Workspace workspace, Interpreter interpreter)
        {
            var emptyQualifierId = interpreter.FrontEndContext.QualifierTable.EmptyQualifierId;
            interpreter.SetWorkspaceForTesting(workspace);
            var evaluationTasks = NonPreludeModules(workspace).Select(m => interpreter.EvaluateAsync(
                EvaluationFilter.Empty,
                emptyQualifierId));
            await Task.WhenAll(evaluationTasks);
            return new TestResult<Interpreter>(interpreter, Diagnostics);
        }

        /// <summary>
        /// Performs all the steps necessary to evaluate a workspace (specified as a <see cref="ModuleRepository"/>
        /// and build a <see cref="PipGraph"/>.  The steps are: 
        ///     <see cref="ParseAsync"/>, 
        ///     <see cref="TypeCheck"/>,
        ///     <see cref="ConvertAsync"/>, 
        ///     <see cref="EvaluateAsync(Workspace, DScriptSourceResolver)"/>.
        /// </summary>
        public async Task<PipGraph> EvaluateAsync(ModuleRepository repo)
        {
            var workspace = await ParseAsync(repo);
            var semanticModel = Typecheck(workspace);
            var interpreter = await ConvertAsync(workspace, oldPipGraph: null);
            return await EvaluateAsync(workspace, interpreter);
        }

        /// <summary>
        /// Performs all the same steps as <see cref="EvaluateAsync(ModuleRepository)"/> except that it
        /// patches the graph between the <see cref="ConvertAsync"/> and <see cref="EvaluateAsync"/> steps.
        /// </summary>
        public async Task<PipGraph> EvaluateWithGraphPatchingAsync(ModuleRepository repo, PipGraph oldPipGraph,
            IEnumerable<AbsolutePath> changedSpecs, [CanBeNull] IEnumerable<AbsolutePath> specsToIgnore)
        {
            var workspace = await ParseAsync(repo);
            var semanticModel = Typecheck(workspace);
            var interpreter = await ConvertAsync(workspace, oldPipGraph);

            var pipGraphBuilder = interpreter.PipGraph;
            pipGraphBuilder.PartiallyReloadGraph(new HashSet<AbsolutePath>(changedSpecs));
            pipGraphBuilder.SetSpecsToIgnore(specsToIgnore);

            return await EvaluateAsync(workspace, interpreter);
        }

        /// <summary>
        /// Creates and returns a new <see cref="ModuleRepository"/> preloaded with prelude.
        /// </summary>
        public ModuleRepository NewModuleRepoWithPrelude()
        {
            return new ModuleRepository(PathTable, SrcRoot)
                .AddContent(
                    FrontEndHost.PreludeModuleName,
                    File.ReadAllText("Libs/lib.core.d.ts"),
                    File.ReadAllText("Libs/Prelude.AmbientHacks.ts"),
                    File.ReadAllText("Libs/Prelude.IO.ts"),
                    File.ReadAllText("Libs/Prelude.Context.ts"),
                    File.ReadAllText("Libs/Prelude.Contract.ts"),
                    File.ReadAllText("Libs/Prelude.Transformer.Arguments.ts"))
                .AddContent(
                    "Sdk.Transformers",
                    File.ReadAllText("Libs/Sdk.Transformers.ts"));
        }

        private Package CreatePackageForModule(ParsedModule module)
        {
            var path = module.Definition.Root.Combine(PathTable, "package.config.dsc");
            var package = Package.Create(
                PackageId.Create(StringTable, module.Descriptor.Name),
                path,
                new PackageDescriptor { Name = module.Descriptor.Name },
                parsedProjects: module.PathToSpecs);
            package.ModuleId = module.Descriptor.Id;
            return package;
        }

        private static IEnumerable<ParsedModule> NonPreludeModules(Workspace workspace) => workspace.SpecModules;
    }
}
