// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using BuildXL.Execution.Analyzer.Analyzers.CacheMiss;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public Analyzer InitializeCacheDumpAnalyzer(AnalysisInput analysisInput)
        {
            string outputFile = null;
            long? semistableHash = null;
            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputFile", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputFile = ParseSingletonPathOption(opt, outputFile);
                }
                else if (opt.Name.StartsWith("pip", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.StartsWith("p", StringComparison.OrdinalIgnoreCase))
                {
                    semistableHash = Convert.ToInt64(ParseStringOption(opt).ToUpperInvariant().Replace("PIP", ""), 16);
                }
                else
                {
                    throw Error("Unknown option for fingerprint text analysis: {0}", opt.Name);
                }
            }

            if (semistableHash == null)
            {
                throw Error("pip parameter is required");
            }

            return new CacheDumpAnalyzer(analysisInput)
            {
                OutputFile = outputFile,
                TargetSemistableHash = semistableHash.Value,
            };
        }

        private static void WriteCacheDumpHelp(HelpWriter writer)
        {
            writer.WriteBanner("Cache Dump Analysis");
            writer.WriteModeOption(nameof(AnalysisMode.CacheDump), "EXPERIMENTAL. Dumps cache lookup information for a pip");
            writer.WriteOption("outputFile", "Required. The file where to write the results", shortName: "o");
            writer.WriteOption("pip", "Required. The identifier for the pip. (i.e., pip semi-stable hash)", shortName: "p");
        }
    }

    /// <summary>
    /// Analyzer used to compute the reason for cache misses
    /// </summary>
    internal sealed class CacheDumpAnalyzer : Analyzer
    {
        private readonly AnalysisModel m_model;

        /// <summary>
        /// The path to the output file
        /// </summary>
        public string OutputFile;

        public override bool CanHandleWorkerEvents => true;

        public long TargetSemistableHash;
        private StreamWriter m_writer;

        public CacheDumpAnalyzer(AnalysisInput input)
            : base(input)
        {
            m_model = new AnalysisModel(CachedGraph);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:DoNotDisposeObjectsMultipleTimes")]
        public override int Analyze()
        {
            m_writer.Dispose();
            return 0;
        }

        public override void Prepare()
        {
            m_writer = new StreamWriter(OutputFile);
        }

        /// <inheritdoc />
        public override void FileArtifactContentDecided(FileArtifactContentDecidedEventData data)
        {
            m_model.FileContentMap.GetOrAdd((CurrentEventWorkerId, data.FileArtifact), data.FileContentInfo);
        }

        /// <inheritdoc />
        public override void ProcessFingerprintComputed(ProcessFingerprintComputationEventData data)
        {
            var semistableHash = PipTable.GetPipSemiStableHash(data.PipId);
            if (semistableHash != TargetSemistableHash)
            {
                return;
            }

            var pipInfo = m_model.GetPipInfo(data.PipId);
            pipInfo.SetFingerprintComputation(data, CurrentEventWorkerId);

            m_writer.WriteLine(I($"Fingerprint kind: {data.Kind}"));
            WriteWeakFingerprintData(pipInfo, m_writer);

            foreach (var strongComputation in data.StrongFingerprintComputations)
            {
                pipInfo.StrongFingerprintComputation = strongComputation;
                WriteStrongFingerprintData(pipInfo, m_writer);
            }
        }

        /// <inheritdoc />
        public override void DirectoryMembershipHashed(DirectoryMembershipHashedEventData data)
        {
            m_model.DirectoryData[(CurrentEventWorkerId, data.Directory, data.PipId, data.EnumeratePatternRegex)] = data;
        }

        /// <inheritdoc />
        public override void ExtraEventDataReported(ExtraEventData data)
        {
            m_model.Salts = data.ToFingerprintSalts();
        }

        private static void WriteWeakFingerprintData(PipCachingInfo info, TextWriter writer)
        {
            writer.WriteLine("Weak Fingerprint Info");
            writer.WriteLine(I($"Weak Fingerprint: {info.FingerprintComputation.WeakFingerprint}"));
            writer.WriteLine();
            writer.WriteLine("Fingerprint Text:");

            string fingerprintText;
            info.Fingerprinter.ComputeWeakFingerprint(info.GetOriginalProcess(), out fingerprintText);
            writer.WriteLine(fingerprintText);
        }

        private void WriteStrongFingerprintData(PipCachingInfo info, TextWriter writer)
        {
            writer.WriteLine("Strong Fingerprint Info");
            
            if (!info.StrongFingerprintComputation.Succeeded)
            {
                writer.WriteLine("Strong fingerprint computation failed.");
                return;
            }

            writer.WriteLine(I($"Strong Fingerprint: {info.StrongFingerprintComputation.ComputedStrongFingerprint}"));
            writer.WriteLine(I($"Found match: {info.StrongFingerprintComputation.IsStrongFingerprintHit}"));
            writer.WriteLine(I($"Computed observed inputs: {info.StrongFingerprintComputation.Succeeded}"));

            if (info.StrongFingerprintComputation.PriorStrongFingerprints.Count != 0)
            {
                writer.WriteLine("Prior Strong Fingerprints:");
                foreach (var entry in info.StrongFingerprintComputation.PriorStrongFingerprints)
                {
                    writer.WriteLine(entry);
                }
            }

            writer.WriteLine();
            var pathSetHash = info.StrongFingerprintComputation.PathSetHash.HashType != BuildXL.Cache.ContentStore.Hashing.HashType.Unknown ?
                info.StrongFingerprintComputation.PathSetHash :
                ContentHashingUtilities.ZeroHash;
            writer.WriteLine(I($"PathSet Hash: {pathSetHash}"));
            writer.WriteLine();
            writer.WriteLine("Path Set:");
            writer.WriteLine();
            foreach (var entry in info.StrongFingerprintComputation.PathEntries)
            {
                writer.WriteLine(Print(entry));
            }

            writer.WriteLine();
            writer.WriteLine("Observed Inputs:");
            writer.WriteLine();
            foreach (var observedInput in info.StrongFingerprintComputation.ObservedInputs)
            {
                writer.WriteLine(Print(observedInput));

                if (observedInput.Type == ObservedInputType.DirectoryEnumeration)
                {
                    var path = observedInput.Path;

                    if (observedInput.DirectoryEnumeration)
                    {
                        var membershipData = info.GetDirectoryMembershipData(observedInput.Path, observedInput.PathEntry.EnumeratePatternRegex);
                        if (membershipData != null)
                        {

                            writer.WriteLine($"  Flags: {string.Join(" | ", GetFlags(membershipData.Value))}");
                            writer.WriteLine($"  EnumeratePatternRegex: {membershipData.Value.EnumeratePatternRegex}");
                            writer.WriteLine($"  Members:{membershipData.Value.Members.Count}");

                            membershipData.Value.Members.Sort(PathTable.ExpandedPathComparer);
                            foreach (var item in membershipData.Value.Members)
                            {
                                string relativePath;
                                if (PathTable.TryExpandNameRelativeToAnother(path.Value, item.Value, out relativePath))
                                {
                                    writer.WriteLine("  " + relativePath);
                                }
                                else
                                {
                                    writer.WriteLine("  " + path.ToString(PathTable));
                                }
                            }
                        }
                        else
                        {
                            writer.WriteLine($"  Warning: Missing directory membership data for {path.ToString(PathTable)}");
                        }
                    }
                }
            }
        }

        public static IEnumerable<string> GetFlags(DirectoryMembershipHashedEventData dirData)
        {
            yield return dirData.IsStatic ? "static" : "dynamic";

            if (dirData.IsSearchPath)
            {
                yield return "search path";
            }
        }

        private string Print(ObservedInput arg)
        {
            return I($"{arg.Type}: {Print(arg.Path)} (Hash = {arg.Hash.ToString()}, Flags = {PrintFlags(arg.PathEntry)})");
        }

        private string Print(ObservedPathEntry arg)
        {
            return I($"{Print(arg.Path)} (Flags = {PrintFlags(arg)})");
        }

        private string Print(AbsolutePath path)
        {
            try
            {
                return path.IsValid ? path.ToString(m_model.PathTable).ToUpperInvariant() : "<Unknown>";
            }
#pragma warning disable ERP022 // TODO: This should really handle specific errors
            catch
            {
                return "<Unknown>";
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
        }

        private static string PrintFlags(ObservedPathEntry arg)
        {
            return string.Join(" | ", GetFlags(arg));
        }

        private static IEnumerable<string> GetFlags(ObservedPathEntry arg)
        {
            if (arg.IsSearchPath)
            {
                yield return "SearchPath";
            }

            if (arg.IsDirectoryPath)
            {
                yield return "DirectoryPath";
            }
        }
    }
}
