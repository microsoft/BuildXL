// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BuildXL.Pips;
using BuildXL.Scheduler.Filter;
using BuildXL.Scheduler.Graph;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;
using BuildXL.Utilities.Tracing;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializePipFilterAnalyzer()
        {
            string outputFilePath = null;
            string filter = null;

            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputFile", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputFilePath = ParseSingletonPathOption(opt, outputFilePath);
                }
                else if (opt.Name.Equals("filter", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("f", StringComparison.OrdinalIgnoreCase))
                {
                    filter = opt.Value;
                }
                else
                {
                    throw Error("Unknown option for filter analysis: {0}", opt.Name);
                }
            }

            if (string.IsNullOrWhiteSpace(outputFilePath))
            {
                throw Error("Missing required argument 'outputFile'");
            }

            var input = GetAnalysisInput();

            return new PipFilterAnalyzer(input)
            {
                OutputFilePath = outputFilePath,
                Filter = filter,
            };
        }

        private static void WritePipFilterHelp(HelpWriter writer)
        {
            writer.WriteBanner("Filter Log Analysis");
            writer.WriteModeOption(nameof(AnalysisMode.PipFilter), "Gets a list of pips and output files given a list of inputs");
            writer.WriteOption(
                "outputFile",
                "Required. The file where to write the filtered execution log.",
                shortName: "o");
            writer.WriteOption(
                "filter",
                "Optional. If no filter is specified, a REPL will be launched which will ask for a filter.",
                shortName: "f");
        }
    }

    /// <summary>
    /// Analyzer used to get stats on events (count and total size)
    /// </summary>
    internal sealed class PipFilterAnalyzer : Analyzer
    {
        /// <summary>
        /// The path to the output file
        /// </summary>
        public string OutputFilePath;

        /// <summary>
        /// The filter to be processed (for non-interactive mode)
        /// </summary>
        public string Filter;

        public PipFilterAnalyzer(AnalysisInput input)
            : base(input)
        {
        }

        public override void Prepare()
        {
            using (var stream = File.Open(OutputFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            using (var writer = new StreamWriter(stream))
            {
                // Non-interactive mode
                if (!string.IsNullOrWhiteSpace(Filter))
                {
                    ProcessFilter(Filter, stream, writer);
                }
                // Interactive REPL
                else
                {
                    string filterText = null;

                    while ((filterText = PromptFilter()) != null)
                    {
                        ProcessFilter(filterText, stream, writer);
                    }
                }
            }
        }

        private bool ProcessFilter(string filterText, FileStream stream, StreamWriter writer)
        {
            var parser = new FilterParser(CachedGraph.Context, CachedGraph.MountPathExpander.TryGetRootByMountName, filterText);

            if (!parser.TryParse(out var rootFilter, out var error))
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "Error at position {1} of command line pip filter {0}. {3} {2}",
                    filterText,
                    error.Position,
                    error.FormatFilterPointingToPosition(filterText),
                    error.Message));
                return false;
            }

            var outputs = PipGraph.FilterOutputs(rootFilter);
            var pips = new List<PipId>();

            if (PipGraph.FilterNodesToBuild(Events.StaticContext, rootFilter, out var nodes, canonicalizeFilter: true))
            {
                foreach (var node in nodes)
                {
                    pips.Add(node.ToPipId());
                }
            }

            writer.WriteLine(I($"Filter: {filterText}"));
            writer.WriteLine(I($"Pips: {pips.Count}"));
            Console.WriteLine(I($"Pips: {pips.Count}"));
            foreach (var pip in pips)
            {
                writer.WriteLine(GetDescription(GetPip(pip)));
            }

            writer.WriteLine();
            Console.WriteLine(I($"Outputs: {outputs.Count}"));
            writer.WriteLine(I($"Outputs: {outputs.Count}"));
            foreach (var output in outputs)
            {
                var kind = output.IsDirectory ? "D" : "F";
                writer.WriteLine(I($"{kind}: {output.Path.ToString(PathTable)} ({GetDescription(GetPip(PipGraph.GetProducer(output)))})"));
            }

            writer.WriteLine();
            writer.WriteLine();
            writer.Flush();
            stream.Flush();

            return true;
        }

        private static string PromptFilter()
        {
            Console.WriteLine("Enter filter:");
            var filterText = Console.ReadLine();
            if (filterText != null)
            {
                if (filterText.StartsWith("<"))
                {
                    return string.Join(string.Empty, File.ReadAllLines(filterText.TrimStart('<').Trim()));
                }
            }

            return filterText;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:DoNotDisposeObjectsMultipleTimes")]
        public override int Analyze()
        {
            return 0;
        }
    }
}
