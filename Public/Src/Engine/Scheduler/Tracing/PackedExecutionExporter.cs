// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Pips;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.ParallelAlgorithms;
#if NETCOREAPP
using BuildXL.Utilities.PackedExecution;
using BuildXL.Utilities.PackedTable;
#endif

namespace BuildXL.Scheduler.Tracing
{
    // The PackedExecution has a quite different implementation of the very similar concepts,
    // so in this analyzer we refer to them separately.
    // We use the ugly but unmissable convention of "B_" meaning "BuildXL" and "P_" meaning "PackedExecution".

    using B_PipId = Pips.PipId;
    using B_PipType = Pips.Operations.PipType;

#if NETCOREAPP
    using P_PipId = Utilities.PackedExecution.PipId;
    using P_PipTable = Utilities.PackedExecution.PipTable;
    using P_PipType = Utilities.PackedExecution.PipType;
#endif

    /// <summary>
    /// Statistics for a given export, for cross-checking against analyzer.
    /// </summary>
    /// <remarks>
    /// The fields here are deliberately all public for ease of calling Interlocked methods with refs to any field;
    /// not a great pattern in general, but adequate for this purpose.
    /// </remarks>
    [Newtonsoft.Json.JsonObject(MemberSerialization = Newtonsoft.Json.MemberSerialization.Fields | Newtonsoft.Json.MemberSerialization.OptOut)]
    public class PackedExecutionExportStatistics
    {
        /// <summary>Stat</summary>
        public int FileArtifactContentDecidedEventCount;
        /// <summary>Stat</summary>
        public int FileArtifactOutputWithKnownLengthCount;
        /// <summary>Stat</summary>
        public int ProcessFingerprintComputedEventCount;
        /// <summary>Stat</summary>
        public int ProcessFingerprintComputedExecutionCount;
        /// <summary>Stat</summary>
        public int ProcessFingerprintComputedStrongFingerprintCount;
        /// <summary>Stat</summary>
        public int ProcessFingerprintComputedConsumedPathCount;
        /// <summary>Stat</summary>
        public int PipExecutionDirectoryOutputsEventCount;
        /// <summary>Stat</summary>
        public int PipExecutionDirectoryOutputsOutputCount;
        /// <summary>Stat</summary>
        public int PipExecutionDirectoryOutputsFileCount;
        /// <summary>Stat</summary>
        public int ProcessExecutionMonitoringReportedEventCount;
        /// <summary>Stat</summary>
        public int ProcessExecutionMonitoringReportedNonNullCount;
        /// <summary>Stat</summary>
        public int ProcessExecutionMonitoringReportedProcessCount;
        /// <summary>Stat</summary>
        public int PipListCount;
        /// <summary>Stat</summary>
        public int PipDependencyCount;
        /// <summary>Stat</summary>
        public int ProcessPipInfoCount;
        /// <summary>Stat</summary>
        public int DeclaredInputFileCount;
        /// <summary>Stat</summary>
        public int DeclaredInputDirectoryCount;
        /// <summary>Stat</summary>
        public int ConsumedFileCount;
        /// <summary>Stat</summary>
        public int ConsumedFileUnknownSizeCount;
        /// <summary>Stat</summary>
        public int DecidedFileCount;
        /// <summary>Stat</summary>
        public int DecidedFileValidProducerCount;
        /// <summary>Stat</summary>
        public int DecidedFileProducerConflictCount;
    }

    /// <summary>
    /// Exports the build graph and execution data in PackedExecution format.
    /// </summary>
    /// <remarks>
    /// The PackedExecution format arranges data in large tables of unmanaged structs,
    /// enabling very fast saving and loading, and providing convenient pre-indexed
    /// relational structure.
    /// </remarks>
    public class PackedExecutionExporter : ExecutionAnalyzerBase
    {
#if NETCOREAPP
        #region Fields

        private readonly string m_outputDirectoryPath;

        /// <summary>
        /// The PackedExecution instance being built.
        /// </summary>
        private readonly PackedExecution m_packedExecution;

