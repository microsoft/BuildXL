// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache;
using BuildXL.Pips;
using BuildXL.Processes;
using BuildXL.Scheduler.Distribution;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Tracing;
using System.Linq;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.Scheduler.Tracing
{
    // How to add a new event:
    // - Add an ExecutionEventId
    // - Add a static metadata member to ExecutionLogMetadata, and update ExecutionLogMetadata.Events to include it.
    // - Define a new event-data type like FileArtifactContentDecidedEventData
    //   Be sure that serialization and deserialization round-trips correctly.
    // - Add a corresponding method to IExecutionLogTarget
    // - Add a corresponding VIRTUAL method to ExecutionLogTargetBase
    // Every event requires a log-target method, a unique event-data type, and registered metadata for generic serialization / deserialization support.

    /// <summary>
    /// Target which receives execution log events. The caller of a log target is conceptually some executor of a build.
    /// The implementor is conceptually some consumer of execution events, such as a performance analyzer: <c>Execution -> Consumer</c>.
    /// In practice, we may choose to persist the log events in between execution and the final consumer: <c>Execution -> [FileWriter -> FileReader] -> Consumer</c>.
    /// In that scheme, the FileWriter is also a log target; the FileReader calls Consumer (which remains a log target as before).
    /// The file write and read pair can be provided by <see cref="ExecutionLogFileTarget"/> and <see cref="ExecutionLogFileReader"/> respectively.
    /// </summary>
    /// <remarks>
    /// Each type of event in the execution log corresponds to a method on this interface.
    /// </remarks>
    public interface IExecutionLogTarget : IDisposable
    {
        /// <summary>
        /// Gets whether the target can handle the given evnet
        /// </summary>
        bool CanHandleEvent(ExecutionEventId eventId, uint workerId, long timestamp, int eventPayloadSize);

        /// <summary>
        /// The content hash for a file artifact has been established. This must happen before any consumers of this file artifact are processed.
        /// </summary>
        void FileArtifactContentDecided(FileArtifactContentDecidedEventData data);

        /// <summary>
        /// Save the list of workers for this particular build
        /// </summary>
        void WorkerList(WorkerListEventData data);

        /// <summary>
        /// Performance data about a pip execution
        /// </summary>
        void PipExecutionPerformance(PipExecutionPerformanceEventData data);

        /// <summary>
        /// Directory membership as discovered at the time its hash is calculated
        /// </summary>
        void DirectoryMembershipHashed(DirectoryMembershipHashedEventData data);

        /// <summary>
        ///  Monitoring information (launched processes and file accesses) for process execution reported
        /// </summary>
        void ProcessExecutionMonitoringReported(ProcessExecutionMonitoringReportedEventData data);

        /// <summary>
        /// Information about computation of a strong fingerprint for a process
        /// </summary>
        void ProcessFingerprintComputation(ProcessFingerprintComputationEventData data);

        /// <summary>
        /// The value for monitoring NTCreateFile API.
        /// </summary>
        void ExtraEventDataReported(ExtraEventData data);

        /// <summary>
        /// Dependency analysis violation is reported.
        /// </summary>
        void DependencyViolationReported(DependencyViolationEventData data);

        /// <summary>
        /// PipExecutionStep performance is reported.
        /// </summary>
        void PipExecutionStepPerformanceReported(PipExecutionStepPerformanceEventData data);

        /// <summary>
        /// Pip cache miss
        /// </summary>
        void PipCacheMiss(PipCacheMissEventData data);

        /// <summary>
        /// Resource and PipQueue usage is reported
        /// </summary>
        void StatusReported(StatusEventData data);

        /// <summary>
        /// Single event giving build invocation information that contains configuration details usefull for analyzers.
        /// </summary>
        // $Rename: Due to telemetry backend scripts this cannot be renamed to BuildXL
        void DominoInvocation(DominoInvocationEventData data);

        /// <summary>
        /// Creates a worker target that logs back to master for distributed builds
        /// </summary>
        /// <param name="workerId"></param>
        IExecutionLogTarget CreateWorkerTarget(uint workerId);

        /// <summary>
        /// Content of output directories is reported
        /// </summary>
        void PipExecutionDirectoryOutputs(PipExecutionDirectoryOutputs data);
    }

    /// <summary>
    /// Event ids for execution events in execution log
    /// </summary>
    public enum ExecutionEventId : byte
    {
        /// <summary>
        /// See <see cref="IExecutionLogTarget.FileArtifactContentDecided"/>
        /// </summary>
        FileArtifactContentDecided = 0,

        /// <summary>
        /// See <see cref="IExecutionLogTarget.WorkerList"/>
        /// </summary>
        WorkerList = 1,

        /// <summary>
        /// See <see cref="IExecutionLogTarget.PipExecutionPerformance"/>
        /// </summary>
        PipExecutionPerformance = 2,

        /// <summary>
        /// See <see cref="IExecutionLogTarget.DirectoryMembershipHashed"/>
        /// </summary>
        DirectoryMembershipHashed = 3,

        /// <summary>
        /// Deprecated. Use <see cref="ProcessFingerprintComputation"/>
        /// </summary>
        ObservedInputs = 4,

        /// <summary>
        /// See <see cref="IExecutionLogTarget.ProcessExecutionMonitoringReported"/>
        /// </summary>
        ProcessExecutionMonitoringReported = 5,

        /// <summary>
        /// See <see cref="IExecutionLogTarget.ExtraEventDataReported"/>
        /// </summary>
        ExtraEventDataReported = 6,

        /// <summary>
        /// See <see cref="IExecutionLogTarget.DependencyViolationReported"/>
        /// </summary>
        DependencyViolationReported = 7,

        /// <summary>
        /// See <see cref="IExecutionLogTarget.PipExecutionStepPerformanceReported"/>
        /// </summary>
        PipExecutionStepPerformanceReported = 8,

        /// <summary>
        /// See <see cref="IExecutionLogTarget.StatusReported"/>
        /// </summary>
        ResourceUsageReported = 9,

        /// <summary>
        /// See <see cref="IExecutionLogTarget.ProcessFingerprintComputation"/>
        /// </summary>
        ProcessFingerprintComputation = 10,

        /// <summary>
        /// See <see cref="IExecutionLogTarget.PipCacheMiss"/>
        /// </summary>
        PipCacheMiss = 11,

        /// <summary>
        /// See <see cref="IExecutionLogTarget.PipExecutionDirectoryOutputs"/>
        /// </summary>
        PipExecutionDirectoryOutputs = 12,

        /// <summary>
        /// See <see cref="IExecutionLogTarget.DominoInvocation"/>
        /// </summary>
        DominoInvocation = 13,
    }

    /// <summary>
    /// Metadata for execution logs (in particular, per-event metadata).
    /// This establishes (<see cref="IExecutionLogTarget"/> method, event data, event ID) triples.
    /// </summary>
    public static class ExecutionLogMetadata
    {
        /// <summary>
        /// Event description for <see cref="IExecutionLogTarget.FileArtifactContentDecided"/>
        /// </summary>
        public static readonly ExecutionLogEventMetadata<FileArtifactContentDecidedEventData> FileArtifactContentDecided =
            new ExecutionLogEventMetadata<FileArtifactContentDecidedEventData>(
                ExecutionEventId.FileArtifactContentDecided,
                (data, target) => target.FileArtifactContentDecided(data));

        /// <summary>
        /// Event description for <see cref="IExecutionLogTarget.WorkerList"/>
        /// </summary>
        public static readonly ExecutionLogEventMetadata<WorkerListEventData> WorkerList =
            new ExecutionLogEventMetadata<WorkerListEventData>(
                ExecutionEventId.WorkerList,
                (data, target) => target.WorkerList(data));

        /// <summary>
        /// Event description for <see cref="IExecutionLogTarget.PipExecutionPerformance"/>
        /// </summary>
        public static readonly ExecutionLogEventMetadata<PipExecutionPerformanceEventData> PipExecutionPerformance =
            new ExecutionLogEventMetadata<PipExecutionPerformanceEventData>(
                ExecutionEventId.PipExecutionPerformance,
                (data, target) => target.PipExecutionPerformance(data));

        /// <summary>
        /// Event description for <see cref="IExecutionLogTarget.DirectoryMembershipHashed"/>
        /// </summary>
        public static readonly ExecutionLogEventMetadata<DirectoryMembershipHashedEventData> DirectoryMembershipHashed =
            new ExecutionLogEventMetadata<DirectoryMembershipHashedEventData>(
                ExecutionEventId.DirectoryMembershipHashed,
                (data, target) => target.DirectoryMembershipHashed(data));

        /// <summary>
        /// Event description for <see cref="IExecutionLogTarget.ExtraEventDataReported"/>
        /// </summary>
        public static readonly ExecutionLogEventMetadata<ExtraEventData> ExtraEventDataReported =
            new ExecutionLogEventMetadata<ExtraEventData>(
                ExecutionEventId.ExtraEventDataReported,
                (data, target) => target.ExtraEventDataReported(data));

        /// <summary>
        /// Event description for <see cref="IExecutionLogTarget.ProcessExecutionMonitoringReported"/>
        /// </summary>
        public static readonly ExecutionLogEventMetadata<ProcessExecutionMonitoringReportedEventData> ProcessExecutionMonitoringReported =
            new ExecutionLogEventMetadata<ProcessExecutionMonitoringReportedEventData>(
                ExecutionEventId.ProcessExecutionMonitoringReported,
                (data, target) => target.ProcessExecutionMonitoringReported(data));

        /// <summary>
        /// Event description for <see cref="IExecutionLogTarget.DependencyViolationReported"/>
        /// </summary>
        public static readonly ExecutionLogEventMetadata<DependencyViolationEventData> DependencyViolationReported =
            new ExecutionLogEventMetadata<DependencyViolationEventData>(
                ExecutionEventId.DependencyViolationReported,
                (data, target) => target.DependencyViolationReported(data));

        /// <summary>
        /// Event description for <see cref="IExecutionLogTarget.DependencyViolationReported"/>
        /// </summary>
        public static readonly ExecutionLogEventMetadata<PipExecutionStepPerformanceEventData> PipExecutionStepPerformanceReported =
            new ExecutionLogEventMetadata<PipExecutionStepPerformanceEventData>(
                ExecutionEventId.PipExecutionStepPerformanceReported,
                (data, target) => target.PipExecutionStepPerformanceReported(data));

        /// <summary>
        /// Event description for <see cref="IExecutionLogTarget.DependencyViolationReported"/>
        /// </summary>
        public static readonly ExecutionLogEventMetadata<StatusEventData> ResourceUsageReported =
            new ExecutionLogEventMetadata<StatusEventData>(
                ExecutionEventId.ResourceUsageReported,
                (data, target) => target.StatusReported(data));

        /// <summary>
        /// Event description for <see cref="IExecutionLogTarget.DominoInvocation"/>
        /// </summary>
        // $Rename: Due to telemetry backend scripts this cannot be renamed to BuildXL
        public static readonly ExecutionLogEventMetadata<DominoInvocationEventData> DominoInvocation =
            new ExecutionLogEventMetadata<DominoInvocationEventData>(
                ExecutionEventId.DominoInvocation,
                (data, target) => target.DominoInvocation(data));

        /// <summary>
        /// Event description for <see cref="IExecutionLogTarget.ProcessFingerprintComputation"/>
        /// </summary>
        public static readonly ExecutionLogEventMetadata<ProcessFingerprintComputationEventData> ProcessFingerprintComputation =
            new ExecutionLogEventMetadata<ProcessFingerprintComputationEventData>(
                ExecutionEventId.ProcessFingerprintComputation,
                (data, target) => target.ProcessFingerprintComputation(data));

        /// <summary>
        /// Event description for <see cref="IExecutionLogTarget.PipCacheMiss"/>
        /// </summary>
        public static readonly ExecutionLogEventMetadata<PipCacheMissEventData> PipCacheMiss =
            new ExecutionLogEventMetadata<PipCacheMissEventData>(
                ExecutionEventId.PipCacheMiss,
                (data, target) => target.PipCacheMiss(data));

        /// <summary>
        /// Event description for <see cref="IExecutionLogTarget.PipExecutionDirectoryOutputs"/>
        /// </summary>
        public static readonly ExecutionLogEventMetadata<PipExecutionDirectoryOutputs> PipExecutionDirectoryOutputs =
            new ExecutionLogEventMetadata<PipExecutionDirectoryOutputs>(
                ExecutionEventId.PipExecutionDirectoryOutputs,
                (data, target) => target.PipExecutionDirectoryOutputs(data));

        /// <summary>
        /// All execution log events.
        /// </summary>
        public static readonly IReadOnlyList<ExecutionLogEventMetadata> Events = new ExecutionLogEventMetadata[]
                                                                                 {
                                                                                     DominoInvocation,
                                                                                     FileArtifactContentDecided,
                                                                                     WorkerList,
                                                                                     PipExecutionPerformance,
                                                                                     DirectoryMembershipHashed,
                                                                                     ProcessExecutionMonitoringReported,
                                                                                     ExtraEventDataReported,
                                                                                     DependencyViolationReported,
                                                                                     PipExecutionStepPerformanceReported,
                                                                                     ResourceUsageReported,
                                                                                     ProcessFingerprintComputation,
                                                                                     PipCacheMiss,
                                                                                     PipExecutionDirectoryOutputs,
                                                                                 };
    }

    /// <summary>
    /// Stores salt information
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public struct ExtraEventData : IExecutionLogEventData<ExtraEventData>
    {
        /// <summary>
        /// Whether the /unsafe_DisableDetours flag is passed to BuildXL.
        /// </summary>
        public bool DisableDetours;

        /// <summary>
        /// Whether the /unsafe_IgnoreReparsePoints flag is passed to BuildXL.
        /// </summary>
        public bool IgnoreReparsePoints;

        /// <summary>
        /// Whether the /unsafe_IgnorePreloadedDlls flag is passed to BuildXL.
        /// </summary>
        public bool IgnorePreloadedDlls;

        /// <summary>
        /// Whether the /unsafe_ExistingDirectoryProbesAsEnumerations flag is passed to BuildXL.
        /// </summary>
        public bool ExistingDirectoryProbesAsEnumerations;

        /// <summary>
        /// Whether the NtCreateFile is being monitored.
        /// </summary>
        public bool NtFileCreateMonitored;

        /// <summary>
        /// Whether the ZwCreateOpneFile is being monitored.
        /// </summary>
        public bool ZwFileCreateOpenMonitored;

        /// <summary>
        /// Whether the ZwRenameFileInformation is being detoured.
        /// </summary>
        public bool IgnoreZwRenameFileInformation;

        /// <summary>
        /// Whether the ZwOtherFileInformation is being detoured.
        /// </summary>
        public bool IgnoreZwOtherFileInformation;

        /// <summary>
        /// Whether symlinks are followed for any other APIs, but CreateFile APIs.
        /// </summary>
        public bool IgnoreNonCreateFileReparsePoints;

        /// <summary>
        /// Whether the SetFileInformationByHandle is being detoured.
        /// </summary>
        public bool IgnoreSetFileInformationByHandle;

        /// <summary>
        /// Whether the GetFinalPathNameByHandle API is being detoured.
        /// </summary>
        public bool IgnoreGetFinalPathNameByHandle;

        /// <summary>
        /// FingerprintVersion
        /// </summary>
        public PipFingerprintingVersion FingerprintVersion;

        /// <summary>
        /// Extra optional fingerprint salt.
        /// </summary>
        public string FingerprintSalt;

        /// <summary>
        /// Gets the hash of the search path tools configured
        /// </summary>
        public ContentHash? SearchPathToolsHash;

        /// <summary>
        /// Whether /unsafe_unexpectedFileAccessesAreErrors was passed to BuildXL
        /// </summary>
        public bool UnexpectedFileAccessesAreErrors;

        /// <summary>
        /// Whether BuildXL monitors file accesses.
        /// </summary>
        public bool MonitorFileAccesses;

        /// <summary>
        /// Whether /maskUntrackedAccesses was passed to BuildXL
        /// </summary>
        public bool MaskUntrackedAccesses;

        /// <summary>
        /// Whether /normalizeReadTimestamps was enabled (enabled by default)
        /// </summary>
        public bool NormalizeReadTimestamps;

        /// <summary>
        /// Whether /warnaserror is enabled
        /// </summary>
        public bool PipWarningsPromotedToErrors;

        /// <summary>
        /// Whether /validateDistribution flag was enabled (disabled by default).
        /// </summary>
        public bool ValidateDistribution;

        /// <summary>
        /// Extra optional fingerprint salt.
        /// </summary>
        public string RequiredKextVersionNumber;

        /// <inheritdoc />
        public ExecutionLogEventMetadata<ExtraEventData> Metadata => ExecutionLogMetadata.ExtraEventDataReported;

        /// <summary>
        /// Creates event data from salts
        /// </summary>
        public ExtraEventData(ExtraFingerprintSalts salts)
        {
            IgnoreSetFileInformationByHandle = salts.IgnoreSetFileInformationByHandle;
            IgnoreZwRenameFileInformation = salts.IgnoreZwRenameFileInformation;
            IgnoreZwOtherFileInformation = salts.IgnoreZwOtherFileInformation;
            ZwFileCreateOpenMonitored = salts.MonitorZwCreateOpenQueryFile;
            IgnoreNonCreateFileReparsePoints = salts.IgnoreNonCreateFileReparsePoints;
            IgnoreReparsePoints = salts.IgnoreReparsePoints;
            IgnorePreloadedDlls = salts.IgnorePreloadedDlls;
            IgnoreGetFinalPathNameByHandle = salts.IgnoreGetFinalPathNameByHandle;
            ExistingDirectoryProbesAsEnumerations = salts.ExistingDirectoryProbesAsEnumerations;
            DisableDetours = salts.DisableDetours;
            NtFileCreateMonitored = salts.MonitorNtCreateFile;
            ZwFileCreateOpenMonitored = salts.MonitorZwCreateOpenQueryFile;
            FingerprintVersion = salts.FingerprintVersion;
            FingerprintSalt = salts.FingerprintSalt;
            SearchPathToolsHash = salts.SearchPathToolsHash;
            UnexpectedFileAccessesAreErrors = salts.UnexpectedFileAccessesAreErrors;
            MonitorFileAccesses = salts.MonitorFileAccesses;
            MaskUntrackedAccesses = salts.MaskUntrackedAccesses;
            NormalizeReadTimestamps = salts.NormalizeReadTimestamps;
            PipWarningsPromotedToErrors = salts.PipWarningsPromotedToErrors;
            ValidateDistribution = salts.ValidateDistribution;
            RequiredKextVersionNumber = salts.RequiredKextVersionNumber;
        }

        /// <summary>
        /// Creates event data from salts
        /// </summary>
        public ExtraFingerprintSalts ToFingerprintSalts()
        {
            // NOTE: These datastructures must be kept in sync. If new information is added to
            // ExtraFingerprintSalts it should be added to this class as well
            // IMPORTANT: Update serialization logic as well when adding new values
            return new ExtraFingerprintSalts(
                       ignoreSetFileInformationByHandle: IgnoreSetFileInformationByHandle,
                       ignoreZwRenameFileInformation: IgnoreZwRenameFileInformation,
                       ignoreZwOtherFileInformation: IgnoreZwOtherFileInformation,
                       ignoreNonCreateFileReparsePoints: IgnoreNonCreateFileReparsePoints,
                       ignoreReparsePoints: IgnoreReparsePoints,
                       ignorePreloadedDlls: IgnorePreloadedDlls,
                       ignoreGetFinalPathNameByHandle: IgnoreGetFinalPathNameByHandle,
                       existingDirectoryProbesAsEnumerations: ExistingDirectoryProbesAsEnumerations,
                       disableDetours: DisableDetours,
                       monitorNtCreateFile: NtFileCreateMonitored,
                       monitorZwCreateOpenQueryFile: ZwFileCreateOpenMonitored,
                       fingerprintVersion: FingerprintVersion,
                       fingerprintSalt: FingerprintSalt,
                       searchPathToolsHash: SearchPathToolsHash,
                       monitorFileAccesses: MonitorFileAccesses,
                       unexpectedFileAccessesAreErrors: UnexpectedFileAccessesAreErrors,
                       maskUntrackedAccesses: MaskUntrackedAccesses,
                       normalizeReadTimestamps: NormalizeReadTimestamps,
                       validateDistribution: ValidateDistribution,
                       pipWarningsPromotedToErrors: PipWarningsPromotedToErrors,
                       requiredKextVersionNumber: RequiredKextVersionNumber
                   )
                   {
                       // Constructor appends EngineEnvironmentSettings.FingerprintSalt
                       // We need to ensure that the value used matches the event data so
                       // no modifications should be allowed
                       FingerprintSalt = FingerprintSalt,
                   };
        }

        /// <inheritdoc />
        public void Serialize(BinaryLogger.EventWriter writer)
        {
            writer.Write(MaskUntrackedAccesses);
            writer.Write(NormalizeReadTimestamps);
            writer.Write(IgnoreSetFileInformationByHandle);
            writer.Write(IgnoreZwRenameFileInformation);
            writer.Write(IgnoreZwOtherFileInformation);
            writer.Write(IgnoreNonCreateFileReparsePoints);
            writer.Write(IgnoreReparsePoints);
            writer.Write(IgnorePreloadedDlls);
            writer.Write(IgnoreGetFinalPathNameByHandle);
            writer.Write(ExistingDirectoryProbesAsEnumerations);
            writer.Write(DisableDetours);
            writer.Write(NtFileCreateMonitored);
            writer.Write(ZwFileCreateOpenMonitored);
            writer.Write((int)FingerprintVersion);
            writer.Write(FingerprintSalt);
            writer.Write(SearchPathToolsHash, (w, h) => h.SerializeHashBytes(w));
            writer.Write(MonitorFileAccesses);
            writer.Write(UnexpectedFileAccessesAreErrors);
            writer.Write(MaskUntrackedAccesses);
            writer.Write(NormalizeReadTimestamps);
            writer.Write(PipWarningsPromotedToErrors);
            writer.Write(RequiredKextVersionNumber);
        }

        /// <inheritdoc />
        public void DeserializeAndUpdate(BinaryLogReader.EventReader reader)
        {
            MaskUntrackedAccesses = reader.ReadBoolean();
            NormalizeReadTimestamps = reader.ReadBoolean();
            IgnoreSetFileInformationByHandle = reader.ReadBoolean();
            IgnoreZwRenameFileInformation = reader.ReadBoolean();
            IgnoreZwOtherFileInformation = reader.ReadBoolean();
            IgnoreNonCreateFileReparsePoints = reader.ReadBoolean();
            IgnoreReparsePoints = reader.ReadBoolean();
            IgnorePreloadedDlls = reader.ReadBoolean();
            IgnoreGetFinalPathNameByHandle = reader.ReadBoolean();
            ExistingDirectoryProbesAsEnumerations = reader.ReadBoolean();
            DisableDetours = reader.ReadBoolean();
            NtFileCreateMonitored = reader.ReadBoolean();
            ZwFileCreateOpenMonitored = reader.ReadBoolean();
            FingerprintVersion = (PipFingerprintingVersion)reader.ReadInt32();
            FingerprintSalt = reader.ReadString();
            SearchPathToolsHash = reader.ReadNullableStruct(r => ContentHashingUtilities.CreateFrom(r));
            MonitorFileAccesses = reader.ReadBoolean();
            UnexpectedFileAccessesAreErrors = reader.ReadBoolean();
            MaskUntrackedAccesses = reader.ReadBoolean();
            NormalizeReadTimestamps = reader.ReadBoolean();
            PipWarningsPromotedToErrors = reader.ReadBoolean();
            RequiredKextVersionNumber = reader.ReadString();
        }
    }

    /// <summary>
    /// Information about a file and its content info (size and hash)
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public struct FileArtifactContentDecidedEventData : IExecutionLogEventData<FileArtifactContentDecidedEventData>, IFingerprintInputCollection
    {
        /// <summary>
        /// The file
        /// </summary>
        public FileArtifact FileArtifact;

        /// <summary>
        /// The content information
        /// </summary>
        public FileContentInfo FileContentInfo;

        /// <summary>
        /// The origin information
        /// </summary>
        public PipOutputOrigin OutputOrigin;

        /// <inheritdoc />
        public ExecutionLogEventMetadata<FileArtifactContentDecidedEventData> Metadata => ExecutionLogMetadata.FileArtifactContentDecided;

        /// <inheritdoc />
        public void Serialize(BinaryLogger.EventWriter writer)
        {
            writer.Write(FileArtifact);
            FileContentInfo.Hash.SerializeHashBytes(writer);
            writer.WriteCompact(FileContentInfo.Length);
            writer.Write((byte)OutputOrigin);
        }

        /// <inheritdoc />
        public void DeserializeAndUpdate(BinaryLogReader.EventReader reader)
        {
            FileArtifact = reader.ReadFileArtifact();
            FileContentInfo = new FileContentInfo(
                hash: ContentHashingUtilities.CreateFrom(reader),
                length: reader.ReadInt64Compact());
            OutputOrigin = (PipOutputOrigin)reader.ReadByte();
        }

        /// <inheritdoc />
        public void WriteFingerprintInputs(IFingerprinter writer)
        {
            writer.Add("Path", FileArtifact.Path);
            writer.Add("File Length", FileContentInfo.Length);
        }
    }

    /// <summary>
    /// Information about the list of workers in a build
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public struct WorkerListEventData : IExecutionLogEventData<WorkerListEventData>
    {
        /// <summary>
        /// The worker list
        /// </summary>
        public string[] Workers;

        /// <inheritdoc />
        public ExecutionLogEventMetadata<WorkerListEventData> Metadata => ExecutionLogMetadata.WorkerList;

        /// <inheritdoc />
        public void Serialize(BinaryLogger.EventWriter writer)
        {
            writer.WriteCompact(Workers.Length);
            foreach (string worker in Workers)
            {
                writer.Write(worker);
            }
        }

        /// <inheritdoc />
        public void DeserializeAndUpdate(BinaryLogReader.EventReader reader)
        {
            Workers = new string[reader.ReadInt32Compact()];
            for (int idx = 0; idx < Workers.Length; ++idx)
            {
                Workers[idx] = reader.ReadString();
            }
        }
    }

    /// <summary>
    /// Performance data about a pip execution
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public struct PipExecutionPerformanceEventData : IExecutionLogEventData<PipExecutionPerformanceEventData>
    {
        /// <summary>
        /// The pip ID
        /// </summary>
        public PipId PipId;

        /// <summary>
        /// The pip execution performance
        /// </summary>
        public PipExecutionPerformance ExecutionPerformance;

        /// <inheritdoc />
        public ExecutionLogEventMetadata<PipExecutionPerformanceEventData> Metadata => ExecutionLogMetadata.PipExecutionPerformance;

        /// <inheritdoc />
        public void Serialize(BinaryLogger.EventWriter writer)
        {
            PipId.Serialize(writer);
            ExecutionPerformance.Serialize(writer);
        }

        /// <inheritdoc />
        public void DeserializeAndUpdate(BinaryLogReader.EventReader reader)
        {
            PipId = PipId.Deserialize(reader);
            ExecutionPerformance = PipExecutionPerformance.Deserialize(reader);
        }
    }

    /// <summary>
    /// Data about a cache miss for a pip
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public struct PipCacheMissEventData : IExecutionLogEventData<PipCacheMissEventData>
    {
        /// <summary>
        /// The pip ID
        /// </summary>
        public PipId PipId;

        /// <summary>
        /// The cause of the cache miss
        /// </summary>
        public PipCacheMissType CacheMissType;

        /// <inheritdoc />
        public ExecutionLogEventMetadata<PipCacheMissEventData> Metadata => ExecutionLogMetadata.PipCacheMiss;

        /// <inheritdoc />
        public void Serialize(BinaryLogger.EventWriter writer)
        {
            PipId.Serialize(writer);
            writer.Write((byte)CacheMissType);
        }

        /// <inheritdoc />
        public void DeserializeAndUpdate(BinaryLogReader.EventReader reader)
        {
            PipId = PipId.Deserialize(reader);
            CacheMissType = (PipCacheMissType)reader.ReadByte();
        }
    }

    /// <summary>
    /// Directory membership as discovered at the time its hash is calculated
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public struct DirectoryMembershipHashedEventData : IExecutionLogEventData<DirectoryMembershipHashedEventData>, IFingerprintInputCollection
    {
        [Flags]
        private enum Flags : byte
        {
            None = 0,
            Static = 1 << 1,
            SearchPath = 1 << 2,
        }

        /// <summary>
        /// The resulting fingerprint from hashing the directory.
        /// Does not need to be serialized, only used at runtime to prevent 
        /// having to recalculate the fingerprint as the key for a fingerprint store entry. 
        /// </summary>
        public DirectoryFingerprint DirectoryFingerprint;

        /// <summary>
        /// The directory whose membership is hashed.
        /// </summary>
        public AbsolutePath Directory;

        private Flags m_flags;

        /// <summary>
        /// If true the membership is calculated from the static graph information
        /// otherwise the file system is used as a source.
        /// </summary>
        public bool IsStatic
        {
            get { return (m_flags & Flags.Static) == Flags.Static; }

            set
            {
                if (value)
                {
                    m_flags |= Flags.Static;
                }
                else
                {
                    m_flags &= ~Flags.Static;
                }
            }
        }

        /// <summary>
        /// If true membership was calculated using search paths enumeration
        /// semantics whereby only accessed/explicit dependencies file names are included
        /// </summary>
        public bool IsSearchPath
        {
            get { return (m_flags & Flags.SearchPath) == Flags.SearchPath; }

            set
            {
                if (value)
                {
                    m_flags |= Flags.SearchPath;
                }
                else
                {
                    m_flags &= ~Flags.SearchPath;
                }
            }
        }

        /// <summary>
        /// The process PipId for which the membership was computed. This will only be serialized when
        /// <see cref="IsStatic"/> or <see cref="IsSearchPath"/> is true
        /// </summary>
        public PipId PipId;

        /// <summary>
        /// Files in the directory
        /// </summary>
        /// <remarks>
        /// StringId's can't currently be stored safely in the execution log because there isn't logic to handle the
        /// case of strings added after the graph is cached.
        /// </remarks>
        public List<AbsolutePath> Members;

        /// <summary>
        /// The enumerate pattern in regex format if any used
        /// </summary>
        public string EnumeratePatternRegex;

        /// <inheritdoc />
        public ExecutionLogEventMetadata<DirectoryMembershipHashedEventData> Metadata => ExecutionLogMetadata.DirectoryMembershipHashed;

        /// <inheritdoc />
        public void Serialize(BinaryLogger.EventWriter writer)
        {
            writer.Write(Directory);
            DirectoryFingerprint.Hash.Serialize(writer);
            writer.Write((byte)m_flags);
            PipId.Serialize(writer);

            writer.WriteCompact(Members.Count);
            foreach (var member in Members)
            {
                writer.Write(member);
            }

            writer.Write(EnumeratePatternRegex ?? "");
        }

        /// <inheritdoc />
        public void DeserializeAndUpdate(BinaryLogReader.EventReader reader)
        {
            Directory = reader.ReadAbsolutePath();
            DirectoryFingerprint = new DirectoryFingerprint(new ContentHash(reader));
            m_flags = (Flags)reader.ReadByte();
            PipId = PipId.Deserialize(reader);

            int count = reader.ReadInt32Compact();
            Members = new List<AbsolutePath>(count);
            for (int idx = 0; idx < count; ++idx)
            {
                Members.Add(reader.ReadAbsolutePath());
            }

            EnumeratePatternRegex = reader.ReadString();
        }

        /// <inheritdoc />
        public void WriteFingerprintInputs(IFingerprinter writer)
        {
            writer.AddCollection<AbsolutePath, ReadOnlyArray<AbsolutePath>>("Members", Members.ToReadOnlyArray(), (h, v) => h.AddFileName(v));
        }
    }

    /// <summary>
    /// Observed input Hashes By Path
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public struct ObservedInputsEventData
    {
        /// <summary>
        /// The pip ID
        /// </summary>
        public PipId PipId;

        /// <summary>
        /// The observed inputs
        /// </summary>
        public ReadOnlyArray<ObservedInput> ObservedInputs;
    }

    /// <summary>
    /// Information about processes and file accesses for a pip
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public struct ProcessExecutionMonitoringReportedEventData : IExecutionLogEventData<ProcessExecutionMonitoringReportedEventData>
    {
        /// <summary>
        /// The pip ID
        /// </summary>
        public PipId PipId;

        /// <summary>
        /// The reported processes
        /// </summary>
        public IReadOnlyCollection<ReportedProcess> ReportedProcesses;

        /// <summary>
        /// The reported file accesses
        /// </summary>
        public IReadOnlyCollection<ReportedFileAccess> ReportedFileAccesses;

        /// <summary>
        /// The reported file accesses
        /// </summary>
        public IReadOnlyCollection<ReportedFileAccess> WhitelistedReportedFileAccesses;

        /// <summary>
        /// The reported Process Detouring Status messages
        /// </summary>
        public IReadOnlyCollection<ProcessDetouringStatusData> ProcessDetouringStatuses;

        /// <inheritdoc />
        public ExecutionLogEventMetadata<ProcessExecutionMonitoringReportedEventData> Metadata =>
            ExecutionLogMetadata.ProcessExecutionMonitoringReported;

        /// <inheritdoc />
        public void Serialize(BinaryLogger.EventWriter writer)
        {
            PipId.Serialize(writer);
            ExecutionResultSerializer.WriteReportedProcessesAndFileAccesses(
                writer,
                ReportedFileAccesses,
                WhitelistedReportedFileAccesses,
                ReportedProcesses);
            writer.Write(
                ProcessDetouringStatuses, 
                (w, v) => w.WriteReadOnlyList(v.ToList(), (w2, v2) => v2.Serialize(w2)));
        }

        /// <inheritdoc />
        public void DeserializeAndUpdate(BinaryLogReader.EventReader reader)
        {
            PipId = PipId.Deserialize(reader);
            ReportedFileAccess[] reportedFileAccesses;
            ReportedFileAccess[] whitelistedReportedFileAccesses;
            ReportedProcess[] reportedProcesses;
            ExecutionResultSerializer.ReadReportedProcessesAndFileAccesses(
                reader,
                out reportedFileAccesses,
                out whitelistedReportedFileAccesses,
                out reportedProcesses);
            ProcessDetouringStatuses = reader.ReadNullable(r => r.ReadReadOnlyList(r2 => ProcessDetouringStatusData.Deserialize(r2)));
            ReportedProcesses = reportedProcesses;
            ReportedFileAccesses = reportedFileAccesses;
            WhitelistedReportedFileAccesses = whitelistedReportedFileAccesses;
        }
    }

    /// <summary>
    /// Information about dependency analysis violation
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public struct DependencyViolationEventData : IExecutionLogEventData<DependencyViolationEventData>
    {
        /// <summary>
        /// The violator pip ID
        /// </summary>
        public PipId ViolatorPipId;

        /// <summary>
        /// The related pip ID
        /// </summary>
        public PipId RelatedPipId;

        /// <summary>
        /// Dependency-error classification
        /// </summary>
        public FileMonitoringViolationAnalyzer.DependencyViolationType ViolationType;

        /// <summary>
        /// Access type observed at a path (read or write)
        /// </summary>
        public FileMonitoringViolationAnalyzer.AccessLevel AccessLevel;

        /// <summary>
        /// The path causing the violation
        /// </summary>
        public AbsolutePath Path;

        /// <inheritdoc />
        public ExecutionLogEventMetadata<DependencyViolationEventData> Metadata => ExecutionLogMetadata.DependencyViolationReported;

        /// <inheritdoc />
        public void Serialize(BinaryLogger.EventWriter writer)
        {
            ViolatorPipId.Serialize(writer);
            RelatedPipId.Serialize(writer);
            writer.Write((byte)ViolationType);
            writer.Write((byte)AccessLevel);
            writer.Write(Path);
        }

        /// <inheritdoc />
        public void DeserializeAndUpdate(BinaryLogReader.EventReader reader)
        {
            ViolatorPipId = PipId.Deserialize(reader);
            RelatedPipId = PipId.Deserialize(reader);
            ViolationType = (FileMonitoringViolationAnalyzer.DependencyViolationType)reader.ReadByte();
            AccessLevel = (FileMonitoringViolationAnalyzer.AccessLevel)reader.ReadByte();
            Path = reader.ReadAbsolutePath();
        }
    }

    /// <summary>
    /// Information about PipExecutionStep performance
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public struct PipExecutionStepPerformanceEventData : IExecutionLogEventData<PipExecutionStepPerformanceEventData>
    {
        /// <summary>
        /// The pip Id
        /// </summary>
        public PipId PipId;

        /// <summary>
        /// Start time of the step
        /// </summary>
        public DateTime StartTime;

        /// <summary>
        /// Running time of the step in ms
        /// </summary>
        public TimeSpan Duration;

        /// <summary>
        /// PipExecutionStep which was executed
        /// </summary>
        public PipExecutionStep Step;

        /// <summary>
        /// Dispatcher kind which executed the PipExecutionStep
        /// </summary>
        public WorkDispatcher.DispatcherKind Dispatcher;

        /// <inheritdoc />
        public ExecutionLogEventMetadata<PipExecutionStepPerformanceEventData> Metadata => ExecutionLogMetadata.PipExecutionStepPerformanceReported;

        /// <inheritdoc />
        public void Serialize(BinaryLogger.EventWriter writer)
        {
            PipId.Serialize(writer);
            writer.Write(StartTime);
            writer.Write(Duration);
            writer.Write((byte)Step);
            writer.Write((byte)Dispatcher);
        }

        /// <inheritdoc />
        public void DeserializeAndUpdate(BinaryLogReader.EventReader reader)
        {
            PipId = PipId.Deserialize(reader);
            StartTime = reader.ReadDateTime();
            Duration = reader.ReadTimeSpan();
            Step = (PipExecutionStep)reader.ReadByte();
            Dispatcher = (WorkDispatcher.DispatcherKind)reader.ReadByte();
        }
    }

    /// <summary>
    /// Directory output content for a process pip
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public struct PipExecutionDirectoryOutputs : IExecutionLogEventData<PipExecutionDirectoryOutputs>
    {
        /// <nodoc/>
        public PipId PipId;

        /// <summary>
        /// Each directory output that was produced by the associated pip, with its content
        /// </summary>
        public ReadOnlyArray<(DirectoryArtifact directoryArtifact, ReadOnlyArray<FileArtifact> fileArtifactArray)> DirectoryOutputs;

        /// <nodoc/>
        public ExecutionLogEventMetadata<PipExecutionDirectoryOutputs> Metadata => ExecutionLogMetadata.PipExecutionDirectoryOutputs;

        /// <nodoc/>
        public void Serialize(BinaryLogger.EventWriter writer)
        {
            PipId.Serialize(writer);
            writer.WriteReadOnlyList(
                DirectoryOutputs,
                (w, directoryOutput) =>
                {
                    w.Write(directoryOutput.directoryArtifact);
                    writer.WriteReadOnlyList(
                        directoryOutput.fileArtifactArray,
                        (w2, fileArtifact) => w2.Write(fileArtifact));
                });
        }

        /// <nodoc/>
        public void DeserializeAndUpdate(BinaryLogReader.EventReader reader)
        {
            PipId = PipId.Deserialize(reader);
            DirectoryOutputs = reader.ReadReadOnlyArray(
                r =>
                    (reader.ReadDirectoryArtifact(), r.ReadReadOnlyArray(r2 => r2.ReadFileArtifact())));
        }
    }

    /// <summary>
    /// Information about dependency analysis violation
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public struct StatusEventData : IExecutionLogEventData<StatusEventData>
    {
        /// <summary>
        /// Time of the usage snapshot
        /// </summary>
        public DateTime Time;

        /// <summary>
        /// Cpu usage percent
        /// </summary>
        public int CpuPercent;

        /// <summary>
        /// Disk usage percents
        /// </summary>
        public int[] DiskPercents;

        /// <summary>
        /// Disk queue depths
        /// </summary>
        public int[] DiskQueueDepths;

        /// <summary>
        /// Ram usage percent
        /// </summary>
        public int RamPercent;

        /// <summary>
        /// Ram utilization in MB
        /// </summary>
        public int MachineRamUtilizationMB;

        /// <summary>
        /// Percentage of available commit used. Note if the machine has an expandable page file, this is based on the
        /// current size not necessarily the maximum size. So even if this hits 100%, the machine may still be able to
        /// commit more as the page file expands.
        /// </summary>
        public int CommitPercent;

        /// <summary>
        /// The machine's total commit in MB
        /// </summary>
        public int CommitTotalMB;

        /// <summary>
        /// CPU utilization of the current process
        /// </summary>
        public int ProcessCpuPercent;

        /// <summary>
        /// Working set in MB of the current process
        /// </summary>
        public int ProcessWorkingSetMB;

        /// <summary>
        /// Number of waiting items in the CPU dispatcher
        /// </summary>
        public int CpuWaiting;

        /// <summary>
        /// Number of running items in the CPU dispatcher
        /// </summary>
        public int CpuRunning;

        /// <summary>
        /// Concurrency limit in the IP dispatcher
        /// </summary>
        public int IoCurrentMax;

        /// <summary>
        /// Number of waiting items in the IO dispatcher
        /// </summary>
        public int IoWaiting;

        /// <summary>
        /// Number of running items in the IO dispatcher
        /// </summary>
        public int IoRunning;

        /// <summary>
        /// Number of waiting items in the CacheLookup dispatcher
        /// </summary>
        public int LookupWaiting;

        /// <summary>
        /// Number of running items in the CacheLookup dispatcher
        /// </summary>
        public int LookupRunning;

        /// <summary>
        /// Number of externally running processes
        /// </summary>
        public int ExternalProcesses;

        /// <summary>
        /// Number of pips succeeded for each type
        /// </summary>
        public long[] PipsSucceededAllTypes;

        /// <summary>
        /// LimitingResource heuristic during the sample
        /// </summary>
        public ExecutionSampler.LimitingResource LimitingResource;

        /// <summary>
        /// Factor of how much less frequently the status update time gets compared to what is expected. A value of 1 means
        /// it is fired exactly at the expected rate. 2 means it is trigged twice as slowly as expected. Etc.
        /// </summary>
        public int UnresponsivenessFactor;

        /// <summary>
        /// Number of process pips that have not completed yet
        /// </summary>
        public long ProcessPipsPending;

        /// <summary>
        /// Number of process pips allocated a slot on workers (including localworker)
        /// </summary>
        public long ProcessPipsAllocatedSlots;
        
        /// <inheritdoc />
        public ExecutionLogEventMetadata<StatusEventData> Metadata => ExecutionLogMetadata.ResourceUsageReported;

        /// <inheritdoc />
        public void Serialize(BinaryLogger.EventWriter writer)
        {
            writer.Write(Time);

            writer.Write(CpuPercent);

            writer.Write(DiskPercents.Length);
            foreach (var diskPercent in DiskPercents)
            {
                writer.Write(diskPercent);
            }

            writer.Write(DiskQueueDepths.Length);
            foreach (var diskQueueDepth in DiskQueueDepths)
            {
                writer.Write(diskQueueDepth);
            }

            writer.Write(RamPercent);
            writer.Write(ProcessCpuPercent);
            writer.Write(ProcessWorkingSetMB);

            writer.Write(CpuWaiting);
            writer.Write(CpuRunning);

            writer.Write(IoCurrentMax);
            writer.Write(IoWaiting);
            writer.Write(IoRunning);

            writer.Write(LookupWaiting);
            writer.Write(LookupRunning);

            writer.Write(ExternalProcesses);

            writer.Write(PipsSucceededAllTypes.Length);
            foreach (var pipsSucceeded in PipsSucceededAllTypes)
            {
                writer.Write(pipsSucceeded);
            }
        }

        /// <inheritdoc />
        public void DeserializeAndUpdate(BinaryLogReader.EventReader reader)
        {
            Time = reader.ReadDateTime();

            CpuPercent = reader.ReadInt32();

            var diskCount = reader.ReadInt32();
            DiskPercents = new int[diskCount];
            for (int i = 0; i < DiskPercents.Length; i++)
            {
                DiskPercents[i] = reader.ReadInt32();
            }

            var diskQueueDepthCount = reader.ReadInt32();
            DiskQueueDepths = new int[diskQueueDepthCount];
            for (int i = 0; i < DiskQueueDepths.Length; i++)
            {
                DiskQueueDepths[i] = reader.ReadInt32();
            }

            RamPercent = reader.ReadInt32();
            ProcessCpuPercent = reader.ReadInt32();
            ProcessWorkingSetMB = reader.ReadInt32();

            CpuWaiting = reader.ReadInt32();
            CpuRunning = reader.ReadInt32();

            IoCurrentMax = reader.ReadInt32();
            IoWaiting = reader.ReadInt32();
            IoRunning = reader.ReadInt32();

            LookupWaiting = reader.ReadInt32();
            LookupRunning = reader.ReadInt32();

            ExternalProcesses = reader.ReadInt32();

            var pipTypeLength = reader.ReadInt32();
            PipsSucceededAllTypes = new long[pipTypeLength];
            for (int i = 0; i < PipsSucceededAllTypes.Length; i++)
            {
                PipsSucceededAllTypes[i] = reader.ReadInt64();
            }
        }
    }

    /// <summary>
    /// Information about the build invocation
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    // $Rename: Due to telemetry backend scripts this cannot be renamed to BuildXL
    public struct DominoInvocationEventData : IExecutionLogEventData<DominoInvocationEventData>
    {
        /// <summary>
        /// The expanded configuration used to run the engine.
        /// </summary>
        /// <remarks>
        /// This follows the IConfiguration hierarchy as defined in the BuildXL repo for two reasons:
        /// a) familiarity and less confusion when not similar in structure.
        /// b) The ability to perhaps auto populate from the interface definitions
        /// </remarks>
        public ConfigurationData Configuration;

        /// <nodoc />
        public struct ConfigurationData
        {
            /// <nodoc />
            public LoggingConfigurationData Logging;

            /// <nodoc />
            public ConfigurationData(IConfiguration configuration)
            {
                Logging = new LoggingConfigurationData(configuration.Logging);
            }

            /// <nodoc />
            public void Serialize(BinaryLogger.EventWriter writer)
            {
                Logging.Serialize(writer);
            }

            /// <nodoc />
            public void DeserializeAndUpdate(BinaryLogReader.EventReader reader)
            {
                Logging.DeserializeAndUpdate(reader);
            }
        }

        /// <nodoc />
        public struct LoggingConfigurationData
        {
            /// <summary>
            /// <see cref="ILoggingConfiguration.SubstSource"/>.
            /// </summary>
            /// <nodoc />
            public AbsolutePath SubstSource;

            /// <summary>
            /// <see cref="ILoggingConfiguration.SubstTarget"/>.
            /// </summary>
            public AbsolutePath SubstTarget;

            /// <nodoc />
            public LoggingConfigurationData(ILoggingConfiguration configuration)
            {
                SubstSource = configuration.SubstSource;
                SubstTarget = configuration.SubstTarget;
            }

            /// <nodoc />
            public void Serialize(BinaryLogger.EventWriter writer)
            {
                writer.Write(SubstSource);
                writer.Write(SubstTarget);
            }

            /// <nodoc />
            public void DeserializeAndUpdate(BinaryLogReader.EventReader reader)
            {
                SubstSource = reader.ReadAbsolutePath();
                SubstTarget = reader.ReadAbsolutePath();
            }
        }

        /// <nodoc />
        // $Rename: Due to telemetry backend scripts this cannot be renamed to BuildXL
        public DominoInvocationEventData(IConfiguration configuration)
        {
            Configuration = new ConfigurationData(configuration);
        }

        /// <inheritdoc />
        public ExecutionLogEventMetadata<DominoInvocationEventData> Metadata => ExecutionLogMetadata.DominoInvocation;

        /// <inheritdoc />
        public void Serialize(BinaryLogger.EventWriter writer)
        {
            Configuration.Serialize(writer);
        }

        /// <inheritdoc />
        public void DeserializeAndUpdate(BinaryLogReader.EventReader reader)
        {
            Configuration.DeserializeAndUpdate(reader);
        }
    }
}
