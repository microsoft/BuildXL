// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Xml;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
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
        public Analyzer InitializeDumpProcessAnalyzer()
        {
            string dumpFilePath = null;
            bool compress = false;
            long? semistableHash = null;
            PipId? pipId = null;
            Dictionary<string, string> roots = new Dictionary<string, string>();
            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputFile", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    dumpFilePath = ParseSingletonPathOption(opt, dumpFilePath);
                }
                else if (opt.Name.StartsWith("compress", StringComparison.OrdinalIgnoreCase))
                {
                    compress = ParseBooleanOption(opt);
                }
                else if (opt.Name.StartsWith("pip", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.StartsWith("p", StringComparison.OrdinalIgnoreCase))
                {
                    semistableHash = ParseSemistableHash(opt);
                }
                else if (opt.Name.StartsWith("root", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.StartsWith("r", StringComparison.OrdinalIgnoreCase))
                {
                    ParseKeyValueOption(roots, opt);
                }
                else
                {
                    throw Error("Unknown option for dump process text analysis: {0}", opt.Name);
                }
            }

            var input = GetAnalysisInput();
            if (semistableHash.HasValue)
            {
                pipId = input.CachedGraph.PipTable.StableKeys.Single(id => input.CachedGraph.PipTable.GetPipSemiStableHash(id) == semistableHash);
            }

            return new DumpProcessAnalyzer(input)
            {
                DumpFilePath = dumpFilePath,
                CompressFile = compress,
                TargetSemiStableHash = semistableHash,
                Roots = roots,
                TargetPipId = pipId
            };
        }

        private static void WriteDumpProcessAnalyzerHelp(HelpWriter writer)
        {
            writer.WriteBanner("DumpProcess Analysis");
            writer.WriteModeOption(nameof(AnalysisMode.DumpProcess), "Generates a text file containing process input descriptions");
            writer.WriteOption("outputFile", "Required. The path to the output file.", shortName: "o");
            writer.WriteOption("compress", "Optional. Indicates whether the output should be a compressed zip file.");
            writer.WriteOption("root", "Multiple. Optional. Root replacement.");
        }
    }

    /// <summary>
    /// Analyzer used to generate dump of process command lines and environment variables text file
    /// </summary>
    internal sealed class DumpProcessAnalyzer : Analyzer
    {
        /// <summary>
        /// The path to the dump file
        /// </summary>
        public string DumpFilePath;
        public bool CompressFile;
        public long? TargetSemiStableHash;
        public Dictionary<string, string> Roots;
        public PipId? TargetPipId;

        private readonly ConcurrentBigMap<FileArtifact, ContentHash> m_fileHashes = new ConcurrentBigMap<FileArtifact, ContentHash>();
        private readonly ConcurrentBigMap<FileArtifact, FileContentInfo> m_fileInfos = new ConcurrentBigMap<FileArtifact, FileContentInfo>();
        private readonly ConcurrentBigMap<DirectoryArtifact, IReadOnlyList<FileArtifact>> m_contents = new ConcurrentBigMap<DirectoryArtifact, IReadOnlyList<FileArtifact>>();
        private readonly ConcurrentBigMap<long, HashSet<AbsolutePath>> m_observedInputs = new ConcurrentBigMap<long, HashSet<AbsolutePath>>();

        public DumpProcessAnalyzer(AnalysisInput input)
            : base(input)
        {
        }

        public override void FileArtifactContentDecided(FileArtifactContentDecidedEventData data)
        {
            m_fileHashes[data.FileArtifact] = data.FileContentInfo.Hash;
            m_fileInfos[data.FileArtifact] = data.FileContentInfo;
        }

        public override void PipExecutionDirectoryOutputs(PipExecutionDirectoryOutputs data)
        {
            foreach (var item in data.DirectoryOutputs)
            {
                m_contents[item.directoryArtifact] = item.fileArtifactArray;
            }
        }

        public override void ObservedInputs(ObservedInputsEventData data)
        {
            if (!TargetPipId.HasValue || data.PipId == TargetPipId)
            {
                 m_observedInputs[data.PipId.Value] = new HashSet<AbsolutePath>(data.ObservedInputs.Where(oi => !oi.IsDirectoryPath).Select(oi => oi.Path));
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:DoNotDisposeObjectsMultipleTimes")]
        public override int Analyze()
        {
            var rootExpander = new RootExpander(PathTable);

            HashSet<ContentHash> hashes = new HashSet<ContentHash>();
            hashes.Add(ContentHashingUtilities.ZeroHash);

            HashSet<FileArtifact> files = new HashSet<FileArtifact>();

            foreach (var root in Roots)
            {
                rootExpander.Add(AbsolutePath.Create(PathTable, root.Key), root.Value);
            }

            Func<AbsolutePath, string> expandRoot = absPath => absPath.ToString(PathTable, rootExpander);

            var orderedPips = CachedGraph.PipGraph.RetrievePipReferencesOfType(PipType.Process)
                .Where(lazyPip => TargetSemiStableHash == null || TargetSemiStableHash == lazyPip.SemiStableHash)
                .Select(lazyPip => (Process)lazyPip.HydratePip())
                .ToLookup(process => process.Provenance.Token.Path)
                .OrderBy(grouping => grouping.Key.ToString(PathTable, rootExpander))
                .ToList();

            using (var fingerprintStream = File.Create(DumpFilePath, bufferSize: 64 << 10 /* 64 KB */))
            using (var hashWriter = new StreamWriter(DumpFilePath + ".hashes.txt"))
            {
                using (
                    var fingerprintArchive = CompressFile
                        ? new ZipArchive(fingerprintStream, ZipArchiveMode.Create)
                        : null)
                {
                    using (
                        var writer =
                            XmlWriter.Create(
                                CompressFile
                                    ? fingerprintArchive.CreateEntry("dump.xml", CompressionLevel.Fastest).Open()
                                    : fingerprintStream, new XmlWriterSettings() { Indent = true }))
                    {
                        int doneProcesses = 0;
                        var t = new Timer(
                            o =>
                            {
                                var done = doneProcesses;
                                Console.WriteLine("Processes Done: {0} of {1}", done, orderedPips.Count);
                            },
                            null,
                            5000,
                            5000);

                        try
                        {
                            writer.WriteStartElement("ProcessDump");
                            writer.WriteAttributeString("Count", orderedPips.Count.ToString(CultureInfo.InvariantCulture));

                            foreach (var specPipGroup in orderedPips)
                            {
                                writer.WriteStartElement("SpecFile");
                                writer.WriteAttributeString("Path", specPipGroup.Key.ToString(PathTable, rootExpander));

                                foreach (var pip in specPipGroup)
                                {
                                    doneProcesses++;

                                    writer.WriteStartElement("Process");
                                    writer.WriteAttributeString("Name", pip.Executable.Path.ToString(PathTable, rootExpander));
                                    writer.WriteAttributeString("CMD", RenderProcessArguments(pip));
                                    writer.WriteElementString("Description", pip.GetDescription(PipGraph.Context));

                                    writer.WriteStartElement("EnvironmentVariables");
                                    foreach (var environmentVariable in pip.EnvironmentVariables)
                                    {
                                        writer.WriteStartElement("Environment");
                                        writer.WriteAttributeString("Name", environmentVariable.Name.ToString(PathTable.StringTable));
                                        if (environmentVariable.Value.IsValid)
                                        {
                                            writer.WriteAttributeString("Value", environmentVariable.Value.ToString(expandRoot, PathTable.StringTable, PipData.MaxMonikerRenderer));
                                        }
                                        else
                                        {
                                            writer.WriteAttributeString("Value", "Unset");
                                        }

                                        writer.WriteEndElement();
                                    }

                                    writer.WriteEndElement();

                                    writer.WriteStartElement("Dependencies");
                                    foreach (var input in pip.Dependencies)
                                    {
                                        writer.WriteStartElement("Item");
                                        writer.WriteAttributeString("Path", input.Path.ToString(PathTable, rootExpander));
                                        writer.WriteAttributeString("Hash", m_fileHashes.GetOrAdd(input, ContentHashingUtilities.ZeroHash).Item.Value.ToString());
                                        writer.WriteAttributeString("RewriteCount", input.RewriteCount.ToString());
                                        writer.WriteEndElement();
                                    }

                                    writer.WriteEndElement();

                                    writer.WriteStartElement("DirectoryDependencies");
                                    foreach (var input in pip.DirectoryDependencies)
                                    {
                                        writer.WriteStartElement("Item");
                                        writer.WriteAttributeString("Path", input.Path.ToString(PathTable, rootExpander));
                                        var kind = PipTable.GetSealDirectoryKind(PipGraph.GetSealedDirectoryNode(input).ToPipId());
                                        writer.WriteAttributeString("Kind", kind.ToString());

                                        // Print directory dependency file details when dumping a specific process
                                        if (TargetSemiStableHash != null && (kind == SealDirectoryKind.Full || kind == SealDirectoryKind.Partial))
                                        {
                                            foreach (var file in PipGraph.ListSealedDirectoryContents(input))
                                            {
                                                writer.WriteStartElement("Item");
                                                writer.WriteAttributeString("Path", file.Path.ToString(PathTable, rootExpander));
                                                writer.WriteAttributeString("Hash", m_fileHashes.GetOrAdd(file, ContentHashingUtilities.ZeroHash).Item.Value.ToString());
                                                writer.WriteAttributeString("RewriteCount", file.RewriteCount.ToString());
                                                writer.WriteEndElement();
                                            }
                                        }
                                        else if (m_contents.TryGetValue(input, out var contents))
                                        {
                                            m_observedInputs.TryGetValue(pip.PipId.Value, out var observedInputs);

                                            foreach (var file in contents)
                                            {
                                                // skip the files that were not accessed
                                                if (observedInputs != null && !observedInputs.Contains(file.Path))
                                                {
                                                    continue;
                                                }

                                                writer.WriteStartElement("Item");
                                                writer.WriteAttributeString("Path", file.Path.ToString(PathTable, rootExpander));
                                                writer.WriteAttributeString("Hash", m_fileHashes.GetOrAdd(file, ContentHashingUtilities.ZeroHash).Item.Value.ToString());
                                                writer.WriteAttributeString("RewriteCount", file.RewriteCount.ToString());
                                                writer.WriteEndElement();
                                            }
                                        }

                                        writer.WriteEndElement();
                                    }

                                    writer.WriteEndElement();

                                    writer.WriteStartElement("Outputs");
                                    foreach (var input in pip.FileOutputs)
                                    {
                                        writer.WriteStartElement("Item");

                                        if (input.RewriteCount > 1)
                                        {
                                            writer.WriteAttributeString("RewriteCount", input.RewriteCount.ToString());
                                        }

                                        writer.WriteString(input.Path.ToString(PathTable, rootExpander));

                                        writer.WriteEndElement();
                                    }

                                    writer.WriteEndElement();

                                    writer.WriteStartElement("DirectoryOutputs");
                                    foreach (var output in pip.DirectoryOutputs)
                                    {
                                        writer.WriteStartElement("Directory");
                                        {
                                            writer.WriteAttributeString("Path", output.Path.ToString(PathTable, rootExpander));
                                            if (m_contents.TryGetValue(output, out var contents))
                                            {
                                                writer.WriteStartElement("Contents");
                                                {
                                                    foreach (var file in contents)
                                                    {
                                                        writer.WriteStartElement("Item");

                                                        if (file.RewriteCount > 1)
                                                        {
                                                            writer.WriteAttributeString("RewriteCount", file.RewriteCount.ToString());
                                                        }

                                                        writer.WriteString(file.Path.ToString(PathTable, rootExpander));

                                                        writer.WriteEndElement();
                                                    }
                                                }
                                                writer.WriteEndElement();
                                            }
                                        }
                                        writer.WriteEndElement();
                                    }

                                    writer.WriteEndElement();

                                    if (pip.TempDirectory.IsValid)
                                    {
                                        writer.WriteElementString("TempDirectory", pip.TempDirectory.ToString(PathTable, rootExpander));
                                    }

                                    writer.WriteStartElement("AdditionalTempDirectories");
                                    foreach (var item in pip.AdditionalTempDirectories)
                                    {
                                        writer.WriteElementString("Item", item.ToString(PathTable, rootExpander));
                                    }

                                    writer.WriteEndElement();

                                    writer.WriteEndElement(); // Process
                                }

                                writer.WriteEndElement(); // SpecFile
                            }

                            writer.WriteEndElement(); // ProcessDump
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
    }
}
