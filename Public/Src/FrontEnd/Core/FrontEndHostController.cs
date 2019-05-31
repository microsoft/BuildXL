// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Engine.Cache;
using BuildXL.FrontEnd.Core.Incrementality;
using BuildXL.FrontEnd.Core.Tracing;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.Evaluation;
using BuildXL.FrontEnd.Sdk.FileSystem;
using BuildXL.FrontEnd.Workspaces;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Graph;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Qualifier;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using Newtonsoft.Json;
using TypeScript.Net.Utilities;
using static BuildXL.Utilities.FormattableStringEx;
using Diagnostic = TypeScript.Net.Diagnostics.Diagnostic;
using Logger = BuildXL.FrontEnd.Core.Tracing.Logger;
using WorkspaceStatistics = BuildXL.FrontEnd.Core.Tracing.WorkspaceStatistics;

#pragma warning disable CS0162 // Disable warning CS0162: Unreachable code detected, for the .NET Core ifdef

namespace BuildXL.FrontEnd.Core
{
    /// <summary>
    /// Front-end host.
    /// </summary>
    [SuppressMessage("Microsoft.Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix")]
    [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
    public sealed partial class FrontEndHostController : FrontEndHost, IFrontEndController
    {
        private Workspace m_buildIsCancelledWorkspace;
        private readonly ConcurrentDictionary<string, int> m_frontEndPaths = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private IInternalDefaultDScriptResolverSettings m_defaultDScriptResolverSettings;
        private readonly CycleDetectorStatistics m_cycleDetectorStatistics;

        /// <summary>
        /// Instance of the logger.
        /// </summary>
        private readonly Logger m_logger;

        /// <summary>
        /// State of host.
        /// </summary>
        public State HostState { get; private set; }

        /// <summary>
        /// Gets the workspace (if enabled).
        /// </summary>
        public new Workspace Workspace
        {
            get { return (Workspace)base.Workspace; }
            private set { base.Workspace = value; }
        }

        /// <summary>
        /// Ouptut directory.
        /// </summary>
        /// <remarks>
        /// Used by frontends to store temporary data. It is stored as a string and not as an AbsolutePath since it has to be valid
        /// whether a graph cache hit occurred or not, and therefore the path table may have changed
        /// </remarks>
        private string m_outputDirectory;

        /// <summary>
        /// Gets the BuildXL cache
        /// </summary>
        /// <remarks>
        /// This is hilariously a Task to deal with the fact that cache initialization is slow and we don't want to block any frontends on it.
        /// </remarks>
        private Task<NugetCache> m_nugetCache;

        /// <summary>
        /// Returns a logger instance.
        /// </summary>
        public Logger Logger => m_logger;

        // TODO: Change it to priority queue.
        private IResolver[] m_resolvers;

        /// <nodoc />
        public FrontEndContext FrontEndContext { get; private set; }

        /// <summary>
        /// The Factory to create frontends
        /// </summary>
        private readonly FrontEndFactory m_frontEndFactory;

        private EvaluationScheduler m_evaluationScheduler;

        private readonly IFrontEndStatistics m_frontEndStatistics;

        private readonly PerformanceCollector m_collector;

        private static readonly TimeSpan EvaluationProgressReportingPeriod = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// Constructor.
        /// </summary>
        public FrontEndHostController(
            FrontEndFactory frontEndFactory,
            DScriptWorkspaceResolverFactory workspaceResolverFactory,
            EvaluationScheduler evaluationScheduler,
            IModuleRegistry moduleRegistry,
            IFrontEndStatistics frontEndStatistics,
            Logger logger,
            PerformanceCollector collector,
            bool collectMemoryAsSoonAsPossible)
        {
            Contract.Requires(frontEndFactory != null);
            Contract.Requires(frontEndFactory.IsSealed);
            Contract.Requires(evaluationScheduler != null);
            Contract.Requires(frontEndStatistics != null);
            Contract.Requires(logger != null);
            
            m_logger = logger;

            // Temporary initialization
            m_frontEndFactory = frontEndFactory;
            m_workspaceResolverFactory = workspaceResolverFactory;
            m_evaluationScheduler = evaluationScheduler;
            ModuleRegistry = moduleRegistry;
            m_frontEndStatistics = frontEndStatistics;
            m_cycleDetectorStatistics = new CycleDetectorStatistics();

            m_collector = collector;
            HostState = State.Created;
            m_collectMemoryAsSoonAsPossible = collectMemoryAsSoonAsPossible;
            // For configuration interpretation we don't know if the cycle detector was requested to be disabled
            // So it's always on, since there could be cycles during configuration parsing when using importFrom
            // After configuration interpretation, we check the associated flag and disable it if needed.
            CycleDetector = new CycleDetector(m_cycleDetectorStatistics);
            DefaultEvaluationScheduler = EvaluationScheduler.Default;
        }

        /// <summary>
        /// Ideally, Engine should be set in the constructor but parsing the config file prevents this ideal scenario.
        /// Until we create an engine before parsing the config, the engine can't be set in the constructor above.
        /// </summary>
        internal void SetState(FrontEndEngineAbstraction engine, IPipGraph pipGraph, IConfiguration configuration)
        {
            Contract.Requires(engine != null);
            Contract.Requires(configuration != null);

            Configuration = configuration;
            m_frontEndCacheDirectory = configuration.Layout.EngineCacheDirectory;
            Engine = engine;
            FrontEndArtifactManager = CreateFrontEndArtifactManager();
            PipGraph = pipGraph;
            PipGraphFragmentManager = new PipGraphFragmentManager(LoggingContext, FrontEndContext, pipGraph);

            // TODO: The EngineBasedFileSystem should be replaced with a tracking file system that wraps the passed in filesystem
            // so that the speccache, engine caching/tracking all work for the real and for the fake filesystem.s
            if (FrontEndContext.FileSystem is PassThroughFileSystem)
            {
                FrontEndContext.SetFileSystem(new EngineFileSystem(FrontEndContext.PathTable, engine));
            }
        }

        private FrontEndArtifactManager CreateFrontEndArtifactManager()
        {
            Contract.Assert(Engine != null, "Engine != null");

            var path = m_frontEndCacheDirectory.ToString(FrontEndContext.PathTable);
            return new FrontEndArtifactManager(
                Engine,
                path,
                Logger,
                LoggingContext,
                m_frontEndStatistics,
                FrontEndContext.PathTable,
                FrontEndConfiguration,
                FrontEndContext.CancellationToken);
        }

        /// <summary>
        /// Used for testing when workspace is computed outside of this front end host controller.
        /// </summary>
        internal void SetWorkspaceForTesting(Workspace workspace)
        {
            Workspace = workspace;
        }

        /// <inheritdoc />
        void IFrontEndController.InitializeHost(FrontEndContext context, IConfiguration initialConfigurationFromCommandLine)
        {
            Contract.Requires(context != null);
            Contract.Requires(initialConfigurationFromCommandLine != null);

            Contract.Assume(HostState == State.Created);
            FrontEndContext = context;
            Configuration = initialConfigurationFromCommandLine;

            m_frontEndFactory.ConfigurationProcessor?.Initialize(this, context);

#if DEBUG
            // DScript still has some hackery for debugging that relies on a static instance.
            FrontEndContext.SetContextForDebugging(context);
#endif

            HostState = State.Initialized;
        }

        /// <inheritdoc />
        IConfiguration IFrontEndController.ParseConfig(ICommandLineConfiguration configuration)
        {
            Contract.Requires(configuration != null);
            Contract.Assume(HostState == State.Initialized);

            IConfiguration resultingConfiguration = null;

            // Seed the config environment variables with the ones from the commandline
            EnvVariablesUsedInConfig.Clear();
            foreach (var parameter in configuration.Startup.Properties)
            {
                EnvVariablesUsedInConfig.Add(parameter.Key, (false, parameter.Value));
            }

            if (!ProcessPhase(
                EnginePhases.ParseConfigFiles,
                configuration,
                m_logger.FrontEndLoadConfigPhaseStart,
                m_logger.FrontEndLoadConfigPhaseComplete,
                delegate (LoggingContext evaluateLoggingContext, ref LoadConfigurationStatistics statistics)
                {
                    var startupConfiguration = configuration.Startup;
                    var configurationProcessor = m_frontEndFactory.ConfigurationProcessor;

                    PrimaryConfigFile = configurationProcessor.FindPrimaryConfiguration(startupConfiguration);
                    if (PrimaryConfigFile.IsValid)
                    {
                        resultingConfiguration = configurationProcessor.InterpretConfiguration(PrimaryConfigFile, configuration);
                        statistics = ((ILoadConfigStatistics)configurationProcessor.GetConfigurationStatistics()).ToLoggingStatistics();
                        return resultingConfiguration != null;
                    }

                    m_logger.PrimaryConfigFileNotFound(evaluateLoggingContext);
                    resultingConfiguration = null;
                    return false;
                }))
            {
                return null;
            }

            var frontEndConcurrency = resultingConfiguration.FrontEnd.MaxFrontEndConcurrency();
            m_evaluationScheduler = new EvaluationScheduler(frontEndConcurrency, FrontEndContext.CancellationToken);

            HostState = State.ConfigInterpreted;

            Contract.Assume(resultingConfiguration.Layout.OutputDirectory.IsValid);

            // The object directory is set right after parsing the config since it is needed for computing the non-scrubbable
            // paths of the frontend controller, which is needed even when a cache graph hit occurs
            m_outputDirectory = resultingConfiguration.Layout.OutputDirectory.ToString(FrontEndContext.PathTable);

            return resultingConfiguration;
        }

        /// <summary>
        /// Internal initialization
        /// </summary>
        internal void InitializeInternal(Task<Possible<EngineCache>> cacheTask)
        {
            Contract.Requires(cacheTask != null);

            m_nugetCache = Task.Run(() =>
            {
                // Make sure that we'll log the cache initialization error only once.
                cacheTask.ContinueWith(
                    t =>
                    {
                        // Log the error if the cache initialization failed.
                        if (t.IsCompleted && !t.Result.Succeeded)
                        {
                            m_logger.CanNotRestorePackagesDueToCacheError(LoggingContext, t.Result.Failure.Describe());
                        }
                    });

                return new NugetCache(cacheTask, FrontEndContext.PathTable, LoggingContext);
            });
        }

        /// <summary>
        /// Internal initialization that allows overriding the object directory, mainly for testing purposes
        /// </summary>
        internal void InitializeInternalForTesting(Task<Possible<EngineCache>> cacheTask, AbsolutePath outputDirectory)
        {
            Contract.Requires(cacheTask != null);
            Contract.Requires(outputDirectory.IsValid);

            m_nugetCache = Task.FromResult(new NugetCache(cacheTask, FrontEndContext.PathTable, LoggingContext));
            m_outputDirectory = outputDirectory.ToString(FrontEndContext.PathTable);
        }

        /// <inheritdoc />
        bool IFrontEndController.PopulateGraph(
            Task<Possible<EngineCache>> cacheTask,
            IPipGraph graph,
            FrontEndEngineAbstraction engineAbstraction,
            EvaluationFilter evaluationFilter,
            IConfiguration configuration,
            IStartupConfiguration startupConfiguration)
        {
            Contract.Requires(cacheTask != null);
            Contract.Requires(configuration != null);
            Contract.Requires(engineAbstraction != null);
            Contract.Requires(evaluationFilter != null);
            Contract.Requires(startupConfiguration != null);

            const string MyFrontEndName = "DScript";

            InitializeInternal(cacheTask);
            SetState(engineAbstraction, graph, configuration);

            // When evaluating the config file, we did not have engine initialized that's why we could not record the env variables and track enumerated directories. We do it now.
            var envVariablesUsedInConfig = EnvVariablesUsedInConfig
                .Where(kvp => kvp.Value.valueUsed) // include only those vars that were actually used
                .Select(kvp => kvp.Key) // select variable name
                .ToList();
            engineAbstraction.RecordConfigEvaluation(envVariablesUsedInConfig, EnumeratedDirectoriesInConfig, MyFrontEndName);

            // record any files other than `PrimaryConfigFile` that may have been used to process confugratioin
            // TODO: consider extending the interface between engine and front end so that the result of the 'ParseConfigFiles'
            //       phase includes all processed config files, so that those files do not have to be explicitly reported here.
            var otherConfigSpecs = GetMainConfigWorkspace()?.ConfigurationModule?.PathToSpecs.Except(new[] {PrimaryConfigFile});
            foreach (var configSpec in otherConfigSpecs ?? CollectionUtilities.EmptyArray<AbsolutePath>())
            {
                engineAbstraction.RecordFrontEndFile(configSpec, MyFrontEndName);
            }

            // At this point the configuration is completely interpreted, so we check to see if the cycle detector needs to be disabled.
            if (FrontEndConfiguration.DisableCycleDetection())
            {
                CycleDetector.Dispose();
                CycleDetector = null;
            }

            if (!TryGetQualifiers(configuration, startupConfiguration.QualifierIdentifiers, out QualifierId[] qualifiersToEvaluate))
            {
                return false;
            }

            // Frontend and resolver initialization happens before any other phase. Frontend initialization should happen before any 
            // access to the workspace resolver factory: a frontend may trigger a workspace resolver creation with a particular setting.
            // TODO: during workspace construction the workspace resolver factory is accessed directly, which is wrong from an architecture
            // point of view. Only the corresponding frontend should manage workspace resolver creation.
            if (!ProcessPhase(
                EnginePhases.InitializeResolvers,
                configuration,
                m_logger.FrontEndInitializeResolversPhaseStart,
                m_logger.FrontEndInitializeResolversPhaseComplete,
                delegate(LoggingContext nestedLoggingContext, ref InitializeResolversStatistics statistics)
                {
                    // TODO: Use nestedLoggingContext for resolver errors
                    var success = TryInitializeFrontEndsAndResolvers(configuration, qualifiersToEvaluate);

                    statistics.ResolverCount = success ? m_resolvers.Length : 0;

                    return success;
                }))
            {
                return false;
            }

            // Parse the workspace
            if (!ProcessPhase(
                EnginePhases.ParseWorkspace,
                configuration,
                m_logger.FrontEndBuildWorkspacePhaseStart,
                m_logger.FrontEndBuildWorkspacePhaseComplete,
                delegate(LoggingContext nestedLoggingContext, ref WorkspaceStatistics statistics)
                {
                    Workspace = DoPhaseBuildWorkspace(configuration, engineAbstraction, evaluationFilter, qualifiersToEvaluate);

                    statistics.ProjectCount = Workspace.SpecCount;
                    statistics.ModuleCount = Workspace.ModuleCount;

                    return Workspace.Succeeded;
                }))
            {
                // If we need to cancel on first failure, we bail out here
                // Otherwise, we analyze the workspace anyway
                if (configuration.FrontEnd.CancelParsingOnFirstFailure())
                {
                    return false;
                }
            }

            // Spec cache is no longer needed for the front end.
            // No explicit GC.Collect calls, because we just parsed the entire world and every
            // object is still reachable. GC efficiency in this case would be very small.
            Engine.ReleaseSpecCacheMemory();

            if (!ProcessPhase(
                EnginePhases.AnalyzeWorkspace,
                configuration,
                m_logger.FrontEndWorkspaceAnalysisPhaseStart,
                m_logger.FrontEndWorkspaceAnalysisPhaseComplete,
                delegate(LoggingContext nestedLoggingContext, ref WorkspaceStatistics statistics)
                {
                    Workspace = DoPhaseAnalyzeWorkspace(configuration, Workspace);

                    statistics.ProjectCount = Workspace.AllSpecCount;
                    statistics.ModuleCount = Workspace.ModuleCount;

                    return Workspace.GetSemanticModel()?.Success() == true;
                }))
            {
                return false;
            }

            // TODO: We do not restrict the environment variables for now.
            Engine.RestrictBuildParameters(configuration.AllowedEnvironmentVariables);

            var loggingContext = FrontEndContext.LoggingContext;

            UpdateIncrementalStateAndApplyFilters(evaluationFilter);

            if (!ProcessPhase(
                EnginePhases.ConstructEvaluationModel,
                configuration,
                m_logger.FrontEndConvertPhaseStart,
                m_logger.FrontEndConvertPhaseComplete,
                delegate(LoggingContext nestedLoggingContext, ref ParseStatistics statistics)
                {
                    var result = DoPhaseConvert(evaluationFilter);

                    statistics.FileCount = result.NumberOfSpecsConverted;
                    statistics.ModuleCount = result.NumberOfModulesConverted;

                    return result.Succeeded;
                }))
            {
                return false;
            }

            if (!ProcessPhase(
                EnginePhases.Evaluate,
                configuration,
                m_logger.FrontEndEvaluatePhaseStart,
                m_logger.FrontEndEvaluatePhaseComplete,
                delegate(LoggingContext nestedLoggingContext, ref EvaluateStatistics statistics)
                {
                    // Spec change information may not be available due to corrupted engine cache, USNJournal not working, etc.
                    // When it is available, both engineAbstraction.GetChangedFiles() and GetUnchangedFiles() is guaranteed to be non-null
                    if (configuration.FrontEnd.UseGraphPatching() && engineAbstraction.IsSpecChangeInformationAvailable())
                    {
                        ReloadUnchangedPartsOfTheGraph(
                            graph,
                            changedPaths: ConvertPaths(engineAbstraction.GetChangedFiles()),
                            unchangedPaths: ConvertPaths(engineAbstraction.GetUnchangedFiles()));
                    }

                    bool evaluateSucceeded = DoPhaseEvaluate(evaluationFilter, qualifiersToEvaluate);
                    NotifyResolversEvaluationIsFinished();
                    Engine.FinishTrackingBuildParameters();

                    return evaluateSucceeded;
                }))
            {
                return false;
            }

            return true;
        }

        private void UpdateIncrementalStateAndApplyFilters(EvaluationFilter evaluationFilter)
        {
            // Save spec-2-spec map if requested.
            if (FrontEndConfiguration.FileToFileReportDestination != null)
            {
                SaveFileToFileReport(Workspace);
            }

            // This is not an incremental scenario and filter hasn't been aplied yet.
            if (FrontEndConfiguration.ConstructAndSaveBindingFingerprint())
            {
                SaveFrontEndSnapshot(new SourceFileBasedBindingSnapshot(Workspace.GetAllSourceFiles(), FrontEndContext.PathTable));
            }

            // If the workspace was not filtered yet and spec-2-spec map was constructed,
            // then we can filter workspace.
            if (!Workspace.FilterWasApplied && FrontEndConfiguration.TrackFileToFileDependencies())
            {
                // Applying filters to build a smaller workspace.
                FilterWorkspace(Workspace, evaluationFilter);
            }
        }

        internal ConversionResult DoPhaseConvert([JetBrains.Annotations.CanBeNull] EvaluationFilter evaluationFilter)
        {
            Contract.Requires(evaluationFilter != null || Workspace != null, "Evaluation filter may be null if workspace is enabled");

            return TaskUtilities.WithCancellationHandlingAsync(
                FrontEndContext.LoggingContext,
                ConvertWorkspaceToEvaluationAsync(Workspace),
                m_logger.FrontEndConvertPhaseCanceled,
                ConversionResult.Failed,
                FrontEndContext.CancellationToken).GetAwaiter().GetResult();
        }

        internal bool DoPhaseEvaluate(EvaluationFilter evaluationFilter, QualifierId[] qualifiersToEvaluate)
        {
            return TaskUtilities.WithCancellationHandlingAsync(
                FrontEndContext.LoggingContext,
                EvaluateAsync(evaluationFilter, qualifiersToEvaluate),
                m_logger.FrontEndEvaluatePhaseCanceled,
                false,
                FrontEndContext.CancellationToken).GetAwaiter().GetResult();
        }

        private void ReportWorkspaceSemanticErrorsIfNeeded(Workspace workspace)
        {
            ISemanticModel semanticModel = workspace.GetSemanticModel();
            if (semanticModel != null && !semanticModel.Success() && !workspace.IsCanceled)
            {
                LogWorkspaceSemanticErrors(semanticModel);
            }
        }

        private Workspace GetOrCreateComputationCancelledWorkspace(IWorkspaceProvider workspaceProvider)
        {
            Contract.Requires(workspaceProvider != null);

            if (m_buildIsCancelledWorkspace == null)
            {
                m_buildIsCancelledWorkspace = Workspace.Failure(workspaceProvider, workspaceProvider.Configuration, new Failure<string>("Building workspace cancelled"));
            }

            return m_buildIsCancelledWorkspace;
        }

        private IReadOnlyList<AbsolutePath> ConvertPaths(IEnumerable<string> paths)
        {
            return paths.Select(p => AbsolutePath.Create(FrontEndContext.PathTable, p)).ToList();
        }

        private void ReloadUnchangedPartsOfTheGraph(IPipGraph graph, IReadOnlyList<AbsolutePath> changedPaths, IReadOnlyList<AbsolutePath> unchangedPaths)
        {
            WorkspaceBasedSpecDependencyProvider provider = new WorkspaceBasedSpecDependencyProvider(Workspace, FrontEndContext.PathTable);

            // compute closure of dependent specs
            var affectedSpecPaths = provider.ComputeReflectiveClosureOfDependentFiles(changedPaths);

            // use all unchanged files except the closure of changed specs as 'reloaded specs' (i.e., adding pips from which should be ignored during evaluation)
            var ignoredSpecPaths = unchangedPaths.Except(affectedSpecPaths).ToList();
            graph.SetSpecsToIgnore(ignoredSpecPaths);

            // partially reload graph
            var stats = graph.PartiallyReloadGraph(affectedSpecs: affectedSpecPaths);
            Logger.GraphPartiallyReloaded(FrontEndContext.LoggingContext, stats, ignoredSpecPaths.Count);

            // log some debugging details
            Func<AbsolutePath, string> toStringFn = p => p.ToString(FrontEndContext.PathTable);
            var details = JsonConvert.SerializeObject(
                new
                {
                    ChangedSpecs = changedPaths.SelectArray(toStringFn),
                    AffectedSpecs = affectedSpecPaths.Select(toStringFn).ToArray(),
                    IgnoredSpecs = ignoredSpecPaths.SelectArray(toStringFn),
                },
                Formatting.Indented);
            Logger.GraphPatchingDetails(FrontEndContext.LoggingContext, details);
        }

        internal void NotifyResolversEvaluationIsFinished()
        {
            foreach (var resolver in m_resolvers)
            {
                resolver.NotifyEvaluationFinished();
            }
        }

        /// <inheritdoc />
        public FrontEndControllerStatistics LogStatistics(bool showSlowestElementsStatistics, bool showLargestFilesStatistics)
        {
            if (m_resolvers != null)
            {
                foreach (var resolver in m_resolvers)
                {
                    resolver.LogStatistics();
                }
            }

            LogFrontEndStatistics();
            LogCycleDetectorStatistics();

            if (showSlowestElementsStatistics)
            {
                LogSlowestElementsStatistics();
            }

            if (showLargestFilesStatistics)
            {
                LogLargestSourceFileStatistics();
            }

            return new FrontEndControllerStatistics()
            {
                IOWeight = (m_frontEndStatistics?.EndToEndParsing?.AggregateDuration != null &&
                    m_frontEndStatistics?.EndToEndParsing?.AggregateDuration.TotalMilliseconds > 0 &&
                    m_frontEndStatistics?.EndToEndTypeChecking?.AggregateDuration != null &&
                    m_frontEndStatistics?.EndToEndTypeChecking?.AggregateDuration.TotalMilliseconds > 0) ?
                    m_frontEndStatistics.EndToEndParsing.AggregateDuration.TotalMilliseconds / m_frontEndStatistics.EndToEndTypeChecking.AggregateDuration.TotalMilliseconds : 0
            };
        }
    
        private void LogSlowestElementsStatistics()
        {
            Logger.SlowestScriptElements(
                context: FrontEndContext.LoggingContext,
                parse: m_frontEndStatistics.SpecParsing.RenderSlowest,
                bind: m_frontEndStatistics.SpecBinding.RenderSlowest,
                typeCheck: m_frontEndStatistics.SpecTypeChecking.RenderSlowest,
                astConversion: m_frontEndStatistics.SpecAstConversion.RenderSlowest,
                facadeComputation: m_frontEndStatistics.PublicFacadeComputation.RenderSlowest,
                computeFingerprint: m_frontEndStatistics.SpecComputeFingerprint.RenderSlowest,
                evaluation: m_frontEndStatistics.SpecEvaluation.RenderSlowest,
                preludeProcessing: m_frontEndStatistics.PreludeProcessing.RenderSlowest);
        }

        private void LogLargestSourceFileStatistics()
        {
            Logger.LargestScriptFiles(
                context: FrontEndContext.LoggingContext,
                byIdentifierCount: m_frontEndStatistics.SourceFileIdentifiers.RenderMostHeavyWeight(),
                byLineCount: m_frontEndStatistics.SourceFileLines.RenderMostHeavyWeight(),
                byCharCount: m_frontEndStatistics.SourceFileChars.RenderMostHeavyWeight(),
                byNodeCount: m_frontEndStatistics.SourceFileNodes.RenderMostHeavyWeight(),
                bySymbolCount: m_frontEndStatistics.SourceFileSymbols.RenderMostHeavyWeight());
        }

        private void LogCycleDetectorStatistics()
        {
            Logger.CycleDetectionStatistics(
                FrontEndContext.LoggingContext,
                m_cycleDetectorStatistics.CycleDetectionThreadsCreated,
                m_cycleDetectorStatistics.CycleDetectionChainsAdded,
                m_cycleDetectorStatistics.CycleDetectionChainsRemovedBeforeProcessing,
                m_cycleDetectorStatistics.CycleDetectionChainsAbandonedWhileProcessing,
                m_cycleDetectorStatistics.CycleDetectionChainsRemovedAfterProcessing);
        }

        private void LogFrontEndStatistics()
        {
            if (m_frontEndStatistics == null)
            {
                return;
            }

            bool errorsOrWarningsWereLogged = LoggingContext.WarningWasLoggedByThisLogger || LoggingContext.ErrorWasLogged;
            if (!errorsOrWarningsWereLogged && m_frontEndStatistics.CounterWithRootCause.Count != 0)
            {
                // A very important invariant is violated: BuildXL reloaded some files, but no warnings/errors occurred
                var stackTrace = m_frontEndStatistics.CounterWithRootCause.FileReloadStackTrace;
                Contract.Assert(stackTrace != null);
                Logger.ScriptFilesReloadedWithNoWarningsOrErrors(LoggingContext, m_frontEndStatistics.CounterWithRootCause.Count, stackTrace.ToString());
            }

            var statistics = new Dictionary<string, long>
            {
                { "NuGet.RestoreDuration", (long)m_frontEndStatistics.NugetStatistics.EndToEnd.AggregateDuration.TotalMilliseconds },
                { "NuGet.SpecGenerationDuration", (long)m_frontEndStatistics.NugetStatistics.SpecGeneration.AggregateDuration.TotalMilliseconds },
                { "NuGet.Failures", m_frontEndStatistics.NugetStatistics.Failures.Count },
                { "NuGet.PackagesFromDisk", m_frontEndStatistics.NugetStatistics.PackagesFromDisk.Count },
                { "NuGet.PackagesFromCache", m_frontEndStatistics.NugetStatistics.PackagesFromCache.Count },
                { "NuGet.PackagesFromNuget", m_frontEndStatistics.NugetStatistics.PackagesFromNuget.Count },

                { "DScript.TotalNumberOfSpecsParsed", (long)m_frontEndStatistics.SpecParsing.Count },
                { "DScript.ParseDurationMs", (long)m_frontEndStatistics.EndToEndParsing.AggregateDuration.TotalMilliseconds },
                { "DScript.BindingDurationMs", (long)m_frontEndStatistics.EndToEndBinding.AggregateDuration.TotalMilliseconds },
                { "DScript.TypeCheckingDurationMs", (long)m_frontEndStatistics.EndToEndTypeChecking.AggregateDuration.TotalMilliseconds },
                { "DScript.SourceTextReloadCount", (long)m_frontEndStatistics.CounterWithRootCause.Count },
                { "DScript.AggregatedParseDurationMs", (long)m_frontEndStatistics.SpecParsing.AggregateDuration.TotalMilliseconds },
                { "DScript.AggregatedLocalBindCount", m_frontEndStatistics.SpecBinding.Count },
                { "DScript.AggregatedLocalBindDurationMs", (long)m_frontEndStatistics.SpecBinding.AggregateDuration.TotalMilliseconds },
                { "DScript.AggregatedTypeCheckingCount", (long)m_frontEndStatistics.SpecTypeChecking.Count },
                { "DScript.AggregatedTypeCheckingDurationMs", (long)m_frontEndStatistics.SpecTypeChecking.AggregateDuration.TotalMilliseconds },
                { "DScript.AggregatedAnalysisDurationMs", (long)m_frontEndStatistics.GetOverallAnalysisDuration().TotalMilliseconds },
                { "DScript.AggregatedAstConversionCount", (long)m_frontEndStatistics.SpecAstConversion.Count },
                { "DScript.AggregatedAstConversionDurationMs", (long)m_frontEndStatistics.SpecAstConversion.AggregateDuration.TotalMilliseconds },
                { "DScript.AggregatedAstSerializationDurationMs", (long)m_frontEndStatistics.SpecAstSerialization.AggregateDuration.TotalMilliseconds },
                { "DScript.AggregatedAstDeserializationDurationMs", (long)m_frontEndStatistics.SpecAstDeserialization.AggregateDuration.TotalMilliseconds },
                { "DScript.AggregatedPublicFacadeComputationDurationMs", (long)m_frontEndStatistics.PublicFacadeComputation.AggregateDuration.TotalMilliseconds },
                { "DScript.AggregatedBindingFingerprintComputationCount", (long)m_frontEndStatistics.SpecComputeFingerprint.Count },
                { "DScript.AggregatedBindingFingerprintComputationDurationMs", (long)m_frontEndStatistics.SpecComputeFingerprint.AggregateDuration.TotalMilliseconds },

                { "DScript.PublicFacadeHitsCount", (long)m_frontEndStatistics.PublicFacadeHits.Count },
                { "DScript.PublicFacadeHitsDurationMs", (long)m_frontEndStatistics.PublicFacadeHits.AggregateDuration.TotalMilliseconds },
                { "DScript.SerializedAstHitsCount", (long)m_frontEndStatistics.SerializedAstHits.Count },
                { "DScript.SerializedAstHitsDurationMs", (long)m_frontEndStatistics.SerializedAstHits.AggregateDuration.TotalMilliseconds },
                { "DScript.PublicFacadeGenerationFailures", (long)m_frontEndStatistics.PublicFacadeGenerationFailures.Count },
                { "DScript.PublicFacadeSavesCount", (long)m_frontEndStatistics.PublicFacadeSaves.Count },
                { "DScript.PublicFacadeSavesDurationMs", (long)m_frontEndStatistics.PublicFacadeSaves.AggregateDuration.TotalMilliseconds },
                { "DScript.SerializedAstSavesCount", (long)m_frontEndStatistics.AstSerializationSaves.Count },
                { "DScript.SerializedAstSavesDurationMs", (long)m_frontEndStatistics.AstSerializationSaves.AggregateDuration.TotalMilliseconds },
                { "DScript.SerializedAstBlobSize", m_frontEndStatistics.AstSerializationBlobSize.Value },
                { "DScript.SerializedPublicFacadesBlobSize", m_frontEndStatistics.PublicFacadeSerializationBlobSize.Value },
                { "DScript.TotalLineCount", m_frontEndStatistics.SourceFileLines.AggregateWeight },
                { "DScript.TotalCharCount", m_frontEndStatistics.SourceFileChars.AggregateWeight },
                { "DScript.TotalNodeCount", m_frontEndStatistics.SourceFileNodes.AggregateWeight },
                { "DScript.TotalSymbolCount", m_frontEndStatistics.SourceFileSymbols.AggregateWeight },
                { "DScript.TotalIdentifierCount", m_frontEndStatistics.SourceFileIdentifiers.AggregateWeight },
            };


            foreach (var frontEnd in m_frontEndFactory.RegisteredFrontEnds)
            {
                frontEnd.LogStatistics(statistics);
            }

            if (m_frontEndStatistics.FrontEndSnapshotSavingDuration != null)
            {
                statistics.Add(
                    "DScript.SaveFrontEndSnapshotDurationMs",
                    (long)m_frontEndStatistics.FrontEndSnapshotSavingDuration.Value.TotalMilliseconds);
            }

            if (m_frontEndStatistics.FrontEndSnapshotLoadingDuration != null)
            {
                statistics.Add(
                    "DScript.LoadFrontEndSnapshotDurationMs",
                    (long)m_frontEndStatistics.FrontEndSnapshotLoadingDuration.Value.TotalMilliseconds);
            }

            BuildXL.Tracing.Logger.Log.BulkStatistic(FrontEndContext.LoggingContext, statistics);
        }

        /// <inheritdoc/>
        /// TODO: for now all the frontend folder is flagged as non-scrubbable. Consider pushing this down to each registered resolver.
        public IReadOnlyList<string> GetNonScrubbablePaths()
        {
            return new List<string> { GetRootFrontEndFolder() };
        }

        private bool TryGetQualifiers(IConfiguration configuration, IReadOnlyList<string> requestedQualifierExpressions, out QualifierId[] qualifierIds)
        {
            var defaultQualifier = configuration.Qualifiers?.DefaultQualifier;
            var namedQualifiers = configuration.Qualifiers?.NamedQualifiers;

            qualifierIds = new QualifierId[requestedQualifierExpressions.Count];

            if (!ValidateAndRegisterConfigurationQualifiers(m_logger, FrontEndContext.LoggingContext, FrontEndContext.QualifierTable, defaultQualifier, namedQualifiers))
            {
                return false;
            }

            if (requestedQualifierExpressions.Count == 0)
            {
                // If no explicit qualifiers were requested, we look if there is a default one specified for the command line
                var startingQualifier = configuration.Qualifiers?.DefaultQualifier == null
                    ? FrontEndContext.QualifierTable.EmptyQualifierId
                    : FrontEndContext.QualifierTable.CreateQualifier(configuration.Qualifiers.DefaultQualifier);

                qualifierIds = new[] { startingQualifier };
                return true;
            }

            for (int i = 0; i < requestedQualifierExpressions.Count; ++i)
            {
                if (!TryParseQualifiers(
                    m_logger, 
                    FrontEndContext.LoggingContext, 
                    requestedQualifierExpressions[i], 
                    defaultQualifier,
                    namedQualifiers, 
                    out var qualifier))
                {
                    return false;
                }

                qualifierIds[i] = FrontEndContext.QualifierTable.CreateQualifier(qualifier);
            }

            return true;
        }

        internal static bool ValidateAndRegisterConfigurationQualifiers(
            Logger logger, 
            LoggingContext loggingContext,
            QualifierTable qualifierTable,
            IReadOnlyDictionary<string, string> defaultQualifier,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> namedQualifiers)
        {
            // Validate the configuration
            if (namedQualifiers != null)
            {
                foreach (var namedQualifier in namedQualifiers)
                {
                    var name = namedQualifier.Key;
                    var qualifier = namedQualifier.Value;
                    if (qualifier.Count == 0)
                    {
                        logger.ErrorNamedQualifierNoValues(loggingContext, name);
                        return false;
                    }

                    foreach (var kv in qualifier)
                    {
                        if (!QualifierTable.IsValidQualifierKey(kv.Key))
                        {
                            logger.ErrorNamedQualifierInvalidKey(loggingContext, name, kv.Key, kv.Value);
                            return false;
                        }
                        if (!QualifierTable.IsValidQualifierValue(kv.Value))
                        {
                            logger.ErrorNamedQualifierInvalidValue(loggingContext, name, kv.Key, kv.Value);
                            return false;
                        }
                    }

                    qualifierTable.CreateNamedQualifier(name, qualifier);
                }
            }

            if (defaultQualifier != null)
            {
                foreach (var kv in defaultQualifier)
                {
                    if (!QualifierTable.IsValidQualifierKey(kv.Key))
                    {
                        logger.ErrorDefaultQualiferInvalidKey(loggingContext, kv.Key, kv.Value);
                        return false;
                    }
                    if (!QualifierTable.IsValidQualifierValue(kv.Value))
                    {
                        logger.ErrorDefaultQualifierInvalidValue(loggingContext, kv.Key, kv.Value);
                        return false;
                    }
                }
            }

            return true;
        }

        internal static bool TryParseQualifiers(
            Logger logger,
            LoggingContext loggingContext,
            string qualifierExpression,
            IReadOnlyDictionary<string, string> defaultQualifier,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> namedQualifiers,
            out IReadOnlyDictionary<string, string> qualifier)
        {
            if (qualifierExpression.Contains(";") || qualifierExpression.Contains("="))
            {
                // Explict qualifier case
                var result = new Dictionary<string, string>();
                // If we have a default qualifier, initialize the qualifier with that value.
                if (defaultQualifier != null)
                {
                    foreach (var kv in defaultQualifier)
                    {
                        result[kv.Key] = kv.Value;
                    }
                }

                var kvs = qualifierExpression.Split(';');
                foreach (var kv in kvs)
                {
                    if (string.IsNullOrWhiteSpace(kv))
                    {
                        logger.ErrorEmptyQualfierExpresion(loggingContext, qualifierExpression);
                        qualifier = null;
                        return false;
                    }

                    var parts = kv.Split('=');
                    if (parts.Length != 2)
                    {
                        logger.ErrorIllFormedQualfierExpresion(loggingContext, qualifierExpression, kv);
                        qualifier = null;
                        return false;
                    }

                    var key = parts[0].Trim();
                    var value= parts[1].Trim();

                    if (string.IsNullOrEmpty(key))
                    {
                        logger.ErrorIllFormedQualfierExpresion(loggingContext, qualifierExpression, kv);
                        qualifier = null;
                        return false;
                    }

                    if (string.IsNullOrEmpty(value))
                    {
                        if (result.ContainsKey(key))
                        {
                            result.Remove(key);
                        }
                    }
                    else
                    {
                        result[key] = value;
                    }
                }

                if (result.Count == 0)
                {
                    logger.ErrorNoQualifierValues(loggingContext, qualifierExpression);
                    qualifier = null;
                    return false;
                }

                qualifier = result;
                return true;
            }
            else
            {
                // Named qualifier case:
                if (namedQualifiers == null)
                {
                    logger.ErrorNonExistenceNamedQualifier(
                        loggingContext,
                        new NonExistenceNamedQualifier
                        {
                            RequestedNamedQualifier = qualifierExpression,
                        });

                    qualifier = null;
                    return false;
                }

                if (!namedQualifiers.TryGetValue(qualifierExpression, out qualifier))
                {
                    logger.ErrorNotFoundNamedQualifier(
                        loggingContext,
                        new NotFoundNamedQualifier
                        {
                            RequestedNamedQualifier = qualifierExpression,
                            AvailableNamedQualifiers = string.Join(", ", namedQualifiers.Keys),
                        });

                    qualifier = null;
                    return false;
                }

                return true;
            }
        }


        private delegate bool PhaseLogicHandler<TStatistics>(LoggingContext nestedLoggingContext, ref TStatistics statistics);

        private bool ProcessPhase<TStatistics>(
            EnginePhases phase,
            IConfiguration configuration,
            Action<LoggingContext> startPhaseLogMessage,
            Action<LoggingContext, TStatistics> endPhaseLogMessage,
            PhaseLogicHandler<TStatistics> phaseLogicHandler)
            where TStatistics : IHasEndTime
        {
            var loggingContext = new LoggingContext(FrontEndContext.LoggingContext, phase.ToString());
            if (configuration.Engine.Phase.HasFlag(phase))
            {
                var statistics = default(TStatistics);

                using (var aggregator = m_collector?.CreateAggregator())
                {
                    var stopwatch = Stopwatch.StartNew();
                    startPhaseLogMessage(loggingContext);

                    m_frontEndFactory.GetPhaseStartHook(phase)();
                    var success = phaseLogicHandler(loggingContext, ref statistics);
                    m_frontEndFactory.GetPhaseEndHook(phase)();

                    LaunchDebuggerIfConfigured(phase);

                    statistics.ElapsedMilliseconds = (int)stopwatch.ElapsedMilliseconds;

                    // Call the endPhase handler for both: error and successful cases,
                    // but not if the unhandled exception will occur.
                    endPhaseLogMessage(loggingContext, statistics);

                    if (aggregator != null)
                    {
                        LoggingHelpers.LogPerformanceCollector(aggregator, loggingContext, loggingContext.LoggerComponentInfo, statistics.ElapsedMilliseconds);
                    }

                    if (!success)
                    {
                        Contract.Assume(loggingContext.ErrorWasLogged, "An error should have been logged after frontend phase: " + phase.ToString());
                        return false;
                    }
                }
            }

            return true;
        }

        private static void LaunchDebuggerIfConfigured(EnginePhases phase)
        {
            // TODO: engine, engine abstract and frontend host controller are very similar.
            // Frontend host controller has similar functionality with the engine which lead code duplication
            // and discrepancy in behavior.
            // This should be unified to avoid such a hacky solution like this method!
            EngineEnvironmentSettings.TryLaunchDebuggerAfterPhase(phase);
        }

        /// <inheritdoc />
        public override AbsolutePath GetFolderForFrontEnd(string friendlyName)
        {
            Contract.Requires(!string.IsNullOrEmpty(friendlyName));

            int nrOfEntries = m_frontEndPaths.AddOrUpdate(friendlyName, _ => 0, (_, dupes) => dupes + 1);
            return AbsolutePath.Create(FrontEndContext.PathTable, GetRootFrontEndFolder()).Combine(
                FrontEndContext.PathTable,
                PathAtom.Create(
                    FrontEndContext.StringTable,
                    friendlyName + (nrOfEntries == 0 ? string.Empty : ("_" + nrOfEntries.ToString(CultureInfo.InvariantCulture)))));
        }

        private string GetRootFrontEndFolder()
        {
            return System.IO.Path.Combine(m_outputDirectory, "frontend");
        }

        /// <summary>
        /// Creates front-end host for testing.
        /// </summary>
        /// <remarks>
        /// This front-end host is only for tests that do not involve evaluation, including the evaluation of config file.
        /// This front-end host is useful for tests that do not need to create pips, e.g., pretty printing, but still
        /// need front-end context that the host carries.
        /// </remarks>
        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope", Justification = "Clients should dispose the host")]
        public static FrontEndHostController CreateForTesting(
            FrontEndContext frontEndContext, 
            FrontEndEngineAbstraction engine,
            IModuleRegistry moduleRegistry,
            string configFilePath, 
            Logger logger = null, 
            string outputDirectory = null)
        {
            Contract.Requires(frontEndContext != null);

            var frontEndFactory = new FrontEndFactory();
            frontEndFactory.TrySeal(frontEndContext.LoggingContext);

            var frontEndHost = new FrontEndHostController(
                frontEndFactory,
                new DScriptWorkspaceResolverFactory(),
                new EvaluationScheduler(degreeOfParallelism: 1, cancellationToken: frontEndContext.CancellationToken),
                moduleRegistry,
                new FrontEndStatistics(),
                logger ?? Logger.CreateLogger(),
                collector: null,
                collectMemoryAsSoonAsPossible: false); // Don't need to collect memory in tests.

            ((IFrontEndController)frontEndHost).InitializeHost(
                frontEndContext,
                new ConfigurationImpl() {
                    FrontEnd = new FrontEndConfiguration()
                        {
                            UsePartialEvaluation = false,
                            UseSpecPublicFacadeAndAstWhenAvailable = false,
                            ReloadPartialEngineStateWhenPossible = false,
                            MaxFrontEndConcurrency = 1,
                        }
                });
            frontEndHost.m_resolvers = CollectionUtilities.EmptyArray<IResolver>();
            frontEndHost.HostState = State.ResolversInitialized;
            frontEndHost.Engine = engine;
            frontEndHost.FrontEndArtifactManager = frontEndHost.CreateFrontEndArtifactManager();
            frontEndHost.m_outputDirectory = outputDirectory;
            frontEndHost.PrimaryConfigFile = AbsolutePath.Create(frontEndContext.PathTable, configFilePath);
            return frontEndHost;
        }

        /// <summary>
        /// Initializes front-ends.
        /// </summary>
        public bool TryInitializeFrontEndsAndResolvers(IConfiguration configuration, QualifierId[] requestedQualifiers)
        {
            Contract.Requires(configuration != null);
            Contract.Requires(HostState == State.ConfigInterpreted);

            var resolverConfigurations = GetSourceResolverSettingsWithDefaults(configuration);

            if (resolverConfigurations == null)
            {
                return false;
            }

            // Most resolvers will ensure the workspaceresolverfactory is initialized, so do that here.
            // The factory context may be not set if the controller is not using the workspace, so we set that here
            if (!m_workspaceResolverFactory.IsInitialized)
            {
                m_workspaceResolverFactory.Initialize(FrontEndContext, this, configuration, requestedQualifiers);
            }

            foreach (IFrontEnd frontEnd in m_frontEndFactory.RegisteredFrontEnds)
            {
                frontEnd.InitializeFrontEnd(this, FrontEndContext, configuration);
            }

            // For each resolver settings, tries to find a front end for it.
            var resolvers = new List<IResolver>();
            foreach (var resolverConfiguration in resolverConfigurations)
            {
                IFrontEnd frontEndInstance;

                if (!m_frontEndFactory.TryGetFrontEnd(resolverConfiguration.Kind, out frontEndInstance))
                {
                    m_logger.UnregisteredResolverKind(FrontEndContext.LoggingContext, resolverConfiguration.Kind, string.Join(", ", m_frontEndFactory.RegisteredFrontEndKinds));
                    return false;
                }

                // We ask the front end we found to create a resolver to handle the settings
                var resolver = frontEndInstance.CreateResolver(resolverConfiguration.Kind);

                var maybeWorkspaceResolver = m_workspaceResolverFactory.TryGetResolver(resolverConfiguration);
                if (!maybeWorkspaceResolver.Succeeded)
                {
                    // Error should have been reported.
                    return false;
                }

                // TODO: Make initialization async.
                if (!resolver.InitResolverAsync(resolverConfiguration, maybeWorkspaceResolver.Result).GetAwaiter().GetResult())
                {
                    // Error has been reported by the corresponding front-end.
                    return false;
                }

                resolvers.Add(resolver);
            }

            m_resolvers = resolvers.ToArray();
            HostState = State.ResolversInitialized;
            return true;
        }

        /// <summary>
        /// An alternative to <see cref="TryInitializeFrontEndsAndResolvers(IConfiguration, QualifierId[])"/> used for testing.  Instead
        /// of taking and parsing a config object, this method directly takes a list of resolvers.
        /// </summary>
        internal void InitializeResolvers(IEnumerable<IResolver> resolvers)
        {
            m_resolvers = resolvers.ToArray();
            HostState = State.ResolversInitialized;
        }

        /// <summary>
        /// Returns a <see cref="IWorkspaceProvider"/> responsible for <see cref="Workspace"/> computation.
        /// </summary>
        internal bool TryGetWorkspaceProvider(IConfiguration configuration, QualifierId[] requestedQualifiers, out IWorkspaceProvider workspaceProvider, out IEnumerable<Failure> failures)
        {
            var workspaceConfiguration = GetWorkspaceConfiguration(configuration);

            // This is the point where we have all the objects we need to complete setting up the workspace factory
            if (!m_workspaceResolverFactory.IsInitialized)
            {
                m_workspaceResolverFactory.Initialize(FrontEndContext, this, configuration, requestedQualifiers);
            }

            return TryGetWorkspaceProvider(workspaceConfiguration, out workspaceProvider, out failures);
        }

        private bool TryGetWorkspaceProvider(WorkspaceConfiguration workspaceConfiguration, out IWorkspaceProvider workspaceProvider, out IEnumerable<Failure> failures)
        {
            return WorkspaceProvider.TryCreate(
                GetMainConfigWorkspace(),
                m_frontEndStatistics,
                m_workspaceResolverFactory,
                workspaceConfiguration,
                FrontEndContext.PathTable,
                FrontEndContext.SymbolTable,
                useDecorator: true,
                addBuiltInPreludeResolver: true,
                workspaceProvider: out workspaceProvider,
                failures: out failures);
        }

        private WorkspaceConfiguration GetWorkspaceConfiguration(IConfiguration configuration)
        {
            // This is to preserve existing logic where a default source resolver is explicitly added to the resolver list
            var resolversWithDefault = GetSourceResolverSettingsWithDefaults(configuration);

            // We create a configuration where the prelude is required to be present
            return GetWorkspaceConfiguration(configuration, resolversWithDefault, FrontEndContext.CancellationToken);
        }

        private Location GetLocation(Diagnostic diagnostic)
        {
            Contract.Requires(diagnostic != null);
            Contract.Requires(diagnostic.File != null);

            var absolutePath = AbsolutePath.Create(FrontEndContext.PathTable, diagnostic.File.FileName);
            var lineAndColumn = diagnostic.GetLineAndColumn(diagnostic.File);
            var location = new Location
            {
                Line = lineAndColumn.Line,
                Position = lineAndColumn.Character,
                File = absolutePath.ToString(FrontEndContext.PathTable),
            };

            return location;
        }

        [Pure]
        private List<IResolverSettings> GetSourceResolverSettingsWithDefaults(IConfiguration configuration)
        {
            bool existExplicitDefaultSourceResolver = false;
            AbsolutePath configFilePath = configuration.Layout.PrimaryConfigFile;
            var resolverSettings = new List<IResolverSettings>();

            if (configuration.Resolvers != null)
            {
                // Ensure all resolvers have a name.
                int i = 0;
                var resolverNames = new HashSet<string>();
                foreach (var resolver in configuration.Resolvers)
                {
                    if (!string.IsNullOrEmpty(resolver.Name))
                    {
                        resolverNames.Add(resolver.Name);
                    }
                    else
                    {
                        var name = resolver.Kind;
                        if (resolverNames.Contains(name))
                        {
                            // If there are multiple of these resolvers, append with a number
                            name = resolver.Kind + " #" + i.ToString("D3", CultureInfo.InvariantCulture);
                        }
                        else
                        {
                            resolverNames.Add(name);
                        }

                        resolver.SetName(name);
                    }

                    if (string.Equals(resolver.Kind, KnownResolverKind.DefaultSourceResolverKind, StringComparison.Ordinal))
                    {
                        existExplicitDefaultSourceResolver = true;
                        resolverSettings.Add(
                            CreateDefaultSourceResolverSettings(configFilePath, configuration));
                    }
                    else
                    {
                        resolverSettings.Add(resolver);
                    }

                    i++;
                }
            }

            if (!existExplicitDefaultSourceResolver)
            {
                // Always add default resolver as the last one considered by the front-end host in package resolution procedure, if the resolver
                // is not explicitly mentioned in the resolver list.
                // Users may use resolvers for external packages, but they do not have to additionally and explicitly specify the default resolver.
                // In > 90% cases, this is what users expect. Yes, one can still disable this behavior by setting the "DisableDefaultSourceResolver" to
                // true in the configuration file.
                resolverSettings.Add(CreateDefaultSourceResolverSettings(configFilePath, configuration));
            }

            return resolverSettings;
        }

        private async Task<bool> ConvertModuleToEvaluationAsync(ParsedModule module, Workspace workspace)
        {
            Contract.Requires(module != null);
            Contract.Requires(HostState == State.ResolversInitialized);

            for (int i = 0; i < m_resolvers.Length; ++i)
            {
                var resolver = m_resolvers[i];

                try
                {
                    var task = await resolver.TryConvertModuleToEvaluationAsync(ModuleRegistry, module, workspace);
                    if (task != null)
                    {
                        return task.Value;
                    }
                }
                catch (OperationCanceledException)
                {
                    // error is logged once at the top level (in ConvertWorkspaceToEvaluationAsync)
                    return false;
                }
                catch (Exception e)
                {
                    m_logger.FailedToConvertModuleToEvaluationModel(FrontEndContext.LoggingContext, module.Descriptor.DisplayName, e.ToStringDemystified());
                    return false;
                }
            }

            // TODO: this could be a separate function,
            // but in a stable condition (V1 is gone) only one logging function would be needed.
            m_logger.UnableToFindFrontEndToParse(
                FrontEndContext.LoggingContext,
                module.Definition.MainFile.ToString(FrontEndContext.PathTable));
            return false;
        }

        /// <summary>
        /// Evaluates a file.
        /// </summary>
        public Task<bool> EvaluateAsync(EvaluationFilter evaluationFilter, params QualifierId[] qualifierIds)
        {
            Contract.Requires(HostState == State.ResolversInitialized, "Invalid host state: resolvers should be initialized.");
            Contract.Requires(evaluationFilter != null);
            Contract.Requires(qualifierIds != null);
            Contract.Requires(qualifierIds.Length > 0);
            Contract.RequiresForAll(qualifierIds, qId => qId.IsValid);

            // Even though the workspace was filtered, we need to filter it again.
            // The filtered workspace has all transitive dependencies of all files based on information from the checker.
            // It means that if the file1.dsc references the file2.dsc in unreachable code,
            // the filtered workspace would have 2 files. But at evaluation time, only file1.dsc would be evaluated.
            List<ModuleDefinition> modulesToEvaluate = GetModulesAndSpecsToEvaluate(evaluationFilter);
            return EvaluateModulesAsync(qualifierIds, modulesToEvaluate);
        }

        private async Task<bool> EvaluateModulesAsync(QualifierId[] qualifierIds, IReadOnlyList<ModuleDefinition> modulesToEvaluate)
        {
            // Register the meta pips for the modules and the specs with the graph
            RegisterModuleAndSpecPips(Workspace);

            // Workspace has been converted and is not needed anymore
            CleanWorkspaceMemory();

            // Evaluate with progress reporting
            List<ModuleEvaluationProgress> items = qualifierIds
                .SelectMany(qualifierId =>
                    modulesToEvaluate.Select(m => new ModuleEvaluationProgress(EvaluateModuleAsync(m, qualifierId), m, qualifierId)))
                .ToList();

            var numSpecs = modulesToEvaluate.Sum(m => m.Specs.Count) * qualifierIds.Length;

            bool[] results = await TaskUtilities.AwaitWithProgressReporting(
                items,
                taskSelector: item => item.Task,
                action: (elapsed, all, remaining) => LogModuleEvaluationProgress(numSpecs, elapsed, all, remaining),
                period: EvaluationProgressReportingPeriod);
            bool success = results.All(b => b);
            if (!success)
            {
                return false;
            }

            if (PipGraphFragmentManager != null)
            {
                var tasks = PipGraphFragmentManager.GetAllFragmentTasks();
                numSpecs = tasks.Count;
                results = await TaskUtilities.AwaitWithProgressReporting(
                    tasks,
                    taskSelector: item => item.Item2,
                    action: (elapsed, all, remaining) => LogFragmentEvaluationProgress(numSpecs, elapsed, all, remaining),
                    period: EvaluationProgressReportingPeriod);
                return results.All(b => b);
            }

            return true;
        }

        /// <summary>
        /// Registers the meta pips for the modules and the specs with the graph
        /// </summary>
        /// <remarks>
        /// This should be called on the 'filtered down' modules for evaluation.
        /// </remarks>
        private void RegisterModuleAndSpecPips(Workspace  workspace)
        {
            if (PipGraph != null)
            {
                foreach (var module in workspace.Modules)
                {
                    // This is the module we create to parse config files and module.config.dsc files
                    if (module.Descriptor.Name == Names.ConfigModuleName)
                    {
                        continue;
                    }

                    // TODO: Deprecate top-level PACKAGES field.
                    // This is the module we create always regardlesss of config, but when the projects field is specified we do special handling
                    // of these and stick them in the __config__ module. But this bypasses the other 
                    // ALSO this is a still V1 MODULE :(:(:(:(:(
                    if (module.Descriptor.Name == Names.ConfigAsPackageName && module.PathToSpecs.Count == 0)
                    {
                        continue;
                    }

                    var moduleLocation = new LocationData(module.Definition.ModuleConfigFile, 0, 0);
                    PipGraph.AddModule(
                        new ModulePip(
                            module: module.Descriptor.Id,
                            identity: StringId.Create(FrontEndContext.StringTable, module.Descriptor.Name),
                            version: StringId.Create(FrontEndContext.StringTable, module.Descriptor.Version),
                            location: moduleLocation,
                            resolverKind: StringId.Create(FrontEndContext.StringTable, module.Descriptor.ResolverKind),
                            resolverName: StringId.Create(FrontEndContext.StringTable, module.Descriptor.ResolverName)
                        )
                    );

                    foreach (var spec in module.Specs.Keys)
                    {
                        PipGraph.AddSpecFile(
                            new SpecFilePip(
                                FileArtifact.CreateSourceFile(spec),
                                moduleLocation,
                                module.Descriptor.Id)
                        );
                    }
                }
            }
        }

        private void LogModuleEvaluationProgress(
            int numSpecsTotal,
            TimeSpan elapsed,
            IReadOnlyCollection<ModuleEvaluationProgress> allItems,
            IReadOnlyCollection<ModuleEvaluationProgress> remainingItems)
        {
            string remainingMessage = ConstructProgressRemainingMessage(elapsed, remainingItems);

            m_logger.FrontEndEvaluatePhaseProgress(
                FrontEndContext.LoggingContext,
                numModulesDone: allItems.Count - remainingItems.Count,
                numModulesTotal: allItems.Count,
                numSpecsDone: m_frontEndStatistics.SpecEvaluation.Count,
                numSpecsTotal: numSpecsTotal,
                remaining: remainingMessage);
        }

        private void LogFragmentEvaluationProgress(
            int numSpecsTotal,
            TimeSpan elapsed,
            IReadOnlyCollection<(PipGraphFragmentSerializer, Task<bool>)> allItems,
            IReadOnlyCollection<(PipGraphFragmentSerializer, Task<bool>)> remainingItems)
        {
            string remainingMessage = ConstructProgressRemainingMessage(elapsed, remainingItems);
            m_logger.FrontEndEvaluatePhaseFragmentProgress(
                FrontEndContext.LoggingContext,
                numFragmentsDone: allItems.Count - remainingItems.Count,
                numFragmentsTotal: allItems.Count,
                remaining: remainingMessage);
        }

        private static string ConstructProgressRemainingMessage(TimeSpan elapsed, IReadOnlyCollection<ModuleEvaluationProgress> remainingItems)
        {
            var progressMessages = remainingItems
                .Take(10)
                .Select(item => FormatProgressMessage(elapsed, item.Module.Descriptor.DisplayName))
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();

            return progressMessages.Count > 0
                ? Environment.NewLine + string.Join(Environment.NewLine, progressMessages)
                : "0";
        }

        private static string ConstructProgressRemainingMessage(TimeSpan elapsed, IReadOnlyCollection<(PipGraphFragmentSerializer, Task<bool>)> remainingItems)
        {
            var progressMessages = remainingItems
                .Where(item => item.Item1.PipsDeserialized > 0)
                .Take(10)
                .Select(item => FormatProgressMessage(elapsed, $"{item.Item1.FragmentName} ({item.Item1.PipsDeserialized}/{item.Item1.TotalPips})"))
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();

            return progressMessages.Count > 0
                ? Environment.NewLine + string.Join(Environment.NewLine, progressMessages)
                : "0";
        }

        private static string FormatProgressMessage(TimeSpan elapsed, string message)
        {
            var elapsedStr = FormattingEventListener.TimeSpanToString(TimeDisplay.Seconds, elapsed);
            return I($"    {elapsedStr} - {message}");
        }

        private async Task<bool> EvaluateModuleAsync(ModuleDefinition module, QualifierId qualifierId)
        {
            // Look for a resolver that owns the module
            for (int i = 0; i < m_resolvers.Length; ++i)
            {
                var result = await m_resolvers[i].TryEvaluateModuleAsync(m_evaluationScheduler, module, qualifierId);
                if (result != null)
                {
                    return result.Value;
                }
            }

            m_logger.UnableToFindFrontEndToEvaluate(
                FrontEndContext.LoggingContext,
                module.Root.ToString(FrontEndContext.PathTable));

            return false;
        }
        
        /// <summary>
        /// Creates a default source resolver from the build extent specified in the configuration.
        /// </summary>
        [Pure]
        private IInternalDefaultDScriptResolverSettings CreateDefaultSourceResolverSettings(AbsolutePath configFilePath, IConfiguration configObject)
        {
            Contract.Requires(configFilePath.IsValid);
            Contract.Requires(configObject != null);

            if (m_defaultDScriptResolverSettings == null)
            {
                m_defaultDScriptResolverSettings = new InternalDefaultDScriptResolverSettings()
                {
                    Name = KnownResolverKind.DefaultSourceResolverKind,
                    Kind = KnownResolverKind.DefaultSourceResolverKind,
                    Root = configFilePath.GetParent(FrontEndContext.PathTable),
                    Modules = configObject.Modules == null ? null : new List<AbsolutePath>(configObject.Modules),
                    Packages = configObject.Packages == null ? null : new List<AbsolutePath>(configObject.Packages),
                    Projects = configObject.Projects == null ? null : new List<AbsolutePath>(configObject.Projects),
                    ConfigFile = configFilePath,
                };
            }

            return m_defaultDScriptResolverSettings;
        }

        /// <summary>
        /// States of the host.
        /// </summary>
        public enum State : byte
        {
            /// <summary>
            /// Host is uninitialized.
            /// </summary>
            Uninitialized,

            /// <summary>
            /// Host is created.
            /// </summary>
            Created,

            /// <summary>
            /// The host is initialized.
            /// </summary>
            Initialized,

            /// <summary>
            /// Config is found and interpreted.
            /// </summary>
            ConfigInterpreted,

            /// <summary>
            /// Resolvers are initialized.
            /// </summary>
            ResolversInitialized,
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_nugetCache?.GetAwaiter().GetResult();
            FrontEndArtifactManager?.Dispose();
            CycleDetector?.Dispose();
        }
    }

    /// <summary>
    /// Simple memento class used internaly for module evaluation progress reporting.
    /// </summary>
    internal sealed class ModuleEvaluationProgress
    {
        internal Task<bool> Task { get; }

        internal ModuleDefinition Module { get; }

        internal QualifierId QualifierId { get; }

        /// <nodoc/>
        public ModuleEvaluationProgress(Task<bool> task, ModuleDefinition module, QualifierId qualifierId)
        {
            Task = task;
            Module = module;
            QualifierId = qualifierId;
        }
    }
}
