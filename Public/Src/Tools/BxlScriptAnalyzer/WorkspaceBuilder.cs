// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BuildXL.Engine;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Engine.Cache.Fingerprints.TwoPhase;
using BuildXL.FrontEnd.Core;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.FrontEnd.Script.RuntimeModel.AstBridge;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.FileSystem;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Pips;
using BuildXL.Scheduler.Filter;
using BuildXL.Scheduler.Graph;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Instrumentation.Common;
using TypeScript.Net.Diagnostics;
using TypeScript.Net.Parsing;
using TypeScript.Net.Scanning;
using TypeScript.Net.Types;
using CancellationToken = System.Threading.CancellationToken;
using Diagnostic = TypeScript.Net.Diagnostics.Diagnostic;

namespace BuildXL.FrontEnd.Script.Analyzer
{
    /// <summary>
    /// Standalone workspace builder that bootstraps BuildXL frontend to create a workspace.
    /// </summary>
    public static class WorkspaceBuilder
    {
        /// <summary>
        /// Tries to build a workspace that an IDE-like language service can use
        /// </summary>
        public static bool TryBuildWorkspaceForIde(
            FrontEndContext frontEndContext,
            EngineContext engineContext,
            FrontEndEngineAbstraction frontEndEngineAbstraction,
            AbsolutePath rootFolder,
            bool skipNuget,
            EventHandler<WorkspaceProgressEventArgs> progressHandler,
            out Workspace workspace,
            out FrontEndHostController controller)
        {
            Contract.Requires(frontEndEngineAbstraction != null);
            Contract.Requires(frontEndContext != null);
            Contract.Requires(engineContext != null);
            Contract.Requires(rootFolder.IsValid);

            var config = FindPrimaryConfiguration(rootFolder, frontEndEngineAbstraction, frontEndContext.PathTable);

            if (!config.IsValid)
            {
                workspace = null;
                controller = null;
                return false;
            }

            return TryBuildWorkspace(
                EnginePhases.AnalyzeWorkspace, // The IDE always wants to analyze the workspace
                frontEndContext,
                engineContext,
                config,
                EvaluationFilter.Empty, // TODO: consider passing a filter that scopes down the build to the root folder
                AbsolutePath.Invalid,
                AbsolutePath.Invalid,
                progressHandler,
                out workspace,
                out controller,
                out _,
                new WorkspaceBuilderConfiguration()
                {
                    CancelOnFirstParsingFailure = false, // We always want to do as much as we can for the IDE,
                    PublicFacadeOptimization = false, // The IDE never wants public facades to be on, since this swaps specs under the hood.
                    SaveBindingFingerprint = false, // Off for IDE.
                    SkipNuget = skipNuget,
                },
                frontEndEngineAbstraction: frontEndEngineAbstraction,
                collectMemoryAsSoonAsPossible: false);
        }