        /// <summary>
        /// If this is non-null, it will be locked before handling each incoming event.
        /// </summary>
        /// <remarks>
        /// When running as a post-execution XLG analyzer, the event callbacks are called serially.
        /// When running as a tracer in the build, the event callbacks are called concurrently.
        /// This object is only set in the latter scenario (and is set to this exporter itself,
        /// as it happens).
        /// </remarks>
        private readonly object m_lockObject;
   
        /// <summary>
        /// The Builder for the PackedExecution being built.
        /// </summary>
        private readonly PackedExecution.Builder m_packedExecutionBuilder;

        /// <summary>
        /// List of decided files (either materialized from cache, or produced).
        /// </summary>
        private readonly HashSet<FileArtifact> m_decidedFiles = new HashSet<FileArtifact>();

        /// <summary>
        /// Upwards index from files to their containing director(ies).
        /// </summary>
        private readonly Dictionary<FileArtifact, List<DirectoryArtifact>> m_parentOutputDirectory = new Dictionary<FileArtifact, List<DirectoryArtifact>>();

        /// <summary>
        /// Index to optimize AbsolutePath->FileId lookup.
        /// </summary>
        /// <remarks>
        /// Note that this arguably should be keyed off FileArtifact rather than AbsolutePath, but this is made
        /// difficult by the fact that the ObservedInput type (which represents pip-consumed files) has only 
        /// a Path attribute, and doesn't track the source FileArtifacts. So we have to have some way to map from
        /// path to FileId for purposes of determining the per-pip ConsumedFiles.
        /// </remarks>
        private readonly Dictionary<AbsolutePath, FileId> m_fileIndex = new Dictionary<AbsolutePath, FileId>();

        /// <summary>
        /// Index to optimize AbsolutePath->DirectoryId lookup.
        /// </summary>
        private readonly Dictionary<DirectoryArtifact, DirectoryId> m_directoryIndex = new Dictionary<DirectoryArtifact, DirectoryId>();

        /// <summary>
        /// The processor object which consumes pip execution data and analyzes it for file provenance.
        /// </summary>
        private readonly ConcurrentPipProcessor m_concurrentPipProcessor;

        /// <summary>
        /// Buffer for accessing the data of a hash as an array of ulongs.
        /// </summary>
        /// <remarks>
        /// This is 256 bits as that is the most we ever save in this format.
        /// </remarks>
        private readonly ulong[] m_hashBuffer = new ulong[4];

        /// <summary>
        /// The statistics for this run; not thread-safe, only access from a serial context.
        /// </summary>
        private readonly PackedExecutionExportStatistics m_statistics = new PackedExecutionExportStatistics();

        #endregion

#endif

        /// <summary>
        /// Handle the events from workers
        /// </summary>
        public override bool CanHandleWorkerEvents => true;

        /// <summary>
        /// Construct a PackedExecutionExporter.
        /// </summary>
        public PackedExecutionExporter(PipGraph input, string outputDirectoryPath, bool threadSafe = true)
            : base(input)
        {
#if NETCOREAPP
            m_outputDirectoryPath = outputDirectoryPath;

            m_packedExecution = new PackedExecution();
            // we'll be building these right away, not loading the tables first
            m_packedExecution.ConstructRelationTables();

            m_packedExecutionBuilder = new PackedExecution.Builder(m_packedExecution);
            m_concurrentPipProcessor = new ConcurrentPipProcessor(this);

            if (threadSafe)
            {
                m_lockObject = this;
            }
#endif
        }

#if !NETCOREAPP
        /// <inheritdoc />
        public override int Analyze()
        {
            throw new NotSupportedException("Only supported on NET core runtime");
        }
#else
        /// <summary>
        /// Log processing is complete; analyze any remaining data.
        /// </summary>
        public override int Analyze()
        {
            if (!Directory.Exists(m_outputDirectoryPath))
            {
                Directory.CreateDirectory(m_outputDirectoryPath);
            }

            var pipList = BuildPips();

            BuildPipDependencies(pipList);

            BuildProcessPipRelations();

            BuildFileProducers();

            m_packedExecutionBuilder.Complete();

            // and write it out
            m_packedExecution.SaveToDirectory(m_outputDirectoryPath);

            // and the stats
            File.WriteAllText(
                Path.Combine(m_outputDirectoryPath, "statistics.json"),
                Newtonsoft.Json.JsonConvert.SerializeObject(m_statistics, Newtonsoft.Json.Formatting.Indented));

            return 0;
        }

