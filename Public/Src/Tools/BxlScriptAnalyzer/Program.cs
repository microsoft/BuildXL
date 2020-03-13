// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Threading;
using BuildXL.FrontEnd.Factory;
using BuildXL.FrontEnd.Script.Analyzer.Analyzers;
using BuildXL.FrontEnd.Script.Analyzer.Tracing;
using BuildXL.Storage;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;

namespace BuildXL.FrontEnd.Script.Analyzer
{
    /// <summary>
    /// BuildXL.Execution.Analyzer entry point
    /// </summary>
    internal sealed class Program : ToolProgram<Args>
    {
        private readonly PathTable m_pathTable = new PathTable();

        private Program()
            : base("Dsa")
        {
        }

        /// <nodoc />
        public static int Main(string[] arguments)
        {
            return new Program().MainHandler(arguments);
        }

        /// <inheritdoc />
        public override bool TryParse(string[] rawArgs, out Args arguments)
        {
            try
            {
                arguments = new Args(rawArgs, AnalyzerFactory, m_pathTable);
                return true;
            }
            catch (Exception ex)
            {
                ConsoleColor original = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine(ex.GetLogEventMessage());
                Console.ForegroundColor = original;
                arguments = null;
                return false;
            }
        }

        /// <inheritdoc />
        public override int Run(Args arguments)
        {
            int retries = 0;
            int maxRetries = 3;
            while (retries < maxRetries)
            {
                try
                {
                    retries++;
                    return RunInner(arguments);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Try: {retries} of {maxRetries} to analyze workspace hit an error.");
                    if (retries == maxRetries)
                    {
                        ConsoleColor original = Console.ForegroundColor;
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.Error.WriteLine(ex.GetLogEventMessage());
                        Console.ForegroundColor = original;
                    }
                }
            }

            return 1;
        }

        /// <inheritdoc />
        private int RunInner(Args arguments)
        {
            if (arguments.Help)
            {
                return 0;
            }

            // TODO: Don't assume particular hash types (this is particularly seen in WorkspaceNugetModuleResolver.TryGetExpectedContentHash).
            ContentHashingUtilities.SetDefaultHashType();

            using (SetupEventListener(EventLevel.Informational))
            {
                var logger = Logger.CreateLogger();

                arguments.CommandLineConfig.Engine.Phase = arguments.Analyzers.Max(a => a.RequiredPhases);
                // This needs to be passed in as a path through environment variable because it changes every 
                if (!WorkspaceBuilder.TryBuildWorkspaceAndCollectFilesToAnalyze(
                    logger,
                    m_pathTable,
                    arguments.CommandLineConfig,
                    out var workspace,
                    out var pipGraph,
                    out var filesToAnalyze,
                    out var context))
                {
                    return 1;
                }

                foreach (var analyzer in arguments.Analyzers)
                {
                    if (!analyzer.SetSharedState(arguments, context, logger, workspace, pipGraph))
                    {
                        return 1;
                    }
                }

                if (arguments.Fix && arguments.Analyzers.Last().Kind != AnalyzerKind.PrettyPrint)
                {
                    logger.FixRequiresPrettyPrint(context.LoggingContext);
                }

                int errorCount = 0;

                // TODO: Make this multi-threaded. For now since we are developing keeping simple loop to maintain easy debugging.
                foreach (var kv in filesToAnalyze)
                {
                    foreach (var analyzer in arguments.Analyzers)
                    {
                        if (!analyzer.AnalyzeSourceFile(workspace, kv.Key, kv.Value))
                        {
                            Interlocked.Increment(ref errorCount);
                        }
                    }
                }

                foreach (var analyzer in arguments.Analyzers)
                {
                    if (!analyzer.FinalizeAnalysis())
                    {
                        return 1;
                    }
                }

                if (errorCount > 0)
                {
                    logger.AnalysisErrorSummary(context.LoggingContext, errorCount, arguments.Analyzers.Count);
                    return 1;
                }

                return 0;
            }
        }

        /// <summary>
        /// Set up console event listener for BuildXL's ETW event sources.
        /// </summary>
        /// <param name="level">The level of data to be sent to the listener.</param>
        /// <returns>An <see cref="EventListener"/> with the appropriate event sources registered.</returns>
        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope")]
        private static IDisposable SetupEventListener(EventLevel level)
        {
            var eventListener = new ConsoleEventListener(Events.Log, DateTime.UtcNow, true, true, true, false, level: level);

            var primarySource = global::bxlScriptAnalyzer.ETWLogger.Log;
            if (primarySource.ConstructionException != null)
            {
                throw primarySource.ConstructionException;
            }

            eventListener.RegisterEventSource(primarySource);

            eventListener.EnableTaskDiagnostics(global::BuildXL.Tracing.ETWLogger.Tasks.CommonInfrastructure);

            var eventSources = new EventSource[]
                               {
                                   global::bxlScriptAnalyzer.ETWLogger.Log,
                                   global::BuildXL.Engine.Cache.ETWLogger.Log,
                                   global::BuildXL.Engine.ETWLogger.Log,
                                   global::BuildXL.Scheduler.ETWLogger.Log,
                                   global::BuildXL.Pips.ETWLogger.Log,
                                   global::BuildXL.Tracing.ETWLogger.Log,
                                   global::BuildXL.Storage.ETWLogger.Log,
                               }.Concat(FrontEndControllerFactory.GeneratedEventSources);

            using (var dummy = new TrackingEventListener(Events.Log))
            {
                foreach (var eventSource in eventSources)
                {
                    Events.Log.RegisterMergedEventSource(eventSource);
                }
            }

            return eventListener;
        }

        private static Analyzer AnalyzerFactory(AnalyzerKind type)
        {
            switch (type)
            {
                case AnalyzerKind.PrettyPrint:
                    return new PrettyPrint();
                case AnalyzerKind.LegacyLiteralCreation:
                    return new LegacyLiteralCreation();
                case AnalyzerKind.PathFixer:
                    return new PathFixerAnalyzer();
                case AnalyzerKind.Documentation:
                    return new DocumentationGenerator();
                case AnalyzerKind.Codex:
                    return new CodexAnalyzer();
                default:
                    return null;
            }
        }
    }
}
