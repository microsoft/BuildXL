// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Graph;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeCodexAnalyzer()
        {
            string outputDirectory = null;

            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("out", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputDirectory = ParseSingletonPathOption(opt, outputDirectory);
                }
                else
                {
                    throw Error("Unknown option for codex analysis: {0}", opt.Name);
                }
            }

            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw Error("Output directory must be specified with /out");
            }

            return new CodexAnalyzer(GetAnalysisInput(), outputDirectory);
        }
    }

    /// <summary>
    /// Analyzer for generating compiler arguments files used by Ref12 Codex.exe analyzer for semantic search/navigation
    /// </summary>
    internal sealed class CodexAnalyzer : Analyzer
    {
        /// <summary>
        /// The path to the fingerprint file
        /// </summary>
        public string OutputDirectory { get; }

        private readonly VisitationTracker m_executedPipsTracker;

        public CodexAnalyzer(AnalysisInput input, string outputDirectory)
            : base(input)
        {
            m_executedPipsTracker = new VisitationTracker(input.CachedGraph.DataflowGraph);
            OutputDirectory = outputDirectory;
        }

        public override void PipExecutionPerformance(PipExecutionPerformanceEventData data)
        {
            m_executedPipsTracker.MarkVisited(data.PipId.ToNodeId());
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:DoNotDisposeObjectsMultipleTimes")]
        public override int Analyze()
        {
            var cscExePathAtom = PathAtom.Create(StringTable, "csc.exe");

            var cscLookupBySpec = CachedGraph.PipGraph.RetrievePipReferencesOfType(PipType.Process)
                .Where(lazyPip => m_executedPipsTracker.WasVisited(lazyPip.PipId.ToNodeId()))
                .Select(lazyPip => (Process)lazyPip.HydratePip())
                .Where(proc => proc.Executable.Path.GetName(PathTable).CaseInsensitiveEquals(StringTable, cscExePathAtom))
                .ToLookup(process => process.Provenance.Token.Path);

            var newlineSeparator = StringId.Create(StringTable, Environment.NewLine);

            foreach (var specGroup in cscLookupBySpec)
            {
                var specName = specGroup.Key.GetName(PathTable).ToString(StringTable);
                var specOutDirectory = Path.Combine(OutputDirectory, specName);
                var specPath = specGroup.Key.ToString(PathTable);
                Directory.CreateDirectory(specOutDirectory);
                Console.WriteLine(I($"Processing: {specPath}"));
                foreach (var cscInvocation in specGroup)
                {
                    var targetPath = Path.Combine(specOutDirectory, cscInvocation.FormattedSemiStableHash + ".csc.args.txt");
                    Console.WriteLine(I($"Emitting: {targetPath}"));
                    using (var writer = new StreamWriter(targetPath))
                    {
                        writer.WriteLine(I($"Project={specPath}"));
                        var arguments = GetArgumentsDataFromProcess(cscInvocation)
                            .With(newlineSeparator, PipDataFragmentEscaping.NoEscaping);
                        writer.Write(arguments.ToString(PathTable));
                    }
                }
            }

            return 0;
        }
    }
}