        #region Analyzer event handlers

        /// <summary>
        /// Get the WorkerAnalyzer instance for the current worker ID (available as a base field on the analyzer).
        /// </summary>
        /// <returns></returns>
        private ConcurrentPipProcessor GetConcurrentPipProcessor() => m_concurrentPipProcessor;

        /// <summary>
        /// Call the action from within a lock if m_lockObject is set.
        /// </summary>
        /// <remarks>
        /// The "this" object and the action's argument are passed separately, to allow the
        /// action to be static.
        /// </remarks>
        private void CallSerialized<T>(Action<T> action, T argument)
        {
            if (m_lockObject != null)
            {
                lock (m_lockObject)
                {
                    action(argument);
                }
            }
            else
            {
                action(argument);
            }
        }

        /// <summary>
        /// Handle a list of workers.
        /// </summary>
        public override void WorkerList(WorkerListEventData data) 
            => CallSerialized(WorkerListInternal, data);

        private void WorkerListInternal(WorkerListEventData data)
        {
            foreach (string workerName in data.Workers)
            {
                m_packedExecutionBuilder.WorkerTableBuilder.GetOrAdd(workerName);
            }
        }

        /// <summary>
        /// File artifact content (size, origin) is now known; create a FileTable entry for this file.
        /// </summary>
        public override void FileArtifactContentDecided(FileArtifactContentDecidedEventData data)
            => CallSerialized(FileArtifactContentDecidedInternal, data);

        private void FileArtifactContentDecidedInternal(FileArtifactContentDecidedEventData data)
        {
            m_statistics.FileArtifactContentDecidedEventCount++;
            
            if (data.FileContentInfo.HasKnownLength)
            {
                m_statistics.FileArtifactOutputWithKnownLengthCount++;

                ContentFlags contentFlags = default;
                switch (data.OutputOrigin)
                {
                    case PipOutputOrigin.DeployedFromCache:
                        contentFlags = ContentFlags.MaterializedFromCache;
                        break;
                    case PipOutputOrigin.Produced:
                        contentFlags = ContentFlags.Produced;
                        break;
                        // We ignore the following cases:
                        // PipOutputOrigin.NotMaterialized - we cannot infer the current or future materialization status of a file
                        // PipOutputOrigin.UpToDate - not relevant to this analyzer
                }

                UpdateOrAddFile(
                    data.FileArtifact,
                    data.FileContentInfo.Length,
                    contentFlags,
                    FromContentHash(data.FileContentInfo.Hash),
                    data.FileArtifact.RewriteCount);

                m_decidedFiles.Add(data.FileArtifact);
            }
        }

        /// <summary>
        /// Get a FileHash from the first 256 bits of the content hash.
        /// </summary>
        private FileHash FromContentHash(ContentHash contentHash)
        {
            for (int i = 0; i < m_hashBuffer.Length; i++)
            {
                m_hashBuffer[i] = 0;
            }

            for (int i = 0; i < Math.Min(m_hashBuffer.Length * sizeof(ulong), contentHash.ByteLength); i++)
            {
                byte nextByte = contentHash[i];
                ulong nextByteAsUlong = (ulong)nextByte;
                ulong shiftedLeft = nextByteAsUlong << (56 - (i % 8) * 8);
                m_hashBuffer[i / 8] |= shiftedLeft;
            }

            return new FileHash(m_hashBuffer);
        }

        private DirectoryId GetOrAddDirectory(DirectoryArtifact directoryArtifact, B_PipId producerPip)
            // this cast is safe because BuildPips() confirms that B_PipId and P_PipId Values are the same
            => GetOrAddDirectory(directoryArtifact, new P_PipId((int)producerPip.Value));

