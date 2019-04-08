// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.IO.Compression;
using BuildXL.Engine;
using BuildXL.Engine.Serialization;
using BuildXL.ToolSupport;
using BuildXL.Utilities;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeInputTrackerAnalyzer()
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
                    throw Error("Unknown option for input tracker analysis: {0}", opt.Name);
                }
            }

            return new InputTrackerAnalyzer(GetAnalysisInput(), inputFile, outputFile);
        }

        private static void WriteInputTrackerAnalyzerHelp(HelpWriter writer)
        {
            writer.WriteBanner("Input tracker Analysis");
            writer.WriteModeOption(nameof(AnalysisMode.InputTracker), "Generates textual format of input tracker");
            writer.WriteOption("inputFile", "Required. The input file.", shortName: "i");
            writer.WriteOption("outputFile", "Required. The output file.", shortName: "o");
        }
    }

    internal class InputTrackerAnalyzer : Analyzer
    {
        private readonly string m_inputFile;
        private readonly string m_outputFile;

        public InputTrackerAnalyzer(AnalysisInput input, string inputFile, string outputFile)
            : base(input)
        {
            m_inputFile = inputFile;
            m_outputFile = outputFile;
        }

        public override int Analyze()
        {
            using(var fileStreamWrapper = FileSystemStreamProvider.Default.OpenReadStream(m_inputFile))
            {
                var fileStream = fileStreamWrapper.Value;

                FileEnvelopeId persistedCorrelationId = InputTracker.FileEnvelope.ReadHeader(fileStream);

                var isCompressed = fileStream.ReadByte() == 1;

                using (Stream readStream = isCompressed ? new DeflateStream(fileStream, CompressionMode.Decompress) : fileStream)
                using (BinaryReader reader = new BinaryReader(readStream))
                using (var writer = File.CreateText(Path.GetFullPath(m_outputFile)))
                {
                    InputTracker.ReadAndWriteText(reader, writer);
                }
            }

            return 0;
        }
    }
}
