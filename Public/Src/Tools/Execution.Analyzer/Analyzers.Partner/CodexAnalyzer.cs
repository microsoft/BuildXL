// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using BuildXL.Pips.DirectedGraph;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
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

        private readonly Context m_context;

        public CodexAnalyzer(AnalysisInput input, string outputDirectory)
            : base(input)
        {
            m_context = new Context(this);
            m_executedPipsTracker = new VisitationTracker(input.CachedGraph.DirectedGraph);
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
            var cscLookupBySpec = CachedGraph.PipGraph.RetrievePipReferencesOfType(PipType.Process)
                .Where(lazyPip => m_executedPipsTracker.WasVisited(lazyPip.PipId.ToNodeId()))
                .Select(lazyPip => (Process)lazyPip.HydratePip())
                .Where(proc => m_context.IsCsc(proc))
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
                    var toolName = cscInvocation.GetToolName(PathTable);
                    bool isDotNetTool = IsDotNetTool(toolName, m_context);
                    var targetPath = Path.Combine(specOutDirectory, cscInvocation.FormattedSemiStableHash + ".csc.args.txt");
                    Console.WriteLine(I($"Emitting: {targetPath}"));
                    using (var writer = new StreamWriter(targetPath))
                    {
                        writer.WriteLine(I($"Project={specPath}"));
                        var arguments = GetArgumentsDataFromProcess(cscInvocation, skipFragmentCount: isDotNetTool ? 1 : 0)
                            .With(newlineSeparator, PipDataFragmentEscaping.NoEscaping);
                        writer.Write(arguments.ToString(PathTable));
                    }
                }
            }

            return 0;
        }

        public class Context : PipExecutionContext
        {
            public Context(ExecutionAnalyzerBase analyzer) 
                : base(analyzer.PipGraph.Context)
            {
                Analyzer = analyzer;
                DotnetName = PathAtom.Create(StringTable, "dotnet");
                DotnetExeName = PathAtom.Create(StringTable, "dotnet.exe");
                CscExeName = PathAtom.Create(StringTable, "csc.exe");
                CscDllName = PathAtom.Create(StringTable, "csc.dll");
            }

            public ExecutionAnalyzerBase Analyzer { get; }
            public PathAtom DotnetName { get; }
            public PathAtom DotnetExeName { get; }
            public PathAtom CscExeName { get; }
            public PathAtom CscDllName { get; }

            public bool IsCsc(Process process)
            {
                var toolName = process.GetToolName(PathTable);
                return toolName.CaseInsensitiveEquals(StringTable, CscExeName) ||
                (IsDotNetTool(toolName, this) &&
                 FirstArgIsPathWithFileName(this, process, CscDllName));
            }
        }

        private static bool IsDotNetTool(PathAtom toolName, Context context)
        {
            return toolName.CaseInsensitiveEquals(context.StringTable, context.DotnetName) 
                || toolName.CaseInsensitiveEquals(context.StringTable, context.DotnetExeName);
        }

        private static bool FirstArgIsPathWithFileName(Context context, Process process, PathAtom name)
        {
            var arguments = context.Analyzer.GetArgumentsDataFromProcess(process);
            if (arguments.FragmentCount == 0)
            {
                return false;
            }

            var firstArg = arguments.First();
            if (firstArg.FragmentType != PipFragmentType.AbsolutePath)
            {
                return false;
            }

            var path = firstArg.GetPathValue();
            if (!path.IsValid)
            {
                return false;
            }

            return path.GetName(context.PathTable).CaseInsensitiveEquals(context.StringTable, name);
        }
    }
}
