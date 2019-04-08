// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.ToolSupport;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        // TODO: Generate CSV summary output, html output tables with suspect summary
        public Analyzer InitializeBuildStatus(AnalysisInput analysisInput)
        {
            string reportOutputPath = null;
            string outputFilterPath = string.Empty;
            var filterOptions = new FilterOptions()
            {
                NoCriticalPathReport = false,
                TransitiveDownPips = false,
                IgnoreAbsentPathProbe = false,
            };

            var whatBuiltOptions = default(WhatBuiltOptions);
            var rootSubdirectories = new List<string>();
            int maximumCountOfDifferencesToReport = 20; // Report ordered by reference count so 20 gives an idea of most impactful.
            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputFile", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    reportOutputPath = ParseSingletonPathOption(opt, reportOutputPath);
                }
                else if (opt.Name.Equals("whatbuilt", StringComparison.OrdinalIgnoreCase) ||
                         opt.Name.Equals("wb", StringComparison.OrdinalIgnoreCase))
                {
                    filterOptions.WhatBuilt = true;
                    Console.Write("Executing with the following options: ");
                    var whatBuiltOption = ParseStringOption(opt);
                    if (whatBuiltOption.Contains("X"))
                    {
                        whatBuiltOptions.Executed = true;
                        Console.Write(" Executed ");
                    }

                    if (whatBuiltOption.Contains("F"))
                    {
                        whatBuiltOptions.Failed = true;
                        Console.Write(" Failed ");
                    }

                    if (whatBuiltOption.Contains("U"))
                    {
                        whatBuiltOptions.UpToDate = true;
                        Console.Write(" Up-to-date ");
                    }

                    if (whatBuiltOption.Contains("C"))
                    {
                        whatBuiltOptions.Cached = true;
                        Console.Write(" Cached ");
                    }

                    if (whatBuiltOption.Contains("R"))
                    {
                        whatBuiltOptions.Updated = true;
                        Console.Write(" Updated ");
                    }

                    Console.WriteLine();
                    if (!(whatBuiltOptions.Cached || whatBuiltOptions.Executed || whatBuiltOptions.Failed || whatBuiltOptions.UpToDate ||
                          whatBuiltOptions.Updated))
                    {
                        throw Error("Unknown option for whatbuilt analysis: {0}", whatBuiltOption);
                    }
                }
                else if (opt.Name.Equals("outputFilterPath", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("fp", StringComparison.OrdinalIgnoreCase))
                {
                    outputFilterPath = ParsePathOption(opt);
                }
                else if (opt.Name.Equals("filterRootSubdirectories", StringComparison.OrdinalIgnoreCase) ||
                                   opt.Name.Equals("frs", StringComparison.OrdinalIgnoreCase))
                {
                    var filterRootSubdirectories = ParseStringOption(opt);
                    rootSubdirectories = filterRootSubdirectories.Split(',').ToList();
                }
                else
                {
                    throw Error("Unknown option for buildstatus analysis: {0}", opt.Name);
                }
            }

            return new BuildStatus(analysisInput, maximumCountOfDifferencesToReport, filterOptions, whatBuiltOptions, outputFilterPath, rootSubdirectories)
            {
                ComparedFilePath = reportOutputPath,
                HtmlOutput = true,
            };
        }

        private static void WriteBuildStatusHelp(HelpWriter writer)
        {
            writer.WriteBanner("BuildStatus Analysis");
            writer.WriteModeOption(nameof(AnalysisMode.BuildStatus), "Generates report on process pips that build and its impact and a summary");
            writer.WriteOption("outputFile", "Not Required for whatbuilt option. The file where to write the results.", shortName: "o");
            writer.WriteOption("whatbuilt", "Console output of pips [CFRUX]. C = Cached, F = Failed, R = Updated, U = UpToDate, X = Executed Pips", shortName: "wb");
            writer.WriteOption("outputFilterPath", "Optional - Filter to show only Pip outputs updated to the filter path.", shortName: "fp");
            writer.WriteOption("filterRootSubdirectories", "Optional - Coma separated list of subdirectories to filter out from /fp path option.", shortName: "frs");
            writer.WriteOption("Usage:", "/whatbuilt:R /fp:rootpath /frs:logs,foo  :: Dump to console executed, failed, cached Pips and output files that were written to specified filter path option excluding those that were updated on frs subdirectories.");
            writer.WriteOption("Usage:", "/whatbuilt:XF will dump to console executed and failed Pips only.\n");
        }
    }

    internal struct WhatBuiltOptions
    {
        public bool Cached;
        public bool Executed;
        public bool Failed;
        public bool Updated;
        public bool UpToDate;
    }

    /// <summary>
    /// Analyzer used to
    /// </summary>
    internal sealed class BuildStatus : SummaryAnalyzer
    {
        private readonly bool m_generateWhatBuiltOutput;
        private readonly string m_outputFilterPath;
        private readonly List<string> m_rootSubdirectories;
        private WhatBuiltOptions m_whatBuiltOptions;

        public BuildStatus(AnalysisInput input, int maxDifferenceReportCount, FilterOptions filterOptions, WhatBuiltOptions whatBuiltOptions, string outputFilterPath, List<string> rootSubdirectories)
            : base(input, maxDifferenceReportCount, filterOptions)
        {
            m_generateWhatBuiltOutput = filterOptions.WhatBuilt;
            m_whatBuiltOptions = whatBuiltOptions;
            m_outputFilterPath = outputFilterPath;
            m_rootSubdirectories = rootSubdirectories;
            SingleLog = true;
        }

        public override int Analyze()
        {
            if (m_generateWhatBuiltOutput)
            {
                Console.WriteLine("Loading BuildXL Execution log {0}", ExecutionLogPath);
                PrintWhatBuiltToConsole();
                return 0;
            }

            return GenerateSummaries();
        }

        private void PrintWhatBuiltToConsole()
        {
            Console.WriteLine("Total number of process pips:: {0}", GetProcessPipCount());

            if (m_whatBuiltOptions.UpToDate)
            {
                Console.WriteLine("Number of pips that were up to date: {0}", ProcessPipsExectuionTypesCounts.UpToDate);
                var upToDate = GetPipsExecutionLevel(PipExecutionLevel.UpToDate);
                if (upToDate.Count > 0)
                {
                    Console.WriteLine("Up to date pips:");
                    PrintPipsDetails(upToDate);
                }
            }

            var cached = GetPipsExecutionLevel(PipExecutionLevel.Cached);
            var executed = GetPipsExecutionLevel(PipExecutionLevel.Executed);
            var failed = GetPipsExecutionLevel(PipExecutionLevel.Failed);
            if (m_whatBuiltOptions.Cached)
            {
                Console.WriteLine("Number of pips that have been deployed from cache: {0}", ProcessPipsExectuionTypesCounts.Cached);
                if (cached.Count > 0)
                {
                    Console.WriteLine("Pips deployed from cache:");
                    PrintPipsDetails(cached);
                }
            }

            if (m_whatBuiltOptions.Executed)
            {
                Console.WriteLine("Number of pips that have been built: {0}", ProcessPipsExectuionTypesCounts.Executed);
                if (executed.Count > 0)
                {
                    Console.WriteLine("Pips that executed successfully:");
                    PrintPipOutputs(executed);
                }
            }

            if (m_whatBuiltOptions.Failed)
            {
                Console.WriteLine("Number of pips that failed: {0}", ProcessPipsExectuionTypesCounts.Failed);
                if (failed.Count > 0)
                {
                    Console.WriteLine("Pips that executed and failed:");
                    PrintPipsDetails(failed);
                }
            }

            if (m_whatBuiltOptions.Updated)
            {
                // Prints the updated pips that updated binaries
                Console.WriteLine(
                    "Number of pips that updated binaries: {0}",
                    ProcessPipsExectuionTypesCounts.Failed + ProcessPipsExectuionTypesCounts.Cached + ProcessPipsExectuionTypesCounts.Executed);
                Console.WriteLine("Updated binaries:");

                // Start listing executed, followed by cache, and failed pips
                PrintPipOutputs(executed);
                PrintPipOutputs(cached);
                PrintPipOutputs(failed);
            }
        }

        /// <summary>
        /// Non-Concurrent write text to console. This method prints to the console the text with the specified colors
        /// There are no concurrent calls to this method in this analyzer because pips are printed in serial order.
        /// </summary>
        private static void ColoredConsoleWrite(ConsoleColor foreground, ConsoleColor background, string text)
        {
            ConsoleColor originalForeground = Console.ForegroundColor;
            ConsoleColor originalBackground = Console.BackgroundColor;

            Console.ForegroundColor = foreground;
            Console.BackgroundColor = background;
            Console.Write(text);
            Console.ForegroundColor = originalForeground;
            Console.BackgroundColor = originalBackground;
        }

        private void PrintPipOutputs(IEnumerable<Process> executed)
        {
            foreach (var process in executed)
            {
                ColoredConsoleWrite(
                    ConsoleColor.White,
                    ConsoleColor.DarkBlue,
                    process.Provenance.OutputValueSymbol.ToString(CachedGraph.Context.SymbolTable));

                Console.Write(" - {0} - Duration:", GetPipWorkingDirectory(process));

                ColoredConsoleWrite(
                    ConsoleColor.Yellow,
                    Console.BackgroundColor,
                    $"{GetPipElapsedTime(process):dd\\.hh\\:mm\\:ss} ");

                ColoredConsoleWrite(
                    ConsoleColor.Yellow,
                    ConsoleColor.DarkGreen,
                    GetPipExecutionLevel(process.PipId).ToString());
                Console.WriteLine();

                foreach (var fileOutput in process.FileOutputs)
                {
                    var file = fileOutput.Path.ToString(CachedGraph.Context.PathTable);
                    if (file.StartsWith(m_outputFilterPath, StringComparison.OrdinalIgnoreCase))
                    {
                        var relativepath = file.Substring(m_outputFilterPath.Length + 1);
                        if (m_rootSubdirectories.Any(subdirectory => relativepath.StartsWith(subdirectory, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        ConsoleColor fileConsoleColor = Console.ForegroundColor;
                        if (relativepath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || relativepath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || relativepath.EndsWith(".sys", StringComparison.OrdinalIgnoreCase))
                        {
                            fileConsoleColor = ConsoleColor.Green;
                        }
                        else if (relativepath.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
                        {
                            fileConsoleColor = ConsoleColor.Cyan;
                        }

                        ColoredConsoleWrite(
                            fileConsoleColor,
                            Console.BackgroundColor,
                            string.Format(System.Globalization.CultureInfo.InvariantCulture, "   {0}", file));

                        Console.WriteLine();
                    }
                }
            }
        }

        private void PrintPipsDetails(IEnumerable<Process> pips)
        {
            foreach (var pip in pips)
            {
                Console.WriteLine(
                    "{0} ({1})",
                    pip.Provenance.OutputValueSymbol.ToString(CachedGraph.Context.SymbolTable),
                    GetPipWorkingDirectory(pip));
            }
        }
    }
}