        /// <summary>
        /// Tries to build/analyze a workspace given a config and an evaluation filter
        /// </summary>
        /// <param name="phase">Engine phase, must have either <see cref="EnginePhases.ParseWorkspace"/> or <see cref="EnginePhases.AnalyzeWorkspace"/>.</param>
        /// <param name="frontEndContext">Contextual information used by BuildXL front-end.</param>
        /// <param name="engineContext">Contextual information used by BuildXL engine.</param>
        /// <param name="configFile">Path to the primary configuration file. If invalid, a lookup will be performed</param>
        /// <param name="evaluationFilter">Evaluation filter that defines the build extent to care about.</param>
        /// <param name="outputDirectory">Output directory that will be used for evaluation.</param>
        /// <param name="objectDirectory">Object directory that will be used for evaluation.</param>
        /// <param name="progressHandler">Event handler to receive workspace loading progress notifications.</param>
        /// <param name="workspace">The parsed, and possibly type-checked workspace.</param>
        /// <param name="frontEndHostController">The host controller used for computing the workspace</param>
        /// <param name="pipGraph">Resulting pip graph</param>
        /// <param name="configuration">Configuration for workspace construction</param>
        /// <param name="frontEndEngineAbstraction">The engine abstraction to use. A default one is used if not provided</param>
        /// <param name="collectMemoryAsSoonAsPossible">Flag to indicate if memory should be released as soon as possible after workspace creation</param>
        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope")]
        public static bool TryBuildWorkspace(
            EnginePhases phase,
            FrontEndContext frontEndContext,
            EngineContext engineContext,
            AbsolutePath configFile,
            EvaluationFilter evaluationFilter,
            AbsolutePath outputDirectory,
            AbsolutePath objectDirectory,
            EventHandler<WorkspaceProgressEventArgs> progressHandler,
            out Workspace workspace,
            out FrontEndHostController frontEndHostController,
            out IPipGraph pipGraph,
            WorkspaceBuilderConfiguration configuration,
            FrontEndEngineAbstraction frontEndEngineAbstraction = null,
            bool collectMemoryAsSoonAsPossible = true)
        {
            Contract.Requires((phase & (EnginePhases.ParseWorkspace | EnginePhases.AnalyzeWorkspace)) != EnginePhases.None);
            Contract.Requires(frontEndContext != null);
            Contract.Requires(engineContext != null);
            Contract.Requires(configFile.IsValid);
            Contract.Requires(evaluationFilter != null);

            workspace = null;
            frontEndHostController = null;
            pipGraph = null;

            var pathTable = engineContext.PathTable;
            var loggingContext = frontEndContext.LoggingContext;

            var commandlineConfig = GetCommandLineConfiguration(
                configuration, 
                phase, 
                configFile,
                outputDirectory,
                objectDirectory);

            BuildXLEngine.PopulateLoggingAndLayoutConfiguration(commandlineConfig, pathTable, bxlExeLocation: null);

            var statistics = new FrontEndStatistics(progressHandler);
            var frontEndControllerFactory = FrontEndControllerFactory.Create(
                mode: FrontEndMode.NormalMode,
                loggingContext: loggingContext,
                configuration: commandlineConfig,
                collector: null,
                statistics: statistics,
                collectMemoryAsSoonAsPossible: collectMemoryAsSoonAsPossible);

            var controller = frontEndControllerFactory.Create(engineContext.PathTable, engineContext.SymbolTable);
            controller.InitializeHost(frontEndContext, commandlineConfig);

            frontEndHostController = (FrontEndHostController)controller;

            // If there is an explicit engine abstraction, we set it. This is used by IDE test.
            if (frontEndEngineAbstraction != null)
            {
                frontEndHostController.SetState(frontEndEngineAbstraction, pipGraph: null, configuration: commandlineConfig);
            }

            var config = controller.ParseConfig(commandlineConfig);
            if (config == null)
            {
                return false;
            }

            IPipGraph pipGraphBuilder = null;

            using (var cache = Task.FromResult<Possible<EngineCache>>(
                new EngineCache(
                    new InMemoryArtifactContentCache(),

                    // Note that we have an 'empty' store (no hits ever) rather than a normal in memory one.
                    new EmptyTwoPhaseFingerprintStore())))
            {
                
                if (frontEndEngineAbstraction == null)
                {
                    if (phase.HasFlag(EnginePhases.Schedule))
                    {
                        var mountsTable = MountsTable.CreateAndRegister(loggingContext, engineContext, config, commandlineConfig.Startup.Properties);
                        frontEndEngineAbstraction = new FrontEndEngineImplementation(
                            loggingContext,
                            frontEndContext.PathTable,
                            config,
                            commandlineConfig.Startup,
                            mountsTable,
                            InputTracker.CreateDisabledTracker(loggingContext),
                            null,
                            null,
                            () => FileContentTable.CreateStub(),
                            5000,
                            false);

                        pipGraphBuilder = new GraphFragmentBuilder(loggingContext, engineContext, config);

                        // TODO: Think more if an analyzer wants to use the real pip graph builder.
                        //pipGraphBuilder = new PipGraph.Builder(
                        //    EngineSchedule.CreateEmptyPipTable(engineContext),
                        //    engineContext,
                        //    Scheduler.Tracing.Logger.Log,
                        //    loggingContext,
                        //    config,
                        //    mountsTable.MountPathExpander,
                        //    fingerprintSalt: config.Cache.CacheSalt,
                        //    directoryMembershipFingerprinterRules: new DirectoryMembershipFingerprinterRuleSet(config, engineContext.StringTable));
                        if (!AddConfigurationMountsAndCompleteInitialization(config, loggingContext, mountsTable))
                        {
                            return false;
                        }

                        IDictionary<ModuleId, MountsTable> moduleMountsTableMap;
                        if (!mountsTable.PopulateModuleMounts(config.ModulePolicies.Values, out moduleMountsTableMap))
                        {
                            Contract.Assume(loggingContext.ErrorWasLogged, "An error should have been logged after MountTable.PopulateModuleMounts()");
                            return false;
                        }
                    }
                    else
                    {
                        frontEndEngineAbstraction = new BasicFrontEndEngineAbstraction(frontEndContext.PathTable, frontEndContext.FileSystem, config);
                    }
                }

                using (frontEndEngineAbstraction is IDisposable ? (IDisposable)frontEndEngineAbstraction : null)
                {
                    // Attempt to build and/or analyze the workspace
                    if (!controller.PopulateGraph(
                        cache: cache,
                        graph: pipGraphBuilder,
                        engineAbstraction: frontEndEngineAbstraction,
                        evaluationFilter: evaluationFilter,
                        configuration: config,
                        startupConfiguration: commandlineConfig.Startup))
                    {
                        workspace = frontEndHostController.GetWorkspace();

                        // Error has been reported already
                        return false;
                    }

                    pipGraph = pipGraphBuilder;

                    //if (pipGraphBuilder != null)
                    //{
                    //    pipGraph = pipGraphBuilder.Build();

                    //    if (pipGraph == null)
                    //    {
                    //        // Error has been reported already.
                    //        return false;
                    //    }
                    //}
                }
            }

            Contract.Assert(frontEndHostController != null);

            workspace = frontEndHostController.GetWorkspace();

            if (phase == EnginePhases.AnalyzeWorkspace)
            {
                // If workspace construction is successful, we run the linter on all specs.
                // This makes sure the workspace will carry all the errors that will occur when running the same specs in the regular engine path
                workspace = CreateLintedWorkspace(
                    workspace,
                    frontEndContext.LoggingContext,
                    config.FrontEnd,
                    pathTable);
            }

            return true;
        }