        private DirectoryId GetOrAddDirectory(DirectoryArtifact directoryArtifact, P_PipId producerPip)
        {
            if (m_directoryIndex.TryGetValue(directoryArtifact, out DirectoryId result))
            {
                return result;
            }
            result = m_packedExecutionBuilder.DirectoryTableBuilder.GetOrAdd(
                directoryArtifact.Path.ToString(PathTable).ToCanonicalizedPath(),
                producerPip: producerPip,
                contentFlags: default,
                isSharedOpaque: directoryArtifact.IsSharedOpaque,
                partialSealId: directoryArtifact.PartialSealId);
            m_directoryIndex[directoryArtifact] = result;
            return result;
        }

        private FileId GetOrAddFile(AbsolutePath path)
        {
            if (m_fileIndex.TryGetValue(path, out FileId result))
            {
                return result;
            }

            result = m_packedExecutionBuilder.FileTableBuilder.GetOrAdd(
                path.ToString(PathTable).ToCanonicalizedPath(),
                sizeInBytes: default,
                contentFlags: default,
                hash: default,
                rewriteCount: default);
            m_fileIndex[path] = result;
            return result;
        }

        private FileId UpdateOrAddFile(FileArtifact fileArtifact, long length, ContentFlags contentFlags, FileHash hash, int rewriteCount)
        {
            FileId result = m_packedExecutionBuilder.FileTableBuilder.UpdateOrAdd(
                fileArtifact.Path.ToString(PathTable).ToCanonicalizedPath(),
                length,
                contentFlags,
                hash,
                rewriteCount);
            m_fileIndex[fileArtifact] = result;
            return result;
        }

        /// <summary>
        /// A process fingerprint was computed; use the execution data.
        /// </summary>
        public override void ProcessFingerprintComputed(ProcessFingerprintComputationEventData data)
            => CallSerialized(ProcessFingerprintComputedInternal, data);

        private void ProcessFingerprintComputedInternal(ProcessFingerprintComputationEventData data)
        {
            m_statistics.ProcessFingerprintComputedEventCount++;
            GetConcurrentPipProcessor()?.ProcessFingerprintComputed(data);
        }

        /// <summary>
        /// The directory outputs of a pip are now known; index the directory contents
        /// </summary>
        /// <param name="data"></param>
        public override void PipExecutionDirectoryOutputs(PipExecutionDirectoryOutputs data)
            => CallSerialized(PipExecutionDirectoryOutputsInternal, data);

        private void PipExecutionDirectoryOutputsInternal(PipExecutionDirectoryOutputs data)
        {
            m_statistics.PipExecutionDirectoryOutputsEventCount++;

            foreach (var kvp in data.DirectoryOutputs)
            {
                m_statistics.PipExecutionDirectoryOutputsOutputCount++;

                DirectoryId directoryId = GetOrAddDirectory(kvp.directoryArtifact, data.PipId);

                foreach (FileArtifact fileArtifact in kvp.fileArtifactArray)
                {
                    m_statistics.PipExecutionDirectoryOutputsFileCount++;

                    // The XLG file can wind up constructing a given file instance either here or in
                    // FileArtifactContentDecided. If it publishes it in both places, that place's entry
                    // should win, which is why this calls GetOrAdd rather than UpdateOrAdd.
                    FileId fileId = GetOrAddFile(fileArtifact);

                    m_packedExecutionBuilder.DirectoryContentsBuilder.Add(directoryId, fileId);

                    // make the index from files up to containing directories
                    // TODO: when can there be more than one entry in this list?
                    if (!m_parentOutputDirectory.TryGetValue(fileArtifact, out List<DirectoryArtifact> parents))
                    {
                        parents = new List<DirectoryArtifact>();
                        m_parentOutputDirectory.Add(fileArtifact, parents);
                    }
                    parents.Add(kvp.directoryArtifact);
                }
            }
        }

        /// <summary>Collect pip performance data.</summary>
        public override void PipExecutionPerformance(PipExecutionPerformanceEventData data)
            => CallSerialized(PipExecutionPerformanceInternal, data);

