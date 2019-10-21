// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using BuildXL.Pips;
using BuildXL.Pips.Artifacts;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using Newtonsoft.Json;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeCosineJsonExport()
        {
            string jsonFilePath = null;
            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputFile", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    jsonFilePath = ParseSingletonPathOption(opt, jsonFilePath);
                }
                else
                {
                    throw Error("Unknown option for fingerprint text analysis: {0}", opt.Name);
                }
            }

            return new CosineJsonExport(GetAnalysisInput(), jsonFilePath);
        }

        private static void WriteCosineJsonExportHelp(HelpWriter writer)
        {
            writer.WriteBanner("Cosine Json Export Analysis");
            writer.WriteModeOption(nameof(AnalysisMode.ExportGraph), "Generates a JSON file containing pip graph and runtime info");
            writer.WriteOption("outputFile", "Required. The json file to create.", shortName: "o");
        }
    }

    /// <summary>
    /// Exports a JSON structured graph, including per-pip static and execution details.
    /// </summary>
    internal sealed class CosineJsonExport : Analyzer
    {
        private readonly CosineJsonWriter m_writer;

        private readonly IList<PipExecutionPerformanceEventData> m_writeExecutionEntries = new List<PipExecutionPerformanceEventData>();
        private readonly Dictionary<FileArtifact, FileContentInfo> m_fileContentMap = new Dictionary<FileArtifact, FileContentInfo>(capacity: 10 * 1000);
        private readonly Dictionary<PipId, PipExecutionDirectoryOutputs> m_directoryOutputContent = new Dictionary<PipId, PipExecutionDirectoryOutputs>();
        private readonly Dictionary<PipId, ProcessExecutionMonitoringReportedEventData> m_directoryInputContent = new Dictionary<PipId, ProcessExecutionMonitoringReportedEventData>();
        private string[] m_workers;

        /// <summary>
        /// Initializes a new instance of the <see cref="CosineJsonExport"/> class
        /// which writes text to <paramref name="output" />.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        internal CosineJsonExport(AnalysisInput input, string jsonFilePath)
            : base(input)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(jsonFilePath));

            m_writer = new CosineJsonWriter(jsonFilePath);
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            m_writer.Dispose();
            base.Dispose();
        }

        /// <summary>
        /// Exports the contents of the given pip graph. It is expected to no longer change as of the start of this call.
        /// Subsequently (and until <see cref="Finish" /> is called), this exporter will write any
        /// execution-time information sent to <see cref="ExecutionObserver" /> (observed execution may also precede this
        /// call safely, in which case it has been queued).
        /// </summary>
        public override int Analyze()
        {
            m_writer.SaveGraphData(
                CachedGraph.PipGraph,
                CachedGraph.Context,
                m_writeExecutionEntries,
                m_workers,
                m_fileContentMap,
                m_directoryInputContent,
                m_directoryOutputContent);
            return 0;
        }

        #region Log processing

        /// <inheritdoc />
        public override void FileArtifactContentDecided(FileArtifactContentDecidedEventData data)
        {
            m_fileContentMap[data.FileArtifact] = data.FileContentInfo;
        }

        /// <inheritdoc />
        public override void WorkerList(WorkerListEventData data)
        {
            m_workers = data.Workers;
        }

        /// <inheritdoc />
        public override void PipExecutionPerformance(PipExecutionPerformanceEventData data)
        {
            m_writeExecutionEntries.Add(data);
        }

        /// <inheritdoc />
        public override void PipExecutionDirectoryOutputs(PipExecutionDirectoryOutputs data)
        {
            // Just collect all the directory output data for each reported pip
            m_directoryOutputContent[data.PipId] = data;
        }

        /// <inheritdoc />
        public override void ProcessExecutionMonitoringReported(ProcessExecutionMonitoringReportedEventData data)
        {
            // Just collect all the directory output data for each reported pip
            m_directoryInputContent[data.PipId] = data;
        }

        #endregion
    }

    /// <summary>
    /// Writes a JSON structured graph, including per-pip static and execution details.
    /// </summary>
    /// <remarks>
    /// Approximate example of output:
    /// <![CDATA[
    /// {
    ///   description: "BuildXL pip graph",
    ///   date: "1/1/1970",
    ///   version: 4,
    ///   artifacts: [
    ///       {id: 1, file: "C:\dir\path"},
    ///       {id: 2, directory: "C:\dir", contents: [1]},
    ///       {id: 3, file: "C:\out\object"},
    ///       {id: 4, file: "C:\out\otherObject"}
    ///   ],
    ///   graph: [
    ///       {pipId: 1, stableId: '456def', type: 'SealDirectory', provenance: {value: 'BuildXL.FancyNamespace.Seal'},
    ///        consumes: [1], produces: [2]},
    ///       {pipId: 2, stableId: '123def', type: 'Process', exe: 'lol.exe', provenance: {value: 'BuildXL.FancyNamespace.Value'},
    ///          dependsOn: [1], consumes: [2], produces: [3]},
    ///       {pipId: 3, stableId: 'abc123', type: 'CopyFile', provenance: {value: 'BuildXL.FancyNamespace.Value'},
    ///          dependsOn: [2], consumes: [3], produces: [4]},
    ///       {pipId: 4, stableId: '123def', type: 'Process', exe: 'lol1.exe', provenance: {value: 'BuildXL.FancyNamespace.Value1'},
    ///          dependsOn: [1], consumes: [2], produces: [3], semaphores:[{name:"MySemaphore", value:1, limit:1]}
    ///   ],
    ///   execution: [
    ///       {id: 4, runLevel: 'UpToDate', startTime: 123, endTime: 456},
    ///       {id: 5, runLevel: 'Cached', startTime: 456, endTime: 460}
    ///   ],
    ///   workerDetails: [
    ///       {id: 0, name: 'local'},
    ///       {id: 1, name: 'worker1:port'},
    ///       {id: 2, name: 'worker2:port'}
    ///   ],
    ///   filedetails: [
    ///       {id: 1, length:2070192, hash:"d9b7bcfc806e493d65b309821b8460f81ad5a264" }
    ///   ]
    /// }
    /// ]]>
    ///
    /// The 'execution', 'workerDetails' and 'filedetails' sections are optional.
    /// </remarks>
    public sealed class CosineJsonWriter : IDisposable
    {
        /// <summary>
        /// Version number used in the graph preamble.
        /// </summary>
        public const int FormatVersion = 4;
        private readonly JsonTextWriter m_writer;
        private Dictionary<AbsolutePath, int> m_fileIds = new Dictionary<AbsolutePath, int>(capacity: 10 * 1000);

        /// <summary>
        /// Creates a JSON writer which writes text to <paramref name="output" />
        /// </summary>
        public CosineJsonWriter(TextWriter output)
        {
            Contract.Requires(output != null);
            m_writer = new JsonTextWriter(output);
        }

        /// <summary>
        /// Creates a JSON writer which writes text to file <paramref name="path" />
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public CosineJsonWriter(string path)
            : this(File.CreateText(path))
        {
        }

        /// <inheritdoc />
        public void Dispose()
        {
            m_writer.Close();
        }

        /// <summary>
        /// Save the graph. This method produces the whole file content. It must be called only once.
        /// </summary>
        public void SaveGraphData(
            IPipScheduleTraversal graph,
            PipExecutionContext context,
            IEnumerable<PipExecutionPerformanceEventData> executionData,
            IEnumerable<string> workers,
            IEnumerable<KeyValuePair<FileArtifact, FileContentInfo>> fileData,
            Dictionary<PipId, ProcessExecutionMonitoringReportedEventData> directoryInputContent,
            Dictionary<PipId, PipExecutionDirectoryOutputs> directoryOutputContent)
        {
            Contract.Requires(graph != null);
            Contract.Requires(context != null);
            Contract.Assume(m_fileIds.Count == 0);

            // We have transitioned to the Exporting state.
            // Observed execution info may get queued up for when we begin draining the queue (below).

            // Don't overlap pip ids with file/directory ids
            int nextId = graph.PipCount + 1;
            var directoryIds = new Dictionary<DirectoryArtifact, int>(capacity: 1000);

            m_writer.WriteStartObject(); // Outermost object

            WritePreamble();

            m_writer.WritePropertyName("artifacts");
            m_writer.WriteStartArray();

            // To save some work on pip deserialization (we are pathologically visiting every pip in the pip table),
            // we hold pips alive between the various passes.
            var pips = new List<Pip>();
            {
                var directoryPips = new List<SealDirectory>();

                // TODO: This is pretty expensive, as it loads all pips in memory
                foreach (Pip pip in graph.RetrieveScheduledPips())
                {
                    pips.Add(pip);

                    // We retrieve outputs rather than inputs since we do not expect duplicate outputs among pips
                    // (not true with inputs). This means the ID assignment work is linear in the number of artifacts.
                    PipArtifacts.ForEachOutput(pip, output =>
                    {
                        if (output.IsFile)
                        {
                            AssignFileIdAndWriteFileEntry(output.FileArtifact, context, ref nextId);
                        }

                        return true;
                    },
                    includeUncacheable: true);

                    // SealDirectory pips are the only ones with directory outputs. As part of the artifact entry for the directory output,
                    // we want to capture the membership of the directory. This means we want to have assigned IDs to all member files before
                    // writing out that list (for easier parsing). So, we defer writing out seal directory pips until all file artifacts have been visited.
                    if (pip.PipType == PipType.SealDirectory)
                    {
                        var directoryPip = (SealDirectory)pip;
                        Contract.Assume(directoryPip.IsInitialized);
                        directoryPips.Add(directoryPip);
                    }

                    // Record files that are written into SharedOpaqueDirectories
                    if (directoryOutputContent.TryGetValue(pip.PipId, out var pipOutput))
                    {
                        foreach (var output in pipOutput.DirectoryOutputs)
                        {
                            DirectoryArtifact dir = output.directoryArtifact;
                            var content = output.fileArtifactArray;

                            foreach (var file in content)
                            {
                                AssignFileIdAndWriteFileEntry(file, context, ref nextId);
                            }
                        }
                    }
                }

                foreach (SealDirectory directoryPip in directoryPips)
                {
                    AssignDirectoryIdAndWriteDirectoryEntry(directoryPip.Directory, context, directoryPip.Contents, directoryIds, ref nextId);
                }
            }

            m_writer.WriteEndArray();

            m_writer.WritePropertyName("graph");
            m_writer.WriteStartArray();
            {
                // Note we are using the 'pips' list captured above rather than possibly deserializing pips again.
                foreach (Pip pip in pips)
                {
                    if (!IncludePip(pip.PipType))
                    {
                        continue;
                    }

                    WriteGraphNodeEntry(pip, graph, context, directoryIds, directoryInputContent, directoryOutputContent);
                }
            }

            m_writer.WriteEndArray();

            // Avoid holding a reference to this map if it isn't later needed by the special file details exporter
            if (fileData == null)
            {
                m_fileIds = null;
            }

            if (executionData != null)
            {
                m_writer.WritePropertyName("execution");
                m_writer.WriteStartArray();

                // Begin draining the execution entry queue, now that we are in an allowed state for that.
                // Iteration will complete only after Finish() is called, which marks the queue as complete (no new items).
                foreach (var pipWithPerf in executionData)
                {
                    WriteExecutionEntry(pipWithPerf.PipId, pipWithPerf.ExecutionPerformance);
                }

                // End the execution: array
                m_writer.WriteEndArray();
            }

            if (workers != null)
            {
                ExportWorkerDetails(workers);
            }

            if (fileData != null)
            {
                ExportFileDetails(fileData);
            }

            // End the outermost object
            m_writer.WriteEndObject();
            m_writer.Flush();
        }

        #region Private Members

        private void ExportWorkerDetails(IEnumerable<string> workers)
        {
            Contract.Assert(workers != null);
            m_writer.WritePropertyName("workerDetails");
            m_writer.WriteStartArray();

            int id = 0;
            foreach (var worker in workers)
            {
                m_writer.WriteWhitespace(Environment.NewLine);
                m_writer.WriteStartObject();

                m_writer.WritePropertyName("id");
                m_writer.WriteValue(id);

                m_writer.WritePropertyName("name");
                m_writer.WriteValue(worker);

                m_writer.WriteEndObject();
                id++;
            }

            m_writer.WriteEndArray();
        }

        private void ExportFileDetails(IEnumerable<KeyValuePair<FileArtifact, FileContentInfo>> fileData)
        {
            Contract.Assert(fileData != null);

            m_writer.WritePropertyName("filedetails");
            m_writer.WriteStartArray();

            foreach (var kvp in fileData)
            {
                // We only care about logging files that are mentioned in the graph. Dynamically observed file hashes
                // from opaque directories are not currently represented in the graph and thus have not corresponding
                // fileid
                int fileId;
                if (m_fileIds.TryGetValue(kvp.Key.Path, out fileId))
                {
                    m_writer.WriteWhitespace(Environment.NewLine);
                    m_writer.WriteStartObject();
                    m_writer.WritePropertyName("file");
                    m_writer.WriteValue(fileId);

                    m_writer.WritePropertyName("length");
                    m_writer.WriteValue(kvp.Value.Length);

                    m_writer.WritePropertyName("hash");
                    m_writer.WriteValue(kvp.Value.Hash.ToHex());
                    m_writer.WriteEndObject();
                }
            }

            m_writer.WriteEndArray();
        }

        private void AssignFileIdAndWriteFileEntry(
            FileArtifact artifact,
            PipExecutionContext context,
            ref int nextId)
        {
            int fileId;
            if (!m_fileIds.TryGetValue(artifact.Path, out fileId))
            {
                fileId = nextId++;
                m_fileIds.Add(artifact.Path, fileId);
            }
            else
            {
                Contract.Assume(false, "Multiple producers for file artifact " + artifact.Path.ToString(context.PathTable));
                throw new InvalidOperationException("Unreachable");
            }

            m_writer.WriteWhitespace(Environment.NewLine);
            m_writer.WriteStartObject();

            m_writer.WritePropertyName("id");
            m_writer.WriteValue(fileId);

            m_writer.WritePropertyName("file");
            string path = artifact.Path.ToString(context.PathTable);
            m_writer.WriteValue(path);
            m_writer.WriteEndObject();
        }

        private void AssignDirectoryIdAndWriteDirectoryEntry(
            DirectoryArtifact artifact,
            PipExecutionContext context,
            ReadOnlyArray<FileArtifact> contents,
            Dictionary<DirectoryArtifact, int> directoryIds,
            ref int nextId)
        {
            int directoryId;
            if (!directoryIds.TryGetValue(artifact, out directoryId))
            {
                directoryId = nextId++;
                directoryIds.Add(artifact, directoryId);
            }
            else
            {
                Contract.Assume(false, "Multiple producers for directory artifact " + artifact.Path.ToString(context.PathTable));
                throw new InvalidOperationException("Unreachable");
            }

            m_writer.WriteWhitespace(Environment.NewLine);
            m_writer.WriteStartObject();

            m_writer.WritePropertyName("id");
            m_writer.WriteValue(directoryId);

            m_writer.WritePropertyName("directory");
            m_writer.WriteValue(artifact.Path.ToString(context.PathTable));

            m_writer.WritePropertyName("contents");
            m_writer.WriteStartArray();

            foreach (FileArtifact directoryMember in contents)
            {
                int dependencyId;
                if (!m_fileIds.TryGetValue(directoryMember.Path, out dependencyId))
                {
                    Contract.Assume(false, "Expected file artifact already written (member of a directory) " + directoryMember.Path.ToString(context.PathTable));
                    throw new InvalidOperationException("Unreachable");
                }

                m_writer.WriteValue(dependencyId);
            }

            m_writer.WriteEndArray();

            m_writer.WriteEndObject();
        }

        private void WritePreamble()
        {
            m_writer.WritePropertyName("description");
            m_writer.WriteValue("BuildXL Pip graph");

            m_writer.WritePropertyName("dateUtc");
            m_writer.WriteValue(DateTime.UtcNow);

            m_writer.WritePropertyName("version");
            m_writer.WriteValue(FormatVersion);
        }

        private static bool IncludePip(PipType pipType)
        {
            // HashSourceFile pips are implicitly referenced as an artifact with no producer.
            return pipType != PipType.HashSourceFile;
        }
        
        private void WriteGraphNodeEntry(
            Pip pip,
            IPipScheduleTraversal graph,
            PipExecutionContext context,
            Dictionary<DirectoryArtifact, int> directoryIds,
            Dictionary<PipId, ProcessExecutionMonitoringReportedEventData> directoryInputContent,
            Dictionary<PipId, PipExecutionDirectoryOutputs> directoryOutputContent)
        {
            Contract.Requires(pip != null);

            m_writer.WriteWhitespace(Environment.NewLine);
            m_writer.WriteStartObject();
            {
                WriteStaticPipDetails(pip, context);
                {
                    // We get nice space savings by omitting dependsOn when it is empty (think HashSourceFile and WriteFile pips).
                    bool writtenHeader = false;

                    foreach (var dependency in graph.RetrievePipImmediateDependencies(pip))
                    {
                        if (!writtenHeader)
                        {
                            m_writer.WritePropertyName("dependsOn");
                            m_writer.WriteStartArray();

                            writtenHeader = true;
                        }

                        if (IncludePip(dependency.PipType))
                        {
                            m_writer.WriteValue(dependency.PipId.Value);
                        }
                    }

                    if (writtenHeader)
                    {
                        m_writer.WriteEndArray();
                    }

                    writtenHeader = false;

                    PipArtifacts.ForEachInput(pip, dependency =>
                    {
                        int dependencyId;
                        if (dependency.IsFile)
                        {
                            if (!m_fileIds.TryGetValue(dependency.FileArtifact.Path, out dependencyId))
                            {
                                Contract.Assume(false, "Expected file artifact already written (dependency of pip) " + dependency.Path.ToString(context.PathTable));
                                throw new InvalidOperationException("Unreachable");
                            }
                        }
                        else
                        {
                            if (!directoryIds.TryGetValue(dependency.DirectoryArtifact, out dependencyId))
                            {
                                Contract.Assume(false, "Expected directory artifact already written (input of pip) " + dependency.Path.ToString(context.PathTable));
                                throw new InvalidOperationException("Unreachable");
                            }
                        }

                        if (!writtenHeader)
                        {
                            m_writer.WritePropertyName("consumes");
                            m_writer.WriteStartArray();

                            writtenHeader = true;
                        }

                        m_writer.WriteValue(dependencyId);
                        return true;
                    }, includeLazyInputs: true);
                    
                    // Write reads from shared opaque directories
                    if (directoryInputContent.TryGetValue(pip.PipId, out var pipInput))
                    {
                        foreach (var input in pipInput.ReportedFileAccesses)
                        {
                            int dependencyId;
                            if (!m_fileIds.TryGetValue(input.ManifestPath, out dependencyId))
                            {
                                // Ignore unrecognized reads (these are usually directories / files that aren't produced, so we don't have their ID)
                                continue;
                            }
                            m_writer.WriteValue(dependencyId);
                        }
                    }
                    
                    if (writtenHeader)
                    {
                        m_writer.WriteEndArray();
                    }
                }

                {
                    m_writer.WritePropertyName("produces");
                    m_writer.WriteStartArray();

                    SortedSet<int> reads = new SortedSet<int>();

                    PipArtifacts.ForEachOutput(pip, dependency =>
                    {
                        int dependencyId;
                        if (dependency.IsFile)
                        {
                            if (!m_fileIds.TryGetValue(dependency.FileArtifact.Path, out dependencyId))
                            {
                                Contract.Assume(false, "Expected file artifact already written (output of pip) " + dependency.Path.ToString(context.PathTable));
                                throw new InvalidOperationException("Unreachable");
                            }
                        }
                        else
                        {
                            if (!directoryIds.TryGetValue(dependency.DirectoryArtifact, out dependencyId))
                            {
                                Contract.Assume(false, "Expected directory artifact already written (output of pip) " + dependency.Path.ToString(context.PathTable));
                                throw new InvalidOperationException("Unreachable");
                            }
                        }

                        reads.Add(dependencyId);
                        return true;
                    }, includeUncacheable: true);

                    // Write outputs into shared opaque directories
                    if (directoryOutputContent.TryGetValue(pip.PipId, out var pipOutput))
                    {
                        foreach (var output in pipOutput.DirectoryOutputs)
                        {
                            DirectoryArtifact dir = output.directoryArtifact;
                            var content = output.fileArtifactArray;
                            
                            foreach (var file in content)
                            {
                                int dependencyId;
                                if (!m_fileIds.TryGetValue(file.Path, out dependencyId))
                                {
                                    Contract.Assume(false, "Expected file artifact not found in fileId table " + file.Path.ToString(context.PathTable));
                                    throw new InvalidOperationException("Unreachable");
                                }
                                reads.Add(dependencyId);
                            }
                        }
                    }

                    foreach (int dependencyId in reads)
                    {
                        m_writer.WriteValue(dependencyId);
                    }

                    m_writer.WriteEndArray();
                }
            }

            m_writer.WriteEndObject();
        }

        private void WriteStaticPipDetails(Pip pip, PipExecutionContext context)
        {
            Contract.Requires(pip != null);
            Contract.Requires(pip.PipType != PipType.HashSourceFile);

            m_writer.WritePropertyName("pipId");
            m_writer.WriteValue(pip.PipId.Value);

            if (pip.SemiStableHash != 0)
            {
                m_writer.WritePropertyName("stableId");

                // The X16 format agrees with e.g. PipDC41BEE27D4187E2 which is the most user-visible presently.
                // See Pip.GetDescription
                m_writer.WriteValue(pip.SemiStableHash.ToString("X16", CultureInfo.InvariantCulture));
            }

            if (pip.Provenance != null)
            {
                m_writer.WritePropertyName("provenance");
                m_writer.WriteStartObject();
                {
                    m_writer.WritePropertyName("value");
                    m_writer.WriteValue(pip.Provenance.OutputValueSymbol.ToString(context.SymbolTable));

                    m_writer.WritePropertyName("spec");
                    m_writer.WriteValue(pip.Provenance.Token.Path.ToString(context.PathTable));
                }

                m_writer.WriteEndObject();
            }

            m_writer.WritePropertyName("type");
            m_writer.WriteValue(pip.PipType.ToString());

            m_writer.WritePropertyName("description");
            m_writer.WriteValue(pip.GetDescription(context));

            var process = pip as Process;
            if (process != null)
            {
                m_writer.WritePropertyName("exe");
                m_writer.WriteValue(process.GetToolName(context.PathTable).ToString(context.StringTable));

                if (process.Semaphores != null && process.Semaphores.Length > 0)
                {
                    m_writer.WritePropertyName("semaphores");
                    m_writer.WriteStartArray();

                    foreach (ProcessSemaphoreInfo semaphore in process.Semaphores)
                    {
                        m_writer.WriteStartObject();
                        m_writer.WritePropertyName("name");
                        m_writer.WriteValue(semaphore.Name.ToString(context.StringTable));
                        m_writer.WritePropertyName("value");
                        m_writer.WriteValue(semaphore.Value);
                        m_writer.WritePropertyName("limit");
                        m_writer.WriteValue(semaphore.Limit);
                        m_writer.WriteEndObject();
                    }

                    m_writer.WriteEndArray();
                }
            }
        }

        /// <summary>
        /// Writes an execution entry.
        /// </summary>
        private void WriteExecutionEntry(PipId id, PipExecutionPerformance performance)
        {
            Contract.Requires(id.IsValid);
            Contract.Requires(performance != null);

            m_writer.WriteWhitespace(Environment.NewLine);
            m_writer.WriteStartObject();
            {
                m_writer.WritePropertyName("pipId");
                m_writer.WriteValue(id.Value);

                m_writer.WritePropertyName("runLevel");
                m_writer.WriteValue(performance.ExecutionLevel.ToString());

                m_writer.WritePropertyName("startTime");
                m_writer.WriteValue(performance.ExecutionStart.ToFileTimeUtc());

                m_writer.WritePropertyName("endTime");
                m_writer.WriteValue(performance.ExecutionStop.ToFileTimeUtc());

                m_writer.WritePropertyName("workerId");
                m_writer.WriteValue(performance.WorkerId);

                var processPerformance = performance as ProcessPipExecutionPerformance;
                if (processPerformance != null)
                {
                    m_writer.WritePropertyName("cacheFingerprint");
                    m_writer.WriteValue(processPerformance.Fingerprint.ToString());

                    if (processPerformance.CacheDescriptorId.HasValue)
                    {
                        m_writer.WritePropertyName("cacheDescriptorUniqueId");
                        m_writer.WriteValue(processPerformance.CacheDescriptorId.Value.ToString("X16", CultureInfo.InvariantCulture));
                    }

                    if (processPerformance.ProcessExecutionTime != TimeSpan.Zero)
                    {
                        m_writer.WritePropertyName("processWallTime");
                        m_writer.WriteValue(processPerformance.ProcessExecutionTime.Ticks);
                    }

                    if (processPerformance.NumberOfProcesses > 1)
                    {
                        m_writer.WritePropertyName("processCount");
                        m_writer.WriteValue(processPerformance.NumberOfProcesses);
                    }

                    if (processPerformance.UserTime != TimeSpan.Zero)
                    {
                        m_writer.WritePropertyName("user");
                        m_writer.WriteValue(processPerformance.UserTime.Ticks);
                    }

                    if (processPerformance.KernelTime != TimeSpan.Zero)
                    {
                        m_writer.WritePropertyName("kernel");
                        m_writer.WriteValue(processPerformance.KernelTime.Ticks);
                    }

                    if (processPerformance.PeakWorkingSet != 0)
                    {
                        m_writer.WritePropertyName("peakMemory");
                        m_writer.WriteValue(processPerformance.PeakWorkingSet);
                    }

                    if (processPerformance.IO.GetAggregateIO().TransferCount > 0)
                    {
                        m_writer.WritePropertyName("io");
                        m_writer.WriteStartObject();
                        {
                            if (processPerformance.IO.ReadCounters.TransferCount != 0)
                            {
                                m_writer.WritePropertyName("bytesRead");
                                m_writer.WriteValue(processPerformance.IO.ReadCounters.TransferCount);
                            }

                            if (processPerformance.IO.WriteCounters.TransferCount != 0)
                            {
                                m_writer.WritePropertyName("bytesWritten");
                                m_writer.WriteValue(processPerformance.IO.WriteCounters.TransferCount);
                            }

                            if (processPerformance.IO.OtherCounters.TransferCount != 0)
                            {
                                m_writer.WritePropertyName("bytesMisc");
                                m_writer.WriteValue(processPerformance.IO.OtherCounters.TransferCount);
                            }
                        }

                        m_writer.WriteEndObject();
                    }

                    FileMonitoringViolationCounters monitoring = processPerformance.FileMonitoringViolations;

                    if (monitoring.HasUncacheableFileAccesses)
                    {
                        m_writer.WritePropertyName("uncacheable");
                        m_writer.WriteValue(true);
                    }

                    if (monitoring.Total > 0)
                    {
                        m_writer.WritePropertyName("fileMonitoringViolations");
                        m_writer.WriteStartObject();
                        {
                            m_writer.WritePropertyName("total");
                            m_writer.WriteValue(monitoring.Total);

                            if (monitoring.TotalWhitelisted > 0)
                            {
                                m_writer.WritePropertyName("whitelisted");
                                m_writer.WriteValue(monitoring.TotalWhitelisted);
                            }

                            if (monitoring.NumFileAccessesWhitelistedButNotCacheable > 0)
                            {
                                m_writer.WritePropertyName("whitelistedButNotCacheable");
                                m_writer.WriteValue(monitoring.NumFileAccessesWhitelistedButNotCacheable);
                            }
                        }

                        m_writer.WriteEndObject();
                    }
                }
            }

            m_writer.WriteEndObject();
        }

        #endregion
    }
}