        private static bool AddConfigurationMountsAndCompleteInitialization(IConfiguration config, LoggingContext loggingContext, MountsTable mountsTable)
        {
            // Add configuration mounts
            foreach (var mount in config.Mounts)
            {
                mountsTable.AddResolvedMount(mount, new LocationData(config.Layout.PrimaryConfigFile, 0, 0));
            }

            if (!mountsTable.CompleteInitialization())
            {
                Contract.Assume(loggingContext.ErrorWasLogged, "An error should have been logged after MountTable.CompleteInitialization()");
                return false;
            }

            return true;
        }

        private static WorkspaceBuilderConfiguration GetDefaultConfiguration()
        {
            return new WorkspaceBuilderConfiguration()
                   {
                       CancelOnFirstParsingFailure = true,
                       PublicFacadeOptimization = false,
                       SaveBindingFingerprint = true,
                       SkipNuget = false
                   };
        }

        private static CommandLineConfiguration GetCommandLineConfiguration(
            WorkspaceBuilderConfiguration configuration,
            EnginePhases phase,
            AbsolutePath configFile,
            AbsolutePath outputDirectory,
            AbsolutePath objectDirectory)
        {
            return new CommandLineConfiguration
            {
                Startup =
                {
                    ConfigFile = configFile,
                },
                FrontEnd =
                {
                    DebugScript = false,
                    PreserveFullNames = true,
                    PreserveTrivia = true, // We want to preserve comments, so let's not skip trivia
                    CancelParsingOnFirstFailure = configuration.CancelOnFirstParsingFailure,
                    UseSpecPublicFacadeAndAstWhenAvailable = configuration.PublicFacadeOptimization,
                    ConstructAndSaveBindingFingerprint = configuration.SaveBindingFingerprint,
                    NameResolutionSemantics = NameResolutionSemantics.ImplicitProjectReferences,
                    // If SkipNuget is specified, then all the packages should be on disk.
                    // Skipping nuget restore in this case.
                    UsePackagesFromFileSystem = configuration.SkipNuget,
                    
                    // Don't release workspace so that analyses can still be done if the min required phase is evaluation.
                    // TODO: Hack -- when phase Evaluate is use, then release workspace. This is for Office to be performant.
                    ReleaseWorkspaceBeforeEvaluation = !phase.HasFlag(EnginePhases.Evaluate),
                    UnsafeOptimizedAstConversion = true,
                    AllowUnsafeAmbient = true,
                },
                Engine =
                {
                    Phase = phase,
                },
                Schedule =
                {
                    UseFixedApiServerMoniker = true
                },
                Layout =
                {
                    OutputDirectory = outputDirectory,
                    ObjectDirectory = objectDirectory,
                },
                Logging =
                {
                    LogsToRetain = 0,
                },
                Cache =
                {
                    CacheSpecs = SpecCachingOption.Disabled
                }
            };
        }