        internal void PipExecutionPerformanceInternal(PipExecutionPerformanceEventData data)
        {
            if (data.ExecutionPerformance != null)
            {
                PipExecutionEntry pipEntry = new PipExecutionEntry(
                    (Utilities.PackedExecution.PipExecutionLevel)(int)data.ExecutionPerformance.ExecutionLevel,
                    data.ExecutionPerformance.ExecutionStart,
                    data.ExecutionPerformance.ExecutionStop,
                    new WorkerId((int)data.ExecutionPerformance.WorkerId + 1));

                m_packedExecutionBuilder.PipExecutionTableBuilder.Add(new P_PipId((int)data.PipId.Value), pipEntry);

                ProcessPipExecutionPerformance processPerformance = data.ExecutionPerformance as ProcessPipExecutionPerformance;

                if (processPerformance != null)
                {
                    ProcessPipExecutionEntry processPipEntry = new ProcessPipExecutionEntry(
                        new IOCounters(
                            processPerformance.IO.ReadCounters.OperationCount,
                            processPerformance.IO.ReadCounters.TransferCount,
                            processPerformance.IO.WriteCounters.OperationCount,
                            processPerformance.IO.WriteCounters.TransferCount,
                            processPerformance.IO.OtherCounters.OperationCount,
                            processPerformance.IO.OtherCounters.TransferCount),
                        processPerformance.KernelTime,
                        new MemoryCounters(
                            processPerformance.MemoryCounters.AverageCommitSizeMb,
                            processPerformance.MemoryCounters.AverageWorkingSetMb,
                            processPerformance.MemoryCounters.PeakCommitSizeMb,
                            processPerformance.MemoryCounters.PeakWorkingSetMb),
                        processPerformance.NumberOfProcesses,
                        processPerformance.ProcessExecutionTime,
                        processPerformance.ProcessorsInPercents,
                        processPerformance.SuspendedDurationMs,
                        processPerformance.UserTime);

                    m_packedExecutionBuilder.ProcessPipExecutionTableBuilder.Add(new P_PipId((int)data.PipId.Value), processPipEntry);
                }
            }
        }

        /// <summary>Collect process performance data.</summary>
        public override void ProcessExecutionMonitoringReported(ProcessExecutionMonitoringReportedEventData data)
            => CallSerialized(ProcessExecutionMonitoringReportedInternal, data);

        private void ProcessExecutionMonitoringReportedInternal(ProcessExecutionMonitoringReportedEventData data)
        {
            m_statistics.ProcessExecutionMonitoringReportedEventCount++;

            if (data.ReportedProcesses != null)
            {
                m_statistics.ProcessExecutionMonitoringReportedNonNullCount++;

                foreach (var reportedProcess in data.ReportedProcesses)
                {
                    m_statistics.ProcessExecutionMonitoringReportedProcessCount++;

                    ProcessExecutionEntry processEntry = new ProcessExecutionEntry(
                        reportedProcess.CreationTime,
                        reportedProcess.ExitCode,
                        reportedProcess.ExitTime,
                        new IOCounters(
                            reportedProcess.IOCounters.ReadCounters.OperationCount,
                            reportedProcess.IOCounters.ReadCounters.TransferCount,
                            reportedProcess.IOCounters.WriteCounters.OperationCount,
                            reportedProcess.IOCounters.WriteCounters.TransferCount,
                            reportedProcess.IOCounters.OtherCounters.OperationCount,
                            reportedProcess.IOCounters.OtherCounters.TransferCount),
                        reportedProcess.KernelTime,
                        reportedProcess.ParentProcessId,
                        m_packedExecutionBuilder.FileTableBuilder.PathTableBuilder.GetOrAdd(reportedProcess.Path),
                        reportedProcess.ProcessId,
                        reportedProcess.UserTime);

                    m_packedExecutionBuilder.ProcessExecutionTableBuilder.Add(new P_PipId((int)data.PipId.Value), processEntry);
                }
            }
        }

        #endregion

        #region Building

