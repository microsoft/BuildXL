// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.App.Tracing;
using BuildXL.FrontEnd.CMake;
using BuildXL.FrontEnd.Core;
using BuildXL.FrontEnd.Download;
using BuildXL.FrontEnd.MsBuild;
using BuildXL.FrontEnd.Ninja;
using BuildXL.FrontEnd.Nuget;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Debugger;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Evaluator.Profiling;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using VSCode.DebugProtocol;

namespace BuildXL
{
    /// <summary>
    /// "Yet another factory" (c) that is responsible for entire front-end construction.
    /// </summary>
    /// <remarks>
    /// This class glues together different lower level factories like <see cref="FrontEndFactory"/> and <see cref="DScriptWorkspaceResolverFactory"/>
    /// in order to hide all this complexity.
    /// </remarks>
    public sealed class FrontEndControllerFactory : IFrontEndControllerFactory
    {
        private readonly FrontEndMode m_mode;

        private readonly IFrontEndStatistics m_statistics;

        private bool CollectMemoryAsSoonAsPossible { get; }

        /// <nodoc />
        public ICommandLineConfiguration Configuration { get; }

        /// <nodoc />
        public LoggingContext LoggingContext { get; }

        /// <nodoc />
        public PerformanceCollector Collector { get; }

        /// <nodoc />
        private FrontEndControllerFactory(
            FrontEndMode mode,
            LoggingContext loggingContext,
            ICommandLineConfiguration configuration,
            PerformanceCollector collector,
            bool collectMemoryAsSoonAsPossible,
            IFrontEndStatistics statistics)
        {
            m_mode = mode;
            CollectMemoryAsSoonAsPossible = collectMemoryAsSoonAsPossible;
            Configuration = configuration;
            LoggingContext = loggingContext;
            Collector = collector;
            m_statistics = statistics;
        }

        /// <nodoc />
        public IFrontEndController Create(PathTable pathTable, SymbolTable symbolTable)
        {
            if (m_mode == FrontEndMode.DebugScript)
            {
                return CreateControllerWithDebugger(pathTable, symbolTable);
            }

            if (m_mode == FrontEndMode.ProfileScript)
            {
                return CreateControllerWithProfiler(pathTable, symbolTable);
            }

            return CreateRegularController(symbolTable);
        }

        /// <nodoc />
        public static FrontEndControllerFactory Create(
            FrontEndMode mode,
            LoggingContext loggingContext,
            ICommandLineConfiguration configuration,
            PerformanceCollector collector,
            bool collectMemoryAsSoonAsPossible = true,
            IFrontEndStatistics statistics = null)
        {
            return new FrontEndControllerFactory(
                mode,
                loggingContext,
                configuration,
                collector,
                collectMemoryAsSoonAsPossible,
                statistics);
        }

        private IFrontEndController CreateControllerWithProfiler(PathTable pathTable, SymbolTable symbolTable)
        {
            var frontEndFactory = new FrontEndFactory();
            var profilerDecorator = new ProfilerDecorator();

            // When evaluation is done we materialize the result of the profiler
            frontEndFactory.AddPhaseEndHook(EnginePhases.Evaluate, () =>
            {
                var entries = profilerDecorator.GetProfiledEntries();
                var materializer = new ProfilerMaterializer(pathTable);
                var reportDestination = Configuration.FrontEnd.ProfileReportDestination(pathTable);

                Logger.Log.MaterializingProfilerReport(LoggingContext, reportDestination.ToString(pathTable));

                try
                {
                    materializer.Materialize(entries, reportDestination);
                }
                catch (BuildXLException ex)
                {
                    Logger.Log.ErrorMaterializingProfilerReport(LoggingContext, ex.LogEventErrorCode, ex.LogEventMessage);
                }
            });

            return TryCreateFrontEndController(
                frontEndFactory,
                profilerDecorator,
                Configuration,
                symbolTable,
                LoggingContext,
                Collector,
                collectMemoryAsSoonAsPossible: CollectMemoryAsSoonAsPossible,
                statistics: m_statistics);
        }

        private IFrontEndController CreateControllerWithDebugger(PathTable pathTable, SymbolTable symbolTable)
        {
            var confPort = Configuration.FrontEnd.DebuggerPort();
            var debugServerPort = confPort != 0 ? confPort : DebugServer.DefaultDebugPort;
            var pathTranslator = GetPathTranslator(Configuration.Logging, pathTable);
            var debugState = new DebuggerState(pathTable, LoggingContext, DScriptDebugerRenderer.Render, new DScriptExprEvaluator(LoggingContext));
            var debugServer = new DebugServer(LoggingContext, debugServerPort, 
                (debugger) => new DebugSession(debugState, pathTranslator, debugger));
            Task<IDebugger> debuggerTask = debugServer.StartAsync();
            var evaluationDecorator = new LazyDecorator(debuggerTask, Configuration.FrontEnd.DebuggerBreakOnExit());
            var frontEndFactory = new FrontEndFactory();

            frontEndFactory.AddPhaseStartHook(EnginePhases.Evaluate, () =>
            {
                if (!debuggerTask.IsCompleted)
                {
                    Logger.Log.WaitingForClientDebuggerToConnect(LoggingContext, debugServer.Port);
                }

                debuggerTask.Result?.Session.WaitSessionInitialized();
            });

            frontEndFactory.AddPhaseEndHook(EnginePhases.Evaluate, () =>
            {
                // make sure the debugger is shut down at the end (unnecessary in most cases, as the debugger will shut itself down after completion)
                debugServer.ShutDown();
                debuggerTask.Result?.ShutDown();
            });

            return TryCreateFrontEndController(
                frontEndFactory,
                evaluationDecorator,
                Configuration,
                symbolTable,
                LoggingContext,
                Collector,
                collectMemoryAsSoonAsPossible: CollectMemoryAsSoonAsPossible,
                statistics: m_statistics);
        }