        /// <summary>
        /// Builds a workspace and uses filter to find specs to evaluate.
        /// </summary>
        public static bool TryBuildWorkspaceAndCollectFilesToAnalyze(
            Tracing.Logger logger,
            PathTable pathTable,
            EnginePhases phase,
            string configFile,
            string filter,
            string outputDirectory,
            string objectDirectory,
            out Workspace workspace,
            out IPipGraph pipGraph,
            out IReadOnlyDictionary<AbsolutePath, ISourceFile> filesToAnalyze,
            out FrontEndContext context)
        {
            workspace = null;
            pipGraph = null;
            filesToAnalyze = null;

            var loggingContext = new LoggingContext("DScriptAnalyzer");
            var fileSystem = new PassThroughFileSystem(pathTable);
            var engineContext = EngineContext.CreateNew(CancellationToken.None, pathTable, fileSystem);

            context = engineContext.ToFrontEndContext(loggingContext);

            // Parse filter string into EvaluationFilter
            var evaluationFilter = EvaluationFilter.Empty;
            if (!string.IsNullOrEmpty(filter))
            {
                if (!TryGetEvaluationFilter(logger, loggingContext, engineContext, filter, out evaluationFilter))
                {
                    // Error has been reported already
                    return false;
                }
            }

            var configFilePath = AbsolutePath.Create(pathTable, configFile);


            var outputDirectoryPath = !string.IsNullOrEmpty(outputDirectory)
                ? AbsolutePath.Create(pathTable, outputDirectory)
                : AbsolutePath.Invalid;

            var objectDirectoryPath = !string.IsNullOrEmpty(objectDirectory)
                ? AbsolutePath.Create(pathTable, objectDirectory)
                : AbsolutePath.Invalid;

            // Try parsing the workspace from config and evaluation filter
            if (!TryBuildWorkspace(
                phase,
                context,
                engineContext,
                configFilePath,
                evaluationFilter,
                outputDirectoryPath,
                objectDirectoryPath,
                progressHandler: null,
                workspace: out workspace,
                frontEndHostController: out _,
                pipGraph: out pipGraph,
                configuration: GetDefaultConfiguration()))
            {
                return false;
            }

            if (phase == EnginePhases.AnalyzeWorkspace)
            {
                // Find strict subset of specs in workspace that should be analyzed
                var collectedFilesToAnalyze = CollectFilesToAnalyze(
                        workspace,
                        pathTable,
                        configFilePath,
                        evaluationFilter);

                if (collectedFilesToAnalyze.Count == 0)
                {
                    logger.ErrorFilterHasNoMatchingSpecs(loggingContext, filter);
                    return false;
                }

                filesToAnalyze = collectedFilesToAnalyze;
            }
            else
            {
                filesToAnalyze = new Dictionary<AbsolutePath, ISourceFile>();
            }

            return true;
        }