        private List<PipReference> BuildPips()
        {
            List<PipReference> pipList =
                PipGraph.AsPipReferences(PipTable.StableKeys, PipQueryContext.PipGraphRetrieveAllPips).ToList();

            // Populate the PipTable with all known PipIds, defining a mapping from the BXL PipIds to the PackedExecution PipIds.
            for (int i = 0; i < pipList.Count; i++)
            {
                // Each pip gets a P_PipId that is one plus its index in this list, since zero is never a PackedExecution ID value.
                // This seems to be exactly how B_PipId works as well, but we verify to be sure we can rely on this invariant.
                P_PipId graphPipId = AddPip(pipList[i], m_packedExecutionBuilder.PipTableBuilder);
                if (graphPipId.Value != pipList[i].PipId.Value)
                {
                    throw new ArgumentException($"Graph pip ID {graphPipId.Value} does not equal BXL pip ID {pipList[i].PipId.Value}");
                }
            }

            return pipList;
        }

        /// <summary>
        /// Add this pip's informationn to the graph.
        /// </summary>
        public P_PipId AddPip(PipReference pipReference, P_PipTable.Builder pipBuilder)
        {
            Pip pip = pipReference.HydratePip();
            string pipName = GetDescription(pip).Replace(", ", ".");
            // strip the pip hash from the start of the description
            if (pipName.StartsWith("Pip"))
            {
                int firstDotIndex = pipName.IndexOf('.');
                if (firstDotIndex != -1)
                {
                    pipName = pipName.Substring(firstDotIndex + 1);
                }
            }
            P_PipType pipType = (P_PipType)(int)pip.PipType;

            long semiStableHash = PipTable.GetPipSemiStableHash(pip.PipId);
            P_PipId g_pipId = pipBuilder.Add(semiStableHash, pipName, pipType);

            return g_pipId;
        }

        private void BuildPipDependencies(List<PipReference> pipList)
        {
            // and now do it again with the dependencies, now that everything is established.
            // Since we added all the pips in pipList order to PipTable, we can traverse them again in the same order
            // to build the relation.
            SpannableList<P_PipId> buffer = new SpannableList<P_PipId>(); // to accumulate the IDs we add to the relation

            m_statistics.PipListCount = pipList.Count;

            for (int i = 0; i < pipList.Count; i++)
            {
                IEnumerable<P_PipId> pipDependencies = PipGraph
                    .RetrievePipReferenceImmediateDependencies(pipList[i].PipId, null)
                    .Where(pipRef => pipRef.PipType != B_PipType.HashSourceFile)
                    .Select(pid => pid.PipId.Value)
                    .Distinct()
                    .OrderBy(pid => pid)
                    .Select(pid => new P_PipId((int)pid));

                // reuse the buffer for span purposes
                buffer.Clear();
                buffer.AddRange(pipDependencies);

                m_statistics.PipDependencyCount += buffer.Count;

                // don't need to use a builder here, we're adding all dependencies in PipId order
                m_packedExecution.PipDependencies.Add(buffer.AsSpan());
            }
        }

        private void BuildProcessPipRelations()
        {
            m_concurrentPipProcessor.Complete();

            foreach (ConcurrentPipProcessor.ProcessPipInfo processPipInfo in m_concurrentPipProcessor.ProcessPipInfoList)
            {
                m_statistics.ProcessPipInfoCount++;

                foreach (FileArtifact declaredInputFile in processPipInfo.DeclaredInputFiles)
                {
                    m_statistics.DeclaredInputFileCount++;

                    FileId fileId = GetOrAddFile(declaredInputFile);
                    m_packedExecutionBuilder.DeclaredInputFilesBuilder.Add(processPipInfo.PipId, fileId);
                }

                foreach (DirectoryArtifact directoryArtifact in processPipInfo.DeclaredInputDirectories)
                {
                    m_statistics.DeclaredInputDirectoryCount++;

                    DirectoryId directoryId = GetOrAddDirectory(directoryArtifact, processPipInfo.PipId);
                    m_packedExecutionBuilder.DeclaredInputDirectoriesBuilder.Add(processPipInfo.PipId, directoryId);
                }

                foreach (AbsolutePath consumedInputPath in processPipInfo.ConsumedFiles)
                {
                    m_statistics.ConsumedFileCount++;

                    FileId fileId = GetOrAddFile(consumedInputPath);

                    if (m_packedExecution.FileTable[fileId].SizeInBytes == 0)
                    {
                        m_statistics.ConsumedFileUnknownSizeCount++;
                    }

                    m_packedExecutionBuilder.ConsumedFilesBuilder.Add(processPipInfo.PipId, fileId);
                }
            }
        }

