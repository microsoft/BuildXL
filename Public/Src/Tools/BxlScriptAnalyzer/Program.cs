// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Threading;
using System.Diagnostics.Tracing;
using BuildXL.FrontEnd.Script.Analyzer.Analyzers;
using BuildXL.FrontEnd.Script.Analyzer.Tracing;
using BuildXL.Storage;
using BuildXL.ToolSupport;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.Analyzer
{
    /// <summary>
    /// BuildXL.Execution.Analyzer entry point
    /// </summary>
    internal sealed class Program : ToolProgram<Args>
    {
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
                arguments = new Args(rawArgs, AnalyzerFactory);
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
            if (arguments.Help)
            {
                return 0;
            }

            // TODO: Don't assume particular hash types (this is particularly seen in WorkspaceNugetModuleResolver.TryGetExpectedContentHash).
            ContentHashingUtilities.SetDefaultHashType();

            using (Logger.SetupEventListener(EventLevel.Informational))
            {
                PathTable pathTable = new PathTable();

                var logger = Logger.CreateLogger();

                if (!WorkspaceBuilder.TryBuildWorkspaceAndCollectFilesToAnalyze(
                    logger,
                    pathTable,
                    arguments.Analyzers.Max(a => a.RequiredPhases),
                    arguments.Config,
                    arguments.Filter,
                    arguments.OutputDirectory,
                    arguments.ObjectDirectory,
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
                    analyzer.FinalizeAnalysis();
                }

                if (errorCount > 0)
                {
                    logger.AnalysisErrorSummary(context.LoggingContext, errorCount, arguments.Analyzers.Count);
                    return 1;
                }

                return 0;
            }
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
                case AnalyzerKind.GraphFragment:
                    return new GraphFragmentGenerator();
                default:
                    return null;
            }
        }
    }
}
