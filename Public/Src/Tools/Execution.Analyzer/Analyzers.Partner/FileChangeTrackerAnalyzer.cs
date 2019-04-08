// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Storage.ChangeTracking;
using BuildXL.ToolSupport;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeFileChangeTrackerAnalyzer()
        {
            string outputFile = null;
            string inputFile = null;
            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputFile", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputFile = ParseSingletonPathOption(opt, outputFile);
                }
                else if (opt.Name.Equals("inputFile", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("i", StringComparison.OrdinalIgnoreCase))
                {
                    inputFile = ParseSingletonPathOption(opt, inputFile);
                }
                else
                {
                    throw Error("Unknown option for fingerprint text analysis: {0}", opt.Name);
                }
            }

            return new FileChangeTrackerAnalyzer(GetAnalysisInput(), inputFile, outputFile);
        }

        private static void WriteFileChangeTrackerAnalyzerHelp(HelpWriter writer)
        {
            writer.WriteBanner("File Change Tracker Analysis");
            writer.WriteModeOption(nameof(AnalysisMode.FileChangeTracker), "Generates textual format of file change tracker");
            writer.WriteOption("inputFile", "Required. The input file.", shortName: "i");
            writer.WriteOption("outputFile", "Required. The output file.", shortName: "o");
        }
    }

    internal class FileChangeTrackerAnalyzer : Analyzer
    {
        private readonly string m_cachedGraphDirectory;
        private readonly string m_outputFile;
        private readonly string m_inputFile;

        public FileChangeTrackerAnalyzer(AnalysisInput input, string inputFile, string outputFile)
            : base(input)
        {
            m_cachedGraphDirectory = input.CachedGraphDirectory;
            m_inputFile = inputFile;
            m_outputFile = outputFile;
        }

        public override int Analyze()
        {
            Console.WriteLine("Loading file change tracker from cache graph directory '{0}'", m_cachedGraphDirectory);

            var loggingContext = new LoggingContext(nameof(FileChangeTrackerAnalyzer));
            FileChangeTracker fileChangeTracker;
            var loadResult = FileChangeTracker.LoadTrackingChanges(
                loggingContext,
                null,
                null,
                m_inputFile,
                // if we do not pass the fingerprint, FileChangeTracker will not check for fingerprint match
                null, 
                out fileChangeTracker,
                loadForAllCapableVolumes: false);

            if (!loadResult.Succeeded)
            {
                Console.Error.WriteLine("Unable to load file change tracker '" + m_inputFile + "'");
                return 1;
            }

            using (var writer = File.CreateText(Path.GetFullPath(m_outputFile)))
            {
                fileChangeTracker.WriteText(writer);
            }

            return 0;
        }
    }
}