        /// <summary>
        /// Build the FileProducer relation.
        /// </summary>
        private void BuildFileProducers()
        {
            m_packedExecution.FileProducer.FillToBaseTableCount();

            // Object we lock when checking for duplicate producers
            object localLock = new object();

            m_statistics.DecidedFileCount = m_decidedFiles.Count;

            // set file producers, concurrently.
            Parallel.ForEach(
                m_decidedFiles,
                new ParallelOptions() { MaxDegreeOfParallelism = Environment.ProcessorCount },
                fileArtifact =>
                {
                    // try to find a producer of this file
                    var producer = PipGraph.TryGetProducer(fileArtifact);
                    if (!producer.IsValid)
                    {
                        // it's not a statically declared file, so it must be produced as a part of an output directory
                        if (m_parentOutputDirectory.TryGetValue(fileArtifact, out var containingDirectories))
                        {
                            foreach (var directory in containingDirectories)
                            {
                                producer = PipGraph.TryGetProducer(directory);
                                if (producer.IsValid)
                                {
                                    break;
                                }
                            }
                        }
                    }

                    if (producer.IsValid)
                    {
                        // Now that we know who it was (the time-consuming part), take a lock before checking/modifying the producer table.
                        lock (localLock)
                        {
                            m_statistics.DecidedFileValidProducerCount++;

                            FileId fileId = GetOrAddFile(fileArtifact);

                            // Theoretically this is safe if we never update the same file twice with different producers.
                            // BXL: should we have an interlock to guard against this?
                            // BXL: can a file have multiple producers legitimately? (PipGraph has some kind of method for getting all producers of a file)
                            P_PipId currentProducer = m_packedExecution.FileProducer[fileId];
                            if (currentProducer != default)
                            {
                                m_statistics.DecidedFileProducerConflictCount++;
                            }

                            m_packedExecution.FileProducer[fileId] = new P_PipId((int)producer.Value);
                        }
                    }
                });
        }

        #endregion

        /// <summary>
        /// Implement some asynchronous, thread-pooled processing of incoming data about processed pips.
        /// </summary>
        /// <remarks>
        /// Finding the producer pip for a file is evidently expensive and shouldn't be done on the execution log event thread.
        /// </remarks>
        private class ConcurrentPipProcessor
        {
            /// <summary>The information about an executed process pip.</summary>
            /// <remarks>
            /// TODO: Consider making this less object-y and more columnar... store all the declared input files
            /// in just one big list indexed by PipId, etc.
            /// </remarks>
            public struct ProcessPipInfo
            {
                /// <summary>The pip ID.</summary>
                public readonly P_PipId PipId;
                /// <summary>The numeric semistable hash.</summary>
                public readonly long SemiStableHash;
                /// <summary>Input files declared for the pip.</summary>
                public readonly ICollection<FileArtifact> DeclaredInputFiles;
                /// <summary>Input directories declared for the pip.</summary>
                public readonly ICollection<DirectoryArtifact> DeclaredInputDirectories;
                /// <summary>Consumed files declared for the pip.</summary>
                public readonly ICollection<AbsolutePath> ConsumedFiles;

                /// <summary>
                /// Construct a ProcessPipInfo.
                /// </summary>
                public ProcessPipInfo(
                    P_PipId pipId, 
                    long semiStableHash,
                    ICollection<FileArtifact> declaredInputFiles,
                    ICollection<DirectoryArtifact> declaredInputDirs,
                    ICollection<AbsolutePath> consumedFiles)
                {
                    PipId = pipId;
                    SemiStableHash = semiStableHash;
                    DeclaredInputFiles = declaredInputFiles;
                    DeclaredInputDirectories = declaredInputDirs;
                    ConsumedFiles = consumedFiles;
                }
            }

