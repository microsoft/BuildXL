// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Processes;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Scheduler.Distribution
{
    /// <summary>
    /// Handles serialization of process execution results for distributed builds
    /// </summary>
    internal sealed class ExecutionResultSerializer
    {
        private readonly int m_maxSerializableAbsolutePathIndex;
        private readonly PipExecutionContext m_executionContext;

        private readonly Func<BuildXLReader, AbsolutePath> m_readPath;
        private readonly Action<BuildXLWriter, AbsolutePath> m_writePath;

        #region Reader Pool

        private readonly ObjectPool<BuildXLReader> m_readerPool = new ObjectPool<BuildXLReader>(CreateReader, (Action<BuildXLReader>)CleanupReader);

        private static void CleanupReader(BuildXLReader reader)
        {
            reader.BaseStream.SetLength(0);
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope", Justification = "Disposal is not needed for memory stream")]
        private static BuildXLReader CreateReader()
        {
            return new BuildXLReader(
                debug: false,
                stream: new MemoryStream(),
                leaveOpen: false);
        }

        #endregion Reader Pool

        private static readonly ObjectPool<Dictionary<ReportedProcess, int>> s_reportedProcessMapPool = new ObjectPool<Dictionary<ReportedProcess, int>>(
            creator: () => new Dictionary<ReportedProcess, int>(),
            cleanup: d => d.Clear());

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="maxSerializableAbsolutePathIndex">the minimum absolute path that can be shared amongst participants in a build</param>
        /// <param name="executionContext">the execution context</param>
        public ExecutionResultSerializer(int maxSerializableAbsolutePathIndex, PipExecutionContext executionContext)
        {
            m_maxSerializableAbsolutePathIndex = maxSerializableAbsolutePathIndex;
            m_executionContext = executionContext;

            m_readPath = ReadAbsolutePath;
            m_writePath = WriteAbsolutePath;
        }

        /// <summary>
        /// Deserialize result from the byte array segment
        /// </summary>
        public ExecutionResult DeserializeFromBlob(ArraySegment<byte> blobData, uint workerId)
        {
            using (var pooledReader = m_readerPool.GetInstance())
            {
                var reader = pooledReader.Instance;
                reader.BaseStream.Write(blobData.Array, blobData.Offset, blobData.Count);
                reader.BaseStream.Position = 0;

                return Deserialize(reader, workerId);
            }
        }

        /// <summary>
        /// Deserialize result from reader
        /// </summary>
        public ExecutionResult Deserialize(BuildXLReader reader, uint workerId)
        {
            int minAbsolutePathValue = reader.ReadInt32();
            Contract.Assert(
                minAbsolutePathValue == m_maxSerializableAbsolutePathIndex,
                "When deserializing for distribution, the minimum absolute path value must match the serialized minimum absolute path value");

            var result = (PipResultStatus)reader.ReadByte();
            var converged = reader.ReadBoolean();
            var numberOfWarnings = reader.ReadInt32Compact();
            var weakFingerprint = reader.ReadNullableStruct(r => r.ReadWeakFingerprint());
            ProcessPipExecutionPerformance performanceInformation;

            if (reader.ReadBoolean())
            {
                performanceInformation = ProcessPipExecutionPerformance.Deserialize(reader);

                // TODO: It looks like this is the wrong class for WorkerId, because the serialized object has always WorkerId == 0.
                performanceInformation.WorkerId = workerId;
            }
            else
            {
                performanceInformation = null;
            }

            var outputContent = ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.FromWithoutCopy(ReadOutputContent(reader));
            var directoryOutputs = ReadOnlyArray<(DirectoryArtifact, ReadOnlyArray<FileArtifact>)>.FromWithoutCopy(ReadDirectoryOutputs(reader));
            var mustBeConsideredPerpetuallyDirty = reader.ReadBoolean();
            var dynamicallyObservedFiles = reader.ReadReadOnlyArray(ReadAbsolutePath);
            var dynamicallyObservedEnumerations = reader.ReadReadOnlyArray(ReadAbsolutePath);
            var allowedUndeclaredSourceReads = reader.ReadReadOnlySet(ReadAbsolutePath);
            var absentPathProbesUnderOutputDirectories = reader.ReadReadOnlySet(ReadAbsolutePath);

            ReportedFileAccess[] fileAccessViolationsNotWhitelisted;
            ReportedFileAccess[] whitelistedFileAccessViolations;
            ReportedProcess[] reportedProcesses;
            ReadReportedProcessesAndFileAccesses(reader, out fileAccessViolationsNotWhitelisted, out whitelistedFileAccessViolations, out reportedProcesses, readPath: m_readPath);

            var twoPhaseCachingInfo = ReadTwoPhaseCachingInfo(reader);
            var cacheDescriptor = ReadPipCacheDescriptor(reader);
            if (cacheDescriptor != null)
            {
                cacheDescriptor.OutputContentReplicasMiniBloomFilter = reader.ReadUInt32();
            }

            ObservedPathSet? pathSet = null;
            if (reader.ReadBoolean())
            {
                var maybePathSet = ObservedPathSet.TryDeserialize(m_executionContext.PathTable, reader, pathReader: ReadAbsolutePath);
                pathSet = maybePathSet.Succeeded
                    ? (ObservedPathSet?)maybePathSet.Result
                    : null;
            }

            CacheLookupPerfInfo cacheLookupCounters = null;
            if (reader.ReadBoolean())
            {
                cacheLookupCounters = CacheLookupPerfInfo.Deserialize(reader);
            }

            if (!result.IndicatesFailure())
            {
                // Successful result needs to be changed to not materialized because
                // the process outputs are not materialized on the local machine.
                result = PipResultStatus.NotMaterialized;
            }

            var processExecutionResult = ExecutionResult.CreateSealed(
                result,
                numberOfWarnings,
                outputContent,
                directoryOutputs,
                performanceInformation,
                weakFingerprint,
                fileAccessViolationsNotWhitelisted,
                whitelistedFileAccessViolations,
                mustBeConsideredPerpetuallyDirty,
                dynamicallyObservedFiles,
                dynamicallyObservedEnumerations,
                allowedUndeclaredSourceReads,
                absentPathProbesUnderOutputDirectories,
                twoPhaseCachingInfo,
                cacheDescriptor,
                converged,
                pathSet,
                cacheLookupCounters);

            return processExecutionResult;
        }

        /// <summary>
        /// Serialize result to writer
        /// </summary>
        public void Serialize(BuildXLWriter writer, ExecutionResult result)
        {
            writer.Write(m_maxSerializableAbsolutePathIndex);

            writer.Write((byte)result.Result);
            writer.Write(result.Converged);
            writer.WriteCompact(result.NumberOfWarnings);
            writer.Write(result.WeakFingerprint, (w, weak) => w.Write(weak));

            var performanceInformation = result.PerformanceInformation;

            if (performanceInformation != null)
            {
                writer.Write(true);
                performanceInformation.Serialize(writer);
            }
            else
            {
                writer.Write(false);
            }

            WriteOutputContent(writer, result.OutputContent);
            WriteDirectoryOutputs(writer, result.DirectoryOutputs);
            writer.Write(result.MustBeConsideredPerpetuallyDirty);
            writer.Write(result.DynamicallyObservedFiles, WriteAbsolutePath);
            writer.Write(result.DynamicallyObservedEnumerations, WriteAbsolutePath);
            writer.Write(result.AllowedUndeclaredReads, WriteAbsolutePath);
            writer.Write(result.AbsentPathProbesUnderOutputDirectories, WriteAbsolutePath);
            WriteReportedProcessesAndFileAccesses(
                writer,
                result.FileAccessViolationsNotWhitelisted,
                result.WhitelistedFileAccessViolations,
                writePath: m_writePath);

            WriteTwoPhaseCachingInfo(writer, result.TwoPhaseCachingInfo);
            WritePipCacheDescriptor(writer, result.PipCacheDescriptorV2Metadata);
            if (result.PipCacheDescriptorV2Metadata != null)
            {
                writer.Write(result.PipCacheDescriptorV2Metadata.OutputContentReplicasMiniBloomFilter);
            }

            writer.Write(result.PathSet.HasValue);
            result.PathSet?.Serialize(m_executionContext.PathTable, writer, pathWriter: WriteAbsolutePath);

            bool sendCacheLookupCounters = result.CacheLookupPerfInfo != null;
            writer.Write(sendCacheLookupCounters);

            if (sendCacheLookupCounters)
            {
                result.CacheLookupPerfInfo.Serialize(writer);
            }
        }

        private static TwoPhaseCachingInfo ReadTwoPhaseCachingInfo(BuildXLReader reader)
        {
            if (reader.ReadBoolean())
            {
                return TwoPhaseCachingInfo.Deserialize(reader);
            }
            else
            {
                return null;
            }
        }

        private static void WriteTwoPhaseCachingInfo(BuildXLWriter writer, TwoPhaseCachingInfo twoPhaseCachingInfo)
        {
            if (twoPhaseCachingInfo != null)
            {
                writer.Write(true);
                twoPhaseCachingInfo.Serialize(writer);
            }
            else
            {
                writer.Write(false);
            }
        }

        /// <summary>
        /// Serialize metadata to writer
        /// </summary>
        public static void WritePipCacheDescriptor(BuildXLWriter writer, PipCacheDescriptorV2Metadata metadata)
        {
            if (metadata != null)
            {
                writer.Write(true);
                var blob = BondExtensions.Serialize(metadata);
                writer.WriteCompact(blob.Count);
                writer.Write(blob.Array, blob.Offset, blob.Count);
            }
            else
            {
                writer.Write(false);
            }
        }

        /// <summary>
        /// Deserialize metadata from reader
        /// </summary>
        public static PipCacheDescriptorV2Metadata ReadPipCacheDescriptor(BuildXLReader reader)
        {
            if (reader.ReadBoolean())
            {
                var length = reader.ReadInt32Compact();
                var blob = new ArraySegment<byte>(reader.ReadBytes(length));
                return BondExtensions.Deserialize<PipCacheDescriptorV2Metadata>(blob);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Reads reported processes and file accesses
        /// </summary>
        public static void ReadReportedProcessesAndFileAccesses(
            BuildXLReader reader,
            out ReportedFileAccess[] reportedFileAccesses,
            out ReportedFileAccess[] whitelistedReportedFileAccesses,
            out ReportedProcess[] reportedProcesses,
            Func<BuildXLReader, AbsolutePath> readPath = null)
        {
            readPath = readPath ?? (r => r.ReadAbsolutePath());

            bool hasReportedFileAccessesOrProcesses = reader.ReadBoolean();

            if (!hasReportedFileAccessesOrProcesses)
            {
                reportedProcesses = CollectionUtilities.EmptyArray<ReportedProcess>();
                reportedFileAccesses = CollectionUtilities.EmptyArray<ReportedFileAccess>();
                whitelistedReportedFileAccesses = CollectionUtilities.EmptyArray<ReportedFileAccess>();
            }
            else
            {
                reportedProcesses = ReadReportedProcesses(reader);

                int reportedFileAccessCount = reader.ReadInt32Compact();
                reportedFileAccesses = new ReportedFileAccess[reportedFileAccessCount];
                for (int i = 0; i < reportedFileAccessCount; i++)
                {
                    reportedFileAccesses[i] = ReadReportedFileAccess(reader, reportedProcesses, readPath);
                }

                int whitelistedReportedFileAccessCount = reader.ReadInt32Compact();
                whitelistedReportedFileAccesses = new ReportedFileAccess[whitelistedReportedFileAccessCount];
                for (int i = 0; i < whitelistedReportedFileAccessCount; i++)
                {
                    whitelistedReportedFileAccesses[i] = ReadReportedFileAccess(reader, reportedProcesses, readPath);
                }
            }
        }

        /// <summary>
        /// Writes reported processes and file accesses
        /// </summary>
        public static void WriteReportedProcessesAndFileAccesses(
            BuildXLWriter writer,
            IReadOnlyCollection<ReportedFileAccess> reportedFileAccesses,
            IReadOnlyCollection<ReportedFileAccess> whitelistedReportedFileAccesses,
            IReadOnlyCollection<ReportedProcess> reportedProcesses = null,
            Action<BuildXLWriter, AbsolutePath> writePath = null)
        {
            writePath = writePath ?? ((w, path) => w.Write(path));

            bool hasReportedFileAccessesOrProcesses = (reportedFileAccesses != null && reportedFileAccesses.Count != 0)
                || (reportedProcesses != null && reportedProcesses.Count != 0)
                || (whitelistedReportedFileAccesses != null && whitelistedReportedFileAccesses.Count != 0);

            if (!hasReportedFileAccessesOrProcesses)
            {
                writer.Write(false);
            }
            else
            {
                writer.Write(true);

                reportedFileAccesses = reportedFileAccesses ?? CollectionUtilities.EmptyArray<ReportedFileAccess>();
                whitelistedReportedFileAccesses = whitelistedReportedFileAccesses ?? CollectionUtilities.EmptyArray<ReportedFileAccess>();
                reportedProcesses = reportedProcesses ?? CollectionUtilities.EmptyArray<ReportedProcess>();

                using (var pooledProcessMap = s_reportedProcessMapPool.GetInstance())
                {
                    Dictionary<ReportedProcess, int> processMap = pooledProcessMap.Instance;
                    var allReportedProcesses = reportedProcesses.Concat(reportedFileAccesses.Select(rfa => rfa.Process)).Concat(whitelistedReportedFileAccesses.Select(rfa => rfa.Process));

                    foreach (var reportedProcess in allReportedProcesses)
                    {
                        int index;
                        if (!processMap.TryGetValue(reportedProcess, out index))
                        {
                            index = processMap.Count;
                            processMap[reportedProcess] = index;
                        }
                    }

                    WriteReportedProcesses(writer, processMap);
                    writer.WriteCompact(reportedFileAccesses.Count);
                    foreach (var reportedFileAccess in reportedFileAccesses)
                    {
                        WriteReportedFileAccess(writer, reportedFileAccess, processMap, writePath);
                    }

                    writer.WriteCompact(whitelistedReportedFileAccesses.Count);
                    foreach (var whitelistedReportedFileAccess in whitelistedReportedFileAccesses)
                    {
                        WriteReportedFileAccess(writer, whitelistedReportedFileAccess, processMap, writePath);
                    }
                }
            }
        }

        private static ReportedProcess[] ReadReportedProcesses(BuildXLReader reader)
        {
            int count = reader.ReadInt32Compact();
            ReportedProcess[] processes = new ReportedProcess[count];
            for (int i = 0; i < count; i++)
            {
                processes[i] = ReportedProcess.Deserialize(reader);
            }

            return processes;
        }

        private static void WriteReportedProcesses(BuildXLWriter writer, Dictionary<ReportedProcess, int> processMap)
        {
            writer.WriteCompact(processMap.Count);
            ReportedProcess[] processes = new ReportedProcess[processMap.Count];
            foreach (var process in processMap)
            {
                processes[process.Value] = process.Key;
            }

            for (int i = 0; i < processes.Length; i++)
            {
                processes[i].Serialize(writer);
            }
        }

        private static ReportedFileAccess ReadReportedFileAccess(BuildXLReader reader, ReportedProcess[] processes, Func<BuildXLReader, AbsolutePath> readPath)
        {
            return ReportedFileAccess.Deserialize(reader, processes, readPath);
        }

        private static void WriteReportedFileAccess(
            BuildXLWriter writer,
            ReportedFileAccess reportedFileAccess,
            Dictionary<ReportedProcess, int> processMap,
            Action<BuildXLWriter, AbsolutePath> writePath)
        {
            reportedFileAccess.Serialize(writer, processMap, writePath);
        }

        private static string ReadNullableString(BuildXLReader reader)
        {
            if (!reader.ReadBoolean())
            {
                return null;
            }

            return reader.ReadString();
        }

        private static void WriteNullableString(BuildXLWriter writer, string value)
        {
            writer.Write(value != null);
            if (value != null)
            {
                writer.Write(value);
            }
        }

        private (FileArtifact, FileMaterializationInfo, PipOutputOrigin)[] ReadOutputContent(BuildXLReader reader)
        {
            int count = reader.ReadInt32Compact();

            (FileArtifact, FileMaterializationInfo, PipOutputOrigin)[] outputContent;

            if (count != 0)
            {
                outputContent = new (FileArtifact, FileMaterializationInfo, PipOutputOrigin)[count];
                for (int i = 0; i < count; i++)
                {
                    var file = ReadFileArtifact(reader);
                    var length = reader.ReadInt64Compact();
                    var hashBytes = reader.ReadBytes(ContentHashingUtilities.HashInfo.ByteLength);
                    var hash = ContentHashingUtilities.CreateFrom(hashBytes);
                    var fileName = ReadPathAtom(reader);
                    var reparsePointType = (ReparsePointType)reader.ReadByte();
                    var reparsePointTarget = ReadNullableString(reader);

                    outputContent[i] = (
                        file,
                        new FileMaterializationInfo(new FileContentInfo(hash, length), fileName, ReparsePointInfo.Create(reparsePointType, reparsePointTarget)),
                        PipOutputOrigin.NotMaterialized);
                }
            }
            else
            {
                outputContent = CollectionUtilities.EmptyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>();
            }

            return outputContent;
        }

        private void WriteOutputContent(BuildXLWriter writer, IReadOnlyList<(FileArtifact fileArtifact, FileMaterializationInfo fileMaterializationInfo, PipOutputOrigin pipOutputOrgin)> outputContent)
        {
            int count = outputContent.Count;
            writer.WriteCompact(count);
            using (var pooledByteArray = Pools.GetByteArray(ContentHashingUtilities.HashInfo.ByteLength))
            {
                var byteBuffer = pooledByteArray.Instance;
                for (int i = 0; i < count; i++)
                {
                    var output = outputContent[i];
                    WriteFileArtifact(writer, output.fileArtifact);
                    writer.WriteCompact(output.fileMaterializationInfo.FileContentInfo.RawLength);
                    output.Item2.Hash.SerializeHashBytes(byteBuffer, 0);
                    writer.Write(byteBuffer, 0, ContentHashingUtilities.HashInfo.ByteLength);
                    WritePathAtom(writer, output.fileMaterializationInfo.FileName);
                    writer.Write((byte)output.fileMaterializationInfo.ReparsePointInfo.ReparsePointType);
                    WriteNullableString(writer, output.fileMaterializationInfo.ReparsePointInfo.GetReparsePointTarget());
                }
            }
        }

        private (DirectoryArtifact, ReadOnlyArray<FileArtifact>)[] ReadDirectoryOutputs(BuildXLReader reader)
        {
            int count = reader.ReadInt32Compact();
            (DirectoryArtifact, ReadOnlyArray<FileArtifact>)[] directoryOutputs = count > 0
                ? new (DirectoryArtifact, ReadOnlyArray<FileArtifact>)[count]
                : CollectionUtilities.EmptyArray<(DirectoryArtifact, ReadOnlyArray<FileArtifact>)>();

            for (int i = 0; i < count; ++i)
            {
                var directory = reader.ReadDirectoryArtifact();
                var length = reader.ReadInt32Compact();
                var members = length > 0 ? new FileArtifact[length] : CollectionUtilities.EmptyArray<FileArtifact>();

                for (int j = 0; j < length; ++j)
                {
                    members[j] = ReadFileArtifact(reader);
                }

                directoryOutputs[i] = (directory, ReadOnlyArray<FileArtifact>.FromWithoutCopy(members));
            }

            return directoryOutputs;
        }

        private void WriteDirectoryOutputs(
            BuildXLWriter writer,
            IReadOnlyList<(DirectoryArtifact directoryArtifact, ReadOnlyArray<FileArtifact> fileArtifactArray)> directoryOutputs)
        {
            writer.WriteCompact(directoryOutputs.Count);
            foreach (var directoryOutput in directoryOutputs)
            {
                writer.Write(directoryOutput.directoryArtifact);
                writer.WriteCompact(directoryOutput.fileArtifactArray.Length);
                foreach (var member in directoryOutput.fileArtifactArray)
                {
                    WriteFileArtifact(writer, member);
                }
            }
        }

        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode", Justification = "To be used when implementing arbitrary path serialization.")]
        private Tuple<AbsolutePath, Encoding> ReadPathAndEncoding(BuildXLReader reader)
        {
            return Tuple.Create(ReadAbsolutePath(reader), Encoding.GetEncoding(reader.ReadInt32()));
        }

        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode", Justification = "To be used when implementing arbitrary path serialization.")]
        private Tuple<AbsolutePath, Encoding> WritePathAndEncoding(BuildXLReader reader)
        {
            return Tuple.Create(ReadAbsolutePath(reader), Encoding.GetEncoding(reader.ReadInt32()));
        }

        private PathAtom ReadPathAtom(BuildXLReader reader)
        {
            string pathAtomString = reader.ReadString();
            return string.IsNullOrEmpty(pathAtomString) ?
                PathAtom.Invalid :
                PathAtom.Create(m_executionContext.StringTable, pathAtomString);
        }

        private void WritePathAtom(BuildXLWriter writer, PathAtom pathAtom)
        {
            writer.Write(pathAtom.IsValid ?
                pathAtom.ToString(m_executionContext.StringTable) :
                string.Empty);
        }

        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode", Justification = "To be used when implementing arbitrary path serialization.")]
        private AbsolutePath ReadAbsolutePath(BuildXLReader reader)
        {
            if (reader.ReadBoolean())
            {
                return reader.ReadAbsolutePath();
            }
            else
            {
                return AbsolutePath.Create(m_executionContext.PathTable, reader.ReadString());
            }
        }

        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode", Justification = "To be used when implementing arbitrary path serialization.")]
        private void WriteAbsolutePath(BuildXLWriter writer, AbsolutePath path)
        {
            if (path.Value.Index <= m_maxSerializableAbsolutePathIndex)
            {
                writer.Write(true);
                writer.Write(path);
            }
            else
            {
                writer.Write(false);
                writer.Write(path.ToString(m_executionContext.PathTable));
            }
        }

        private FileArtifact ReadFileArtifact(BuildXLReader reader)
        {
            var path = ReadAbsolutePath(reader);
            var rewriteCount = reader.ReadInt32Compact();
            return new FileArtifact(path, rewriteCount);
        }

        private void WriteFileArtifact(BuildXLWriter writer, FileArtifact file)
        {
            WriteAbsolutePath(writer, file.Path);
            writer.WriteCompact(file.RewriteCount);
        }
    }
}
