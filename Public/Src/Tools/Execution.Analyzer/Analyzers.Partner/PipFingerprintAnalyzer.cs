// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Engine.Cache.Serialization;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializePipFingerprintAnalyzer(AnalysisInput analysisInput)
        {
            string outputDirectory = null;
            string formattedSemistableHash = "";
            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputDirectory", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputDirectory = ParseSingletonPathOption(opt, outputDirectory);
                }
                else if (opt.Name.StartsWith("pip", StringComparison.OrdinalIgnoreCase))
                {
                    formattedSemistableHash = "Pip" + ParseStringOption(opt).ToUpperInvariant().Replace("PIP", "");
                }
                else
                {
                    throw Error("Unknown option for fingerprint input analysis: {0}", opt.Name);
                }
            }

            if (string.IsNullOrEmpty(outputDirectory))
            {
                throw new Exception("'outputDirectory' is required.");
            }

            if (string.IsNullOrEmpty(formattedSemistableHash))
            {
                throw new Exception("'pip' is required.");
            }

            return new PipFingerprintAnalyzer(analysisInput)
            {
                OutputDirectory = outputDirectory,
                PipFormattedSemistableHash = formattedSemistableHash
            };
        }

        private static void WritePipFingerprintAnalyzerHelp(HelpWriter writer)
        {
            writer.WriteBanner("Pip Fingerprint Analysis (for build with /storeFingerprints enabled)");
            writer.WriteModeOption(nameof(AnalysisMode.CacheMiss), "If the build was run with /storeFingerprints enabled, dumps the last known fingerprint inputs for a pip");
            writer.WriteOption("outputDirectory", "Required. The directory where to write the results", shortName: "o");
            writer.WriteOption("pip", "Optional. Defaults to false.");
        }
    }

    public class PipFingerprintAnalyzer : Analyzer
    {
        /// <summary>
        /// The name of the output file for the analysis.
        /// </summary>
        public const string AnalysisFileName = "pipFingerprint.txt";

        /// <summary>
        /// The path to the output directory.
        /// </summary>
        public string OutputDirectory;

        /// <summary>
        /// Pip formatted semistable hash.
        /// </summary>
        public string PipFormattedSemistableHash;

        /// <summary>
        /// FingerprintStore directory location;
        /// </summary>
        private readonly string m_storeLocation;

        /// <summary>
        /// FingerprintStore reader.
        /// </summary>
        private FingerprintStoreReader m_storeReader;

        /// <summary>
        /// Analysis file writer.
        /// </summary>
        private TextWriter m_writer;

        public PipFingerprintAnalyzer(AnalysisInput analysisInput) : base(analysisInput)
        {
            m_storeLocation = FingerprintStoreAnalyzer.GetStoreLocation(analysisInput);
        }

        protected override bool ReadEvents()
        {
            // Do nothing. This analyzer does not read events.
            return true;
        }

        /// <summary>
        /// Prepares the analyzer for producing outputs.
        /// </summary>
        public override void Prepare()
        {
            m_storeReader = FingerprintStoreReader.Create(m_storeLocation, Path.Combine(OutputDirectory, "json")).Result;
            m_writer = new StreamWriter(Path.Combine(OutputDirectory, AnalysisFileName));
            WriteHeader();
        }

        private void WriteHeader()
        {
            m_writer.WriteLine("FingerprintStore: " + m_storeLocation);
            m_writer.WriteLine("Pip: " + PipFormattedSemistableHash);
            m_writer.WriteLine();
        }

        public override int Analyze()
        {
            using (var pipSession = m_storeReader.StartPipRecordingSession(PipFormattedSemistableHash))
            {
                m_writer.WriteLine("WEAKFINGERPRINT:");
                m_writer.WriteLine(JsonTree.Serialize(pipSession.GetWeakFingerprintTree()));
                m_writer.WriteLine();
                m_writer.WriteLine("STRONGFINGERPRINT:");
                m_writer.WriteLine(JsonTree.Serialize(pipSession.GetStrongFingerprintTree()));
            }

            m_writer.Flush();
            return 0;
        }
    }
}