            /// <summary>
            /// The parent exporter.
            /// </summary>
            private readonly PackedExecutionExporter m_exporter;

            /// <summary>
            /// This analyzer's concurrency manager.
            /// </summary>
            private readonly ActionBlockSlim<ProcessFingerprintComputationEventData> m_processingBlock;

            /// <summary>
            /// Each WorkerAnalyzer collects its own partial relations and merges them when complete.
            /// </summary>
            public readonly List<ProcessPipInfo> ProcessPipInfoList = new List<ProcessPipInfo>();

            /// <summary>
            /// The machine name of this worker.
            /// </summary>
            public string Name { get; }

            /// <summary>
            /// Construct a WorkerAnalyzer.
            /// </summary>
            public ConcurrentPipProcessor(PackedExecutionExporter exporter)
            {
                m_exporter = exporter;
                m_processingBlock =  ActionBlockSlim.Create<ProcessFingerprintComputationEventData>(
                    degreeOfParallelism: -1, // default
                    processItemAction: ProcessFingerprintComputedCore);
            }

            /// <summary>
            /// Complete processing of this WorkerAnalyzer; block on any remaining concurrent work's completion.
            /// </summary>
            public void Complete()
            {
                m_processingBlock.Complete();
            }

            /// <summary>
            /// Handle the incoming fingerprint computation (by queueing it for processing by separate thread).
            /// </summary>
            public void ProcessFingerprintComputed(ProcessFingerprintComputationEventData data)
            {
                m_processingBlock.Post(data);
            }

            private static readonly ICollection<AbsolutePath> s_noPaths = new List<AbsolutePath>();

            /// <summary>
            /// Really handle the incoming fingerprint computation.
            /// </summary>
            /// <remarks>
            /// This is a concurrent method, not a serial method, so beware of shared mutable state.
            /// </remarks>
            internal void ProcessFingerprintComputedCore(ProcessFingerprintComputationEventData data)
            {
                var pip = m_exporter.GetPip(data.PipId) as Process;
                Contract.Assert(pip != null);

                P_PipId packedPipId = new P_PipId((int)data.PipId.Value);

                if (data.Kind != FingerprintComputationKind.Execution)
                {
                    return;
                }

                Interlocked.Increment(ref m_exporter.m_statistics.ProcessFingerprintComputedExecutionCount);

                // part 1: collect requested inputs
                // count only output files/directories
                // TODO: use a builder here? 400K or so objects seems livable though....
                var declaredInputFiles = pip.Dependencies.ToList();
                var declaredInputDirs = pip.DirectoryDependencies.ToList();

                // part 2: collect observed input paths
                IEnumerable<AbsolutePath> observedInputPaths = data.StrongFingerprintComputations.Count == 0
                    ? s_noPaths
                    : data
                        .StrongFingerprintComputations[0]
                        .ObservedInputs
                        .Where(input => input.Type == ObservedInputType.FileContentRead || input.Type == ObservedInputType.ExistingFileProbe)
                        .Select(input => input.Path);
                // The ObservedInputs list has had the "declared inputs" explicitly removed.
                // To BuildXL they are assumed to be consumed, but they are in the weak fingerprint, not in the strong fingerprint.
                // There is no way at this location to know whether the declared inputs were actually consumed,
                // but since a pip will be run based on a declared input changing, it's less misleading to include them.
                ICollection<AbsolutePath> consumedPaths = declaredInputFiles.Select(x => x.Path).Union(observedInputPaths).ToList();

                Interlocked.Add(
                    ref m_exporter.m_statistics.ProcessFingerprintComputedStrongFingerprintCount,
                    data.StrongFingerprintComputations.Count);
                Interlocked.Add(
                    ref m_exporter.m_statistics.ProcessFingerprintComputedConsumedPathCount, 
                    consumedPaths.Count);

                lock (ProcessPipInfoList)
                {
                    ProcessPipInfoList.Add(new ProcessPipInfo(
                        packedPipId,
                        pip.SemiStableHash,
                        declaredInputFiles,
                        declaredInputDirs,
                        consumedPaths));
                }
            }
        }
#endif
    }
}