        private IFrontEndController CreateRegularController(SymbolTable symbolTable)
        {
            var frontEndFactory = new FrontEndFactory();

            return TryCreateFrontEndController(
                frontEndFactory,
                decorator: null,
                configuration: Configuration,
                symbolTable: symbolTable,
                loggingContext: LoggingContext,
                collector: Collector,
                collectMemoryAsSoonAsPossible: CollectMemoryAsSoonAsPossible,
                statistics: m_statistics);
        }

        private static PathTranslator GetPathTranslator(ILoggingConfiguration conf, PathTable pathTable)
        {
            return conf.SubstTarget.IsValid && conf.SubstSource.IsValid
                ? new PathTranslator(conf.SubstTarget.ToString(pathTable), conf.SubstSource.ToString(pathTable))
                : null;
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope", Justification = "Consumers should call Dispose explicitly")]
        private static IFrontEndController TryCreateFrontEndController(
            FrontEndFactory frontEndFactory,
            IDecorator<EvaluationResult> decorator,
            ICommandLineConfiguration configuration,
            SymbolTable symbolTable,
            LoggingContext loggingContext,
            PerformanceCollector collector,
            bool collectMemoryAsSoonAsPossible,
            IFrontEndStatistics statistics)
        {
            Contract.Requires(frontEndFactory != null && !frontEndFactory.IsSealed);

            var workspaceResolverFactory = new DScriptWorkspaceResolverFactory();

            // Statistic should be global for all front-ends, not per an instance.
            var frontEndStatistics = statistics ?? new FrontEndStatistics();

            var sharedModuleRegistry = new ModuleRegistry(symbolTable);

            // Note, that the following code is absolutely critical for detecting that front-end related objects
            // are freed successfully after evaluation.
            // ModuleRegistry was picked intentionally because it holds vast amount of front-end data.
            FrontEndControllerMemoryObserver.CaptureFrontEndReference(sharedModuleRegistry);

            frontEndFactory.SetConfigurationProcessor(
                new ConfigurationProcessor(
                    new FrontEndStatistics(), // Configuration processing is so lightweight that it won't affect overall perf statistics
                    logger: null));

            var msBuildFrontEnd = new MsBuildFrontEnd();

            var ninjaFrontEnd = new NinjaFrontEnd();

            var cmakeFrontEnd = new CMakeFrontEnd();

            // TODO: Workspace resolvers and frontends are registered in separate factories. Consider
            // adding a main coordinator/registry
            RegisterKnownWorkspaceResolvers(
                symbolTable.StringTable,
                workspaceResolverFactory, 
                frontEndStatistics);

            frontEndFactory.AddFrontEnd(new DScriptFrontEnd(
                frontEndStatistics,
                evaluationDecorator: decorator));

            frontEndFactory.AddFrontEnd(new NugetFrontEnd(
                frontEndStatistics,
                evaluationDecorator: decorator));

            frontEndFactory.AddFrontEnd(new DownloadFrontEnd());

            frontEndFactory.AddFrontEnd(msBuildFrontEnd);
            frontEndFactory.AddFrontEnd(ninjaFrontEnd);
            frontEndFactory.AddFrontEnd(cmakeFrontEnd);

            if (!frontEndFactory.TrySeal(loggingContext))
            {
                return null;
            }

            return new FrontEndHostController(
                frontEndFactory,
                workspaceResolverFactory,
                evaluationScheduler: EvaluationScheduler.Default,
                moduleRegistry: sharedModuleRegistry,
                frontEndStatistics: frontEndStatistics,
                logger: BuildXL.FrontEnd.Core.Tracing.Logger.CreateLogger(),
                collector: collector, 
                collectMemoryAsSoonAsPossible: collectMemoryAsSoonAsPossible);
        }

        private static void RegisterKnownWorkspaceResolvers(
            StringTable stringTable,
            DScriptWorkspaceResolverFactory workspaceFactory,
            IFrontEndStatistics statistics)
        {
            workspaceFactory.RegisterResolver(
                KnownResolverKind.SourceResolverKind,
                () => new WorkspaceSourceModuleResolver(stringTable, statistics, logger: null));
            workspaceFactory.RegisterResolver(
                KnownResolverKind.DScriptResolverKind,
                () => new WorkspaceSourceModuleResolver(stringTable, statistics, logger: null));
            workspaceFactory.RegisterResolver(
                KnownResolverKind.DefaultSourceResolverKind,
                () => new WorkspaceDefaultSourceModuleResolver(stringTable, statistics, logger: null));
            workspaceFactory.RegisterResolver(
                KnownResolverKind.NugetResolverKind,
                () => new WorkspaceNugetModuleResolver(stringTable, statistics));
            workspaceFactory.RegisterResolver(
                KnownResolverKind.DownloadResolverKind,
                () => new DownloadWorkspaceResolver());
            workspaceFactory.RegisterResolver(
                KnownResolverKind.MsBuildResolverKind,
                () => new MsBuildWorkspaceResolver());
            workspaceFactory.RegisterResolver(
                KnownResolverKind.NinjaResolverKind,
                () => new NinjaWorkspaceResolver());
            workspaceFactory.RegisterResolver(
                KnownResolverKind.CMakeResolverKind,
                () => new CMakeWorkspaceResolver());
        }
    }
}
