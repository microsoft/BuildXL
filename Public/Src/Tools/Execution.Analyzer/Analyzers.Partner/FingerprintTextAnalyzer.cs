// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Scheduler.Graph;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeFingerprintTextAnalyzer()
        {
            string fingerprintFilePath = null;
            bool compress = false;
            bool includeServicePips = false;
            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputFile", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    fingerprintFilePath = ParseSingletonPathOption(opt, fingerprintFilePath);
                }
                else if (opt.Name.StartsWith("compress", StringComparison.OrdinalIgnoreCase))
                {
                    compress = ParseBooleanOption(opt);
                }
                else if (opt.Name.StartsWith("includeServicePips", StringComparison.OrdinalIgnoreCase))
                {
                    includeServicePips = ParseBooleanOption(opt);
                }
                else
                {
                    throw Error("Unknown option for fingerprint text analysis: {0}", opt.Name);
                }
            }

            return new FingerprintTextAnalyzer(GetAnalysisInput())
            {
                FingerprintFilePath = fingerprintFilePath,
                CompressFingerprintFile = compress,
                IncludeServicePips = includeServicePips,
            };
        }

        private static void WriteFingerprintTextAnalyzerHelp(HelpWriter writer)
        {
            writer.WriteBanner("FingerprintText Analysis");
            writer.WriteModeOption(nameof(AnalysisMode.FingerprintText), "Generates a text file containing fingerprint text for executed pips");
            writer.WriteOption("outputFile", "Required. The directory containing the cached pip graph files.", shortName: "o");
            writer.WriteOption("compress", "Required. The directory containing the cached pip graph files.");
        }
    }

    /// <summary>
    /// Analyzer used to generate fingerprint text file
    /// </summary>
    internal sealed class FingerprintTextAnalyzer : Analyzer
    {
        public static readonly int MaxDegreeOfParallelism = Environment.ProcessorCount;

        private readonly ConcurrentBigMap<FileArtifact, FileContentInfo> m_fileContentMap = new ConcurrentBigMap<FileArtifact, FileContentInfo>();
        private readonly HashSet<PipId> m_completedPips;
        private readonly PathExpander m_pathExpander;

        /// <summary>
        /// The path to the fingerprint file
        /// </summary>
        public string FingerprintFilePath;
        public bool CompressFingerprintFile;
        public bool IncludeServicePips = false;
        private PipContentFingerprinter m_contentFingerprinter;
        private ExtraFingerprintSalts? m_fingerprintSalts;

        public FingerprintTextAnalyzer(AnalysisInput input)
            : base(input)
        {
            m_completedPips = new HashSet<PipId>();
            m_contentFingerprinter = null;
            m_pathExpander = input.CachedGraph.MountPathExpander;
        }

        private FileContentInfo LookupHash(FileArtifact artifact)
        {
            return m_fileContentMap[artifact];
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:DoNotDisposeObjectsMultipleTimes")]
        public override int Analyze()
        {
            List<PipReference> orderedPips = CachedGraph.PipGraph
                .RetrievePipReferencesOfType(PipType.Process)
                .Where(lazyPip => m_completedPips.Contains(lazyPip.PipId))
                .OrderBy(lazyPip => CachedGraph.DataflowGraph.GetNodeHeight(lazyPip.PipId.ToNodeId()))
                .ThenBy(lazyPip => lazyPip.SemiStableHash)
                .ToList();

            using (var fingerprintStream = File.Create(FingerprintFilePath, bufferSize: 64 << 10 /* 64 KB */))
            {
                using (
                    var fingerprintArchive = CompressFingerprintFile
                        ? new ZipArchive(fingerprintStream, ZipArchiveMode.Create)
                        : null)
                {
                    using (
                        var writer =
                            new StreamWriter(
                                CompressFingerprintFile
                                    ? fingerprintArchive.CreateEntry("fingerprint.txt", CompressionLevel.Fastest).Open()
                                    : fingerprintStream))
                    {
                        var pipsAndFingerprintTexts = orderedPips.AsParallel().AsOrdered()
                            .WithDegreeOfParallelism(degreeOfParallelism: MaxDegreeOfParallelism)
                            .Select(p => GetFingerprintInfo(p));

                        int doneFingerprints = 0;
                        var t = new Timer(
                            o =>
                            {
                                var done = doneFingerprints;
                                Console.WriteLine("Fingerprints Done: {0} of {1}", done, orderedPips.Count);
                            },
                            null,
                            5000,
                            5000);

                        try
                        {
                            foreach (var pipAndFingerprintText in pipsAndFingerprintTexts)
                            {
                                doneFingerprints++;
                                Process pip = pipAndFingerprintText.process;
                                if (!IncludeServicePips && pip.IsStartOrShutdownKind)
                                {
                                    continue;
                                }

                                ContentFingerprint fingerprint = pipAndFingerprintText.contentFingerprint;
                                string fingerprintText = pipAndFingerprintText.fingerprintText;

                                writer.WriteLine("PipStableId: Pip{0:X16}", pip.SemiStableHash);
                                writer.WriteLine(
                                    "Pip Dependency Chain Length: {0}",
                                    CachedGraph.DataflowGraph.GetNodeHeight(pip.PipId.ToNodeId()));
                                writer.WriteLine(pip.GetDescription(CachedGraph.Context));
                                writer.WriteLine("Fingerprint: {0}", fingerprint);
                                writer.WriteLine();
                                writer.WriteLine(fingerprintText);
                                writer.WriteLine();
                                writer.WriteLine();
                            }
                        }
                        finally
                        {
                            // kill and wait for the status timer to die...
                            using (var e = new AutoResetEvent(false))
                            {
                                t.Dispose(e);
                                e.WaitOne();
                            }
                        }
                    }
                }
            }

            return 0;
        }

        private (Process process, ContentFingerprint contentFingerprint, string fingerprintText) GetFingerprintInfo(PipReference lazyPip)
        {
            // Make sure the extra event data has set the value properly here.
            Contract.Requires(m_fingerprintSalts.HasValue, "m_fingerprintSalts is not set.");

            Process pip = (Process)lazyPip.HydratePip();
            string fingerprintText;

            // This checks for missing content info for pips.
            foreach (var dependency in pip.Dependencies)
            {
                if (!m_fileContentMap.ContainsKey(dependency))
                {
                    return (pip, ContentFingerprint.Zero, "FINGERPRINT CONTAINS UNKNOWN DEPENDENCIES");
                }
            }

            if (m_contentFingerprinter == null)
            {
                m_contentFingerprinter = new PipContentFingerprinter(
                CachedGraph.Context.PathTable,
                LookupHash,
                m_fingerprintSalts.Value,
                pathExpander: m_pathExpander,
                pipDataLookup: CachedGraph.PipGraph.QueryFileArtifactPipData)
                {
                    FingerprintTextEnabled = true,
                };
            }

            // TODO: Allow specifying fingerprinting version on the command line
            ContentFingerprint fingerprint = m_contentFingerprinter.ComputeWeakFingerprint(
                pip,
                out fingerprintText);

            return (pip, fingerprint, fingerprintText);
        }

        public override void FileArtifactContentDecided(FileArtifactContentDecidedEventData data)
        {
            m_fileContentMap[data.FileArtifact] = data.FileContentInfo;
        }

        /// <inheritdoc />
        public override void BuildSessionConfiguration(BuildSessionConfigurationEventData data)
        {
            m_fingerprintSalts = data.ToFingerprintSalts();
        }

        public override void PipExecutionPerformance(PipExecutionPerformanceEventData data)
        {
            // Got a performance event for a process so register the process as completed
            if (CachedGraph.PipTable.GetPipType(data.PipId) == PipType.Process)
            {
                m_completedPips.Add(data.PipId);
            }
        }
    }
}