        /// <summary>
        /// Collects the files to analyze based on the evaluation filter.
        /// </summary>
        /// <remarks>
        /// The workspace was built using the transitive closure and an over approximation of the specs.
        /// For example: when passing a single spec file, the workspace loads with all specs in the module
        /// it is a part of as well as all transitive modules.
        /// This filter extracts the set of files that the filter selected so that we only analyze and
        /// optionally fix the specified specs.
        /// </remarks>
        private static IReadOnlyDictionary<AbsolutePath, ISourceFile> CollectFilesToAnalyze(
            Workspace workspace,
            PathTable pathTable,
            AbsolutePath primaryConfigFile,
            EvaluationFilter evaluationFilter)
        {
            // In this case we analyze all specs of all modules that the workspace contains
            if (!evaluationFilter.CanPerformPartialEvaluationScript(primaryConfigFile))
            {
                return workspace.SpecSources.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.SourceFile);
            }

            var result = new Dictionary<AbsolutePath, ISourceFile>();

            foreach (var moduleToResolve in evaluationFilter.ModulesToResolve)
            {
                // Skip out the prelude module since we don't support all its constructs yet.
                foreach (var module in workspace.SpecModules)
                {
                    if (string.Equals(module.Descriptor.Name, moduleToResolve.ToString(pathTable.StringTable)))
                    {
                        foreach (var kv in module.Specs)
                        {
                            result[kv.Key] = kv.Value;
                        }
                    }
                }
            }

            foreach (var specRootToResolve in evaluationFilter.ValueDefinitionRootsToResolve)
            {
                foreach (var kv in workspace.SpecSources)
                {
                    if (workspace.PreludeModule != null)
                    {
                        if (workspace.PreludeModule.Specs.ContainsKey(kv.Key))
                        {
                            // Skip out the specs that are the prelude since we don't support all its constructs yet.
                            continue;
                        }
                    }

                    if (kv.Key == specRootToResolve || kv.Key.IsWithin(pathTable, specRootToResolve))
                    {
                        result[kv.Key] = kv.Value.SourceFile;
                    }
                }
            }

            return result;
        }

        private static bool TryGetEvaluationFilter(Tracing.Logger logger, LoggingContext loggingContext, EngineContext engineContext, string filter, out EvaluationFilter evaluationFilter)
        {
            FilterParser parser = new FilterParser(
                engineContext,
                DummyPathResolver,
                filter);
            RootFilter rootFilter;
            FilterParserError error;
            if (!parser.TryParse(out rootFilter, out error))
            {
                logger.ErrorParsingFilter(loggingContext, filter, error.Position, error.Message, error.FormatFilterPointingToPosition(filter));
                evaluationFilter = null;
                return false;
            }

            evaluationFilter = rootFilter.GetEvaluationFilter(engineContext.SymbolTable, engineContext.PathTable);
            return true;
        }

        private static bool DummyPathResolver(string s, out AbsolutePath path)
        {
            // The dummy path returned must be valid
            path = new AbsolutePath(1);
            return true;
        }

        /// <summary>
        /// TODO: This is code duplicated from the configuration processor. Consider refactoring
        /// </summary>
        private static AbsolutePath FindPrimaryConfiguration(AbsolutePath rootFolder, FrontEndEngineAbstraction engine, PathTable pathTable)
        {
            return TryFindConfig(rootFolder, engine, pathTable);
        }

        private static AbsolutePath TryFindConfig(AbsolutePath startDir, FrontEndEngineAbstraction engine, PathTable pathTable)
        {
            Contract.Requires(startDir.IsValid);

            for (; startDir != AbsolutePath.Invalid; startDir = startDir.GetParent(pathTable))
            {
                var configFilename = startDir.Combine(pathTable, Names.ConfigBc);
                var legacyConfigFilename = startDir.Combine(pathTable, Names.ConfigDsc);

                if (engine.FileExists(legacyConfigFilename))
                {
                    return legacyConfigFilename;
                }

                if (engine.FileExists(configFilename))
                {
                    return configFilename;
                }
            }

            return AbsolutePath.Invalid;
        }

