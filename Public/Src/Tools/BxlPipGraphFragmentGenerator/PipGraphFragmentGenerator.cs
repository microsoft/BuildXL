// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.PipGraphFragmentGenerator.Tracing;
using BuildXL.Engine;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Engine.Cache.Fingerprints.TwoPhase;
using BuildXL.FrontEnd.Core;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.FileSystem;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Filter;
using BuildXL.Scheduler.Graph;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Instrumentation.Common;
using CancellationToken = System.Threading.CancellationToken;

namespace BuildXL.PipGraphFragmentGenerator
{
    /// <summary>
    /// Class for generating pip graph fragment.
    /// </summary>
    public static class PipGraphFragmentGenerator
    {
        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope")]
        private static bool TryBuildPipGraphFragment(
            ICommandLineConfiguration commandLineConfig,
            PipGraphFragmentGeneratorConfiguration pipGraphFragmentGeneratorConfig,
            FrontEndContext frontEndContext,
            EngineContext engineContext,
            EvaluationFilter evaluationFilter)
        {
            Contract.Requires(frontEndContext != null);
            Contract.Requires(engineContext != null);
            Contract.Requires(commandLineConfig.Startup.ConfigFile.IsValid);
            Contract.Requires(evaluationFilter != null);

            var pathTable = engineContext.PathTable;
            var loggingContext = frontEndContext.LoggingContext;

            var mutableCommandlineConfig = CompleteCommandLineConfiguration(commandLineConfig);
            BuildXLEngine.ModifyConfigurationForCloudbuild(mutableCommandlineConfig, false, pathTable, loggingContext);
            BuildXLEngine.PopulateLoggingAndLayoutConfiguration(mutableCommandlineConfig, pathTable, bxlExeLocation: null);

            var statistics = new FrontEndStatistics();
            var frontEndControllerFactory = FrontEndControllerFactory.Create(
                mode: FrontEndMode.NormalMode,
                loggingContext: loggingContext,
                configuration: mutableCommandlineConfig,
                collector: null,
                statistics: statistics);

            var controller = frontEndControllerFactory.Create(engineContext.PathTable, engineContext.SymbolTable);
            controller.InitializeHost(frontEndContext, mutableCommandlineConfig);

            FrontEndHostController frontEndHostController = (FrontEndHostController)controller;

            var config = controller.ParseConfig(mutableCommandlineConfig);

            if (config == null)
            {
                return false;
            }

            IPipGraph pipGraphBuilder = null;

            using (var cache = Task.FromResult<Possible<EngineCache>>(
                new EngineCache(
                    new InMemoryArtifactContentCache(),
                    new EmptyTwoPhaseFingerprintStore())))
            {
                var mountsTable = MountsTable.CreateAndRegister(loggingContext, engineContext, config, mutableCommandlineConfig.Startup.Properties);
                FrontEndEngineAbstraction frontEndEngineAbstraction = new FrontEndEngineImplementation(
                    loggingContext,
                    frontEndContext.PathTable,
                    config,
                    mutableCommandlineConfig.Startup,
                    mountsTable,
                    InputTracker.CreateDisabledTracker(loggingContext),
                    null,
                    null,
                    () => FileContentTable.CreateStub(),
                    5000,
                    false);

                pipGraphBuilder = pipGraphFragmentGeneratorConfig.TopSort
                    ? new PipGraphFragmentBuilderTopSort(engineContext, config, mountsTable.MountPathExpander)
                    : new PipGraphFragmentBuilder(engineContext, config, mountsTable.MountPathExpander);

                if (!AddConfigurationMountsAndCompleteInitialization(config, loggingContext, mountsTable))
                {
                    return false;
                }

                if (!mountsTable.PopulateModuleMounts(config.ModulePolicies.Values, out var moduleMountsTableMap))
                {
                    Contract.Assume(loggingContext.ErrorWasLogged, "An error should have been logged after MountTable.PopulateModuleMounts()");
                    return false;
                }

                using (frontEndEngineAbstraction is IDisposable ? (IDisposable)frontEndEngineAbstraction : null)
                {
                    if (!controller.PopulateGraph(
                        cache: cache,
                        graph: pipGraphBuilder,
                        engineAbstraction: frontEndEngineAbstraction,
                        evaluationFilter: evaluationFilter,
                        configuration: config,
                        startupConfiguration: mutableCommandlineConfig.Startup))
                    {
                        // Error should have been reported already
                        return false;
                    }

                    if (!SerializeFragmentIfRequested(pipGraphFragmentGeneratorConfig, frontEndContext, pipGraphBuilder))
                    {
                        // Error should have been reported already
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool SerializeFragmentIfRequested(
            PipGraphFragmentGeneratorConfiguration pipGraphFragmentGeneratorConfig,
            FrontEndContext context,
            IPipGraph pipGraph)
        {
            Contract.Requires(context != null);
            Contract.Requires(pipGraph != null);

            if (!pipGraphFragmentGeneratorConfig.OutputFile.IsValid)
            {
                return true;
            }

            try
            {
                var serializer = new PipGraphFragmentSerializer(context, new PipGraphFragmentContext())
                {
                    AlternateSymbolSeparator = pipGraphFragmentGeneratorConfig.AlternateSymbolSeparator
                };

                serializer.Serialize(
                    pipGraphFragmentGeneratorConfig.OutputFile, 
                    pipGraph,
                    pipGraphFragmentGeneratorConfig.Description,
                    pipGraphFragmentGeneratorConfig.TopSort);

                Logger.Log.GraphFragmentSerializationStats(context.LoggingContext, serializer.FragmentDescription, serializer.Stats.ToString());

                return true;
            }
            catch (Exception e)
            {
                Logger.Log.GraphFragmentExceptionOnSerializingFragment(
                    context.LoggingContext, 
                    pipGraphFragmentGeneratorConfig.OutputFile.ToString(context.PathTable), 
                    e.ToString());

                return false;
            }
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

        private static CommandLineConfiguration CompleteCommandLineConfiguration(ICommandLineConfiguration commandLineConfig)
        {
            return new CommandLineConfiguration (commandLineConfig)
            {
                FrontEnd =
                {
                    DebugScript = false,
                    PreserveFullNames = true,
                    PreserveTrivia = false,
                    CancelParsingOnFirstFailure = true,
                    UseSpecPublicFacadeAndAstWhenAvailable = false,
                    ConstructAndSaveBindingFingerprint = false,
                    NameResolutionSemantics = NameResolutionSemantics.ImplicitProjectReferences,
                    UsePackagesFromFileSystem = false,
                    ReleaseWorkspaceBeforeEvaluation = true,
                    UnsafeOptimizedAstConversion = true,
                    AllowUnsafeAmbient = true,
                },
                Engine =
                {
                    Phase = EnginePhases.Schedule
                },
                Schedule =
                {
                    UseFixedApiServerMoniker = true
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
        /// Generates pip graph fragment.
        /// </summary>
        public static bool TryGeneratePipGraphFragment(
            PathTable pathTable,
            ICommandLineConfiguration commandLineConfig,
            PipGraphFragmentGeneratorConfiguration pipGraphFragmentConfig)
        {
            var loggingContext = new LoggingContext(nameof(PipGraphFragmentGenerator));
            var fileSystem = new PassThroughFileSystem(pathTable);
            var engineContext = EngineContext.CreateNew(CancellationToken.None, pathTable, fileSystem);

            FrontEndContext context = engineContext.ToFrontEndContext(loggingContext);

            // Parse filter string.

            var evaluationFilter = EvaluationFilter.Empty;
            if (!string.IsNullOrWhiteSpace(commandLineConfig.Filter))
            {
                if (!TryGetEvaluationFilter(loggingContext, engineContext, commandLineConfig.Filter, out evaluationFilter))
                {
                    // Error should have been been reported already.
                    return false;
                }
            }

            if (!TryBuildPipGraphFragment(
                commandLineConfig,
                pipGraphFragmentConfig,
                context,
                engineContext,
                evaluationFilter))
            {
                return false;
            }

            return true;
        }

        private static bool TryGetEvaluationFilter(
            LoggingContext loggingContext, 
            EngineContext engineContext, 
            string filter, 
            out EvaluationFilter evaluationFilter)
        {
            FilterParser parser = new FilterParser(
                engineContext,
                DummyPathResolver,
                filter);
            RootFilter rootFilter;
            FilterParserError error;
            if (!parser.TryParse(out rootFilter, out error))
            {
                Logger.Log.ErrorParsingFilter(loggingContext, filter, error.Position, error.Message, error.FormatFilterPointingToPosition(filter));
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
    }
}
