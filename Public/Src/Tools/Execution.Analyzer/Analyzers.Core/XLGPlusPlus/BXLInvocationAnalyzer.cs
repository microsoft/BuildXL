// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Analyzers.Core.XLGPlusPlus;
using BuildXL.ToolSupport;

namespace BuildXL.Execution.Analyzer
{

    internal partial class Args
    {
        public Analyzer InitializeBXLInvocationAnalyzer()
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


            if (string.IsNullOrEmpty(inputDirPath))
            {
                throw Error("/inputDir parameter is required");
            }

            if (string.IsNullOrEmpty(outputFilePath))
            {
                throw Error("/outputFile parameter is required");
            }

            return new BXLInvocationAnalyzer(GetAnalysisInput())
            {
                InputDirPath = inputDirPath,
                OutputFilePath = outputFilePath
            };
        }

        /// <summary>
        /// Write the help message when the analyzer is invoked with the /help flag
        /// </summary>
        private static void WriteDominoInvocationHelp(HelpWriter writer)
        {
            writer.WriteBanner("BXL Invocation \"Analyzer\"");
            writer.WriteModeOption(nameof(AnalysisMode.BXLInvocationXLG), "Gets and outputs information related to BXL invocation events from RocksDB.");
            writer.WriteOption("inputDir", "Required. The directory to read the RocksDB database from", shortName: "i");
            writer.WriteOption("outputFile", "Required. The file where to write the results", shortName: "o");
        }
    }

    /// <summary>
    /// Analyzer to examine BXL Invocation events that have been dumped into the db
    /// </summary>
    internal sealed class BXLInvocationAnalyzer : Analyzer
    {
        /// <summary>
        /// Input directory path that contains the RocksDB sst files
        /// </summary>
        public string InputDirPath;

        /// <summary>
        /// Output file path where the results will be written to.
        /// </summary>
        public string OutputFilePath;

        public BXLInvocationAnalyzer(AnalysisInput input) : base(input) { }

        /// <inheritdoc/>
        public override int Analyze()
        {
            var dataStore = new XldbDataStore(storeDirectory: InputDirPath);
            File.AppendAllLines(OutputFilePath, dataStore.GetBXLInvocationEvents());

            return 0;
        }

        /// <inheritdoc/>
        protected override bool ReadEvents()
        {
            // Do nothing. This analyzer does not read events.
            return true;
        }
    }
}