        /// <summary>
        /// Lints a given workspace and include any potential linter failures in the resulting one.
        /// </summary>
        public static Workspace CreateLintedWorkspace(
            Workspace workspace,
            LoggingContext loggingContext,
            IFrontEndConfiguration configuration,
            PathTable pathTable)
        {
            // We don't want to lint the Prelude specs. These are special and won't pass all rules.
            var allSpecsWithoutPrelude = workspace.SpecSources.Values
                .Where(specWithMetadata => specWithMetadata.OwningModule != workspace.PreludeModule)
                .Select(specWithMetadata => specWithMetadata.SourceFile);

            return CreateLintedWorkspaceForChangedSpecs(workspace, allSpecsWithoutPrelude, loggingContext, configuration, pathTable);
        }

        /// <summary>
        /// Lints a given set of specs that belong to a workspace and include any potential linter failures in the resulting one.
        /// </summary>
        public static Workspace CreateLintedWorkspaceForChangedSpecs(
            Workspace workspace,
            IEnumerable<ISourceFile> changedSpecsToLint,
            LoggingContext loggingContext,
            IFrontEndConfiguration configuration,
            PathTable pathTable)
        {
            var logger = BuildXL.FrontEnd.Script.Tracing.Logger.CreateLogger(preserveLogEvents: true);

            var linter = DiagnosticAnalyzer.Create(
                logger,
                loggingContext,
                new HashSet<string>(configuration.EnabledPolicyRules),
                disableLanguagePolicies: false);

            // Lint all files in parallel and wait for queue completion
            var linterQueue = new ActionBlock<ISourceFile>(
                (Action<ISourceFile>)LintFile,
                new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = configuration.MaxFrontEndConcurrency() });

            foreach (var sourceFile in changedSpecsToLint)
            {
                linterQueue.Post(sourceFile);
            }

            linterQueue.Complete();

            linterQueue.Completion.GetAwaiter().GetResult();

            // Create a workspace with the extra failures (if any) and return it
            var linterFailures = ComputeLinterFailures(workspace, logger, pathTable);
            return linterFailures.Count > 0 ? workspace.WithExtraFailures(linterFailures) : workspace;

            // Local functions
            void LintFile(ISourceFile sourceFile)
            {
                if (!sourceFile.HasDiagnostics())
                {
                    linter.AnalyzeFile(sourceFile, logger, loggingContext, pathTable, workspace);
                }
            }
        }

        private static IReadOnlyCollection<LinterFailure> ComputeLinterFailures(Workspace workspace, BuildXL.FrontEnd.Script.Tracing.Logger logger, PathTable pathTable)
        {
            // Unfortunately all linter rules log using the logger directly, so we need to retrieve the event, map it to a typescript diagnostic and add it to the
            // failure list
            var failures = new List<LinterFailure>();
            foreach (var diagnostic in logger.CapturedDiagnostics)
            {
                var path = diagnostic.Location != null ? AbsolutePath.Create(pathTable, diagnostic.Location.Value.File) : AbsolutePath.Invalid;

                if (path.IsValid && workspace.TryGetSourceFile(path, out var sourceFile))
                {
                    INode node;
                    DScriptNodeUtilities.TryGetNodeAtPosition(sourceFile, diagnostic.Location.Value.GetAbsolutePosition(sourceFile), out node);
                    Contract.Assert(node != null);

                    var startPosition = Scanner.SkipOverTrivia(sourceFile, node.Pos);

                    var typeScriptDiagnostic = new Diagnostic(
                        sourceFile,
                        startPosition,
                        node.GetWidth(),
                        diagnostic.FullMessage,
                        DiagnosticCategory.Error,
                        diagnostic.ErrorCode);

                    var descriptor = workspace.GetModuleBySpecFileName(path).Descriptor;
                    failures.Add(new LinterFailure(descriptor, sourceFile, typeScriptDiagnostic));
                }
            }

            return failures;
        }
    }
}
