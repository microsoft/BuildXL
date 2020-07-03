// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Pips.Operations;
using BuildXL.ToolSupport;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeCopyFilesAnalyzer()
        {
            string outputFilePath = null;
            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputFile", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputFilePath = ParseSingletonPathOption(opt, outputFilePath);
                }
                else
                {
                    throw Error("Unknown option for copy file analysis: {0}", opt.Name);
                }
            }

            if (string.IsNullOrEmpty(outputFilePath))
            {
                throw Error("/outputFile parameter is required");
            }

            return new CopyFilesAnalyzer(GetAnalysisInput(), outputFilePath);
        }

        private static void WriteCopyFilesAnalyzerHelp(HelpWriter writer)
        {
            writer.WriteBanner("Copy Files Analysis");
            writer.WriteModeOption(nameof(AnalysisMode.CopyFile), "Lists all copy files by: destination src");
            writer.WriteOption("outputFile", "Required. The location of the output file.", shortName: "o");
        }
    }

    /// <summary>
    /// Writes a file with (destination, source) pairs for each copy file
    /// </summary>
    public sealed class CopyFilesAnalyzer : Analyzer
    {
        private readonly string m_outputFilePath;

        public CopyFilesAnalyzer(AnalysisInput input, string outputFilePath)
            : base(input)
        {
            m_outputFilePath = outputFilePath;
        }

        public override int Analyze()
        {
            List<string> copyFileLines = new List<string>();
            foreach (var pip in CachedGraph.PipGraph.RetrievePipsOfType(PipType.CopyFile))
            {
                var copyFilePip = (CopyFile)pip;
                string destination = copyFilePip.Destination.Path.ToString(CachedGraph.Context.PathTable);
                string source = copyFilePip.Source.Path.ToString(CachedGraph.Context.PathTable);
                copyFileLines.Add(destination + "\t" + source);
            }

            File.WriteAllLines(m_outputFilePath, copyFileLines.OrderBy(x => x));
            return 0;
        }
    }
}
