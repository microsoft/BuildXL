// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Analyzers.Core.XLGPlusPlus;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using Google.Protobuf;

namespace BuildXL.Execution.Analyzer
{

    internal partial class Args
    {
        public Analyzer InitializeDominoInvocationAnalyzer()
        {
            string inputDirPath = null;
            string outputFilePath = null;

            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputFile", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputFilePath = ParseSingletonPathOption(opt, outputFilePath);
                }
                else if (opt.Name.Equals("inputDir", StringComparison.OrdinalIgnoreCase) ||
                     opt.Name.Equals("i", StringComparison.OrdinalIgnoreCase))
                {
                    inputDirPath = ParseSingletonPathOption(opt, inputDirPath);
                }
                else
                {
                    throw Error("Unknown option for event stats analysis: {0}", opt.Name);
                }
            }

            return new DominoInvocationAnalyzer(GetAnalysisInput())
            {
                InputDirPath = inputDirPath,
                OutputFilePath = outputFilePath
            };
        }

        /// <summary>
        /// Write the help message when the analyzer is invoked with the /help flag
        /// </summary>
        /// <param name="writer"></param>
        private static void WriteDominoInvocationHelp(HelpWriter writer)
        {
            writer.WriteBanner("Domino Invocation \"Analyzer\"");
            writer.WriteModeOption(nameof(AnalysisMode.DominoInvocationXLG), "Gets and outputs information related to domino invocation events from the database.");
            writer.WriteOption("inputDir", "Required. The directory to read the RocksDB database from", shortName: "i");
            writer.WriteOption("outputFile", "Required. The file where to write the results", shortName: "o");
        }
    }

    /// <summary>
    /// Analyzer to examine Domino Invocation events that have been dumped into the db
    /// </summary>
    internal sealed class DominoInvocationAnalyzer : Analyzer
    {

        public string InputDirPath;
        public string OutputFilePath;

        public DominoInvocationAnalyzer(AnalysisInput input): base(input)
        {

        }

        /// <inheritdoc/>
        public override int Analyze()
        {
            var dataStore = new XLGppDataStore();
            if (dataStore.OpenDatastore(storeDirectory: InputDirPath))
            {
                dataStore.GetEventsByType((int)ExecutionEventId.DominoInvocation);
            }
            else
            {
                Console.WriteLine("Could not load RocksDB datastore. Exiting analyzer.");
            }
            return 0;
        }

    }
}
