// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using BuildXL.Engine;
using BuildXL.Processes;
using BuildXL.Scheduler.Graph;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using BuildXL.Xldb.Proto;
using AbsolutePath = BuildXL.Utilities.AbsolutePath;
using CopyFile = BuildXL.Pips.Operations.CopyFile;
using DirectoryArtifact = BuildXL.Utilities.DirectoryArtifact;
using Edge = BuildXL.Scheduler.Graph.Edge;
using FileArtifact = BuildXL.Utilities.FileArtifact;
using FileOrDirectoryArtifact = BuildXL.Utilities.FileOrDirectoryArtifact;
using Fingerprint = BuildXL.Cache.MemoizationStore.Interfaces.Sessions.Fingerprint;
using IpcPip = BuildXL.Pips.Operations.IpcPip;
using NodeId = BuildXL.Scheduler.Graph.NodeId;
using NodeRange = BuildXL.Scheduler.Graph.NodeRange;
using ObservedPathEntry = BuildXL.Scheduler.Fingerprints.ObservedPathEntry;
using ObservedPathSet = BuildXL.Scheduler.Fingerprints.ObservedPathSet;
using Pip = BuildXL.Pips.Operations.Pip;
using PipGraph = BuildXL.Scheduler.Graph.PipGraph;
using PipProvenance = BuildXL.Pips.Operations.PipProvenance;
using PipTable = BuildXL.Pips.PipTable;
using PipType = BuildXL.Pips.Operations.PipType;
using Process = BuildXL.Pips.Operations.Process;
using ProcessPipExecutionPerformance = BuildXL.Pips.ProcessPipExecutionPerformance;
using ReportedFileAccess = BuildXL.Processes.ReportedFileAccess;
using ReportedProcess = BuildXL.Processes.ReportedProcess;
using SealDirectory = BuildXL.Pips.Operations.SealDirectory;
using UnsafeOptions = BuildXL.Scheduler.Fingerprints.UnsafeOptions;
using WriteFile = BuildXL.Pips.Operations.WriteFile;

/// Many enums have been shifted or incremented and this is to avoid protobuf's design to not serialize 
/// int/enum values that are equal to 0. Thus we make "0" as an invalid value for each ProtoBuf enum.
namespace BuildXL.Execution.Analyzer
{
    /// <summary>
    /// Extension methods for Xldb ProtoBuf conversions.
    /// </summary>
    public static class XldbProtobufExtensions
    {
        /// <nodoc />
        public static FileArtifactContentDecidedEvent ToFileArtifactContentDecidedEvent(this FileArtifactContentDecidedEventData data, uint workerID, PathTable pathTable)
        {
            return new FileArtifactContentDecidedEvent()
            {
                WorkerID = workerID,
                FileArtifact = data.FileArtifact.ToFileArtifact(pathTable),
                FileContentInfo = new FileContentInfo
                {
                    LengthAndExistence = data.FileContentInfo.SerializedLengthAndExistence,
                    Hash = new ContentHash() { Value = data.FileContentInfo.Hash.ToString() }
                },
                OutputOrigin = (PipOutputOrigin)(data.OutputOrigin + 1)
            };
        }

        /// <nodoc />
        public static WorkerListEvent ToWorkerListEvent(this WorkerListEventData data, uint workerID)
        {
            var workerListEvent = new WorkerListEvent
            {
                WorkerID = workerID
            };

            workerListEvent.Workers.AddRange(data.Workers);
            return workerListEvent;
        }

        /// <nodoc />
        public static PipExecutionPerformanceEvent ToPipExecutionPerformanceEvent(this PipExecutionPerformanceEventData data)
        {
            var pipExecPerfEvent = new PipExecutionPerformanceEvent();
            var pipExecPerformance = new PipExecutionPerformance();
            pipExecPerformance.PipExecutionLevel = (int)data.ExecutionPerformance.ExecutionLevel;
            pipExecPerformance.ExecutionStart = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(data.ExecutionPerformance.ExecutionStart);
            pipExecPerformance.ExecutionStop = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(data.ExecutionPerformance.ExecutionStop);

            var processPipExecPerformance = new Xldb.Proto.ProcessPipExecutionPerformance();
            var performance = data.ExecutionPerformance as ProcessPipExecutionPerformance;
            if (performance != null)
            {
                processPipExecPerformance.ProcessExecutionTime = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(performance.ProcessExecutionTime);
                processPipExecPerformance.ReadCounters = new IOTypeCounters()
                {
                    OperationCount = performance.IO.ReadCounters.OperationCount,
                    TransferCOunt = performance.IO.ReadCounters.TransferCount
                };

                processPipExecPerformance.WriteCounters = new IOTypeCounters()
                {
                    OperationCount = performance.IO.WriteCounters.OperationCount,
                    TransferCOunt = performance.IO.WriteCounters.TransferCount
                };

                processPipExecPerformance.OtherCounters = new IOTypeCounters()
                {
                    OperationCount = performance.IO.OtherCounters.OperationCount,
                    TransferCOunt = performance.IO.OtherCounters.TransferCount
                };

                processPipExecPerformance.UserTime = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(performance.UserTime);
                processPipExecPerformance.KernelTime = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(performance.KernelTime);
                processPipExecPerformance.PeakMemoryUsage = performance.PeakMemoryUsage;
                processPipExecPerformance.PeakMemoryUsageMb = performance.PeakMemoryUsageMb;
                processPipExecPerformance.NumberOfProcesses = performance.NumberOfProcesses;

                processPipExecPerformance.FileMonitoringViolationCounters = new FileMonitoringViolationCounters()
                {
                    NumFileAccessesWhitelistedAndCacheable = performance.FileMonitoringViolations.NumFileAccessesWhitelistedAndCacheable,
                    NumFileAccessesWhitelistedButNotCacheable = performance.FileMonitoringViolations.NumFileAccessesWhitelistedButNotCacheable,
                    NumFileAccessViolationsNotWhitelisted = performance.FileMonitoringViolations.NumFileAccessViolationsNotWhitelisted
                };

                processPipExecPerformance.Fingerprint = performance.Fingerprint.ToFingerprint();

                if (performance.CacheDescriptorId.HasValue)
                {
                    processPipExecPerformance.CacheDescriptorId = performance.CacheDescriptorId.Value;
                }
            }

            pipExecPerfEvent.WorkerID = data.ExecutionPerformance.WorkerId;
            pipExecPerfEvent.PipID = data.PipId.Value;
            pipExecPerfEvent.PipExecutionPerformance = pipExecPerformance;
            pipExecPerfEvent.ProcessPipExecutionPerformance = processPipExecPerformance;
            return pipExecPerfEvent;
        }

        /// <nodoc />
        public static DirectoryMembershipHashedEvent ToDirectoryMembershipHashedEvent(this DirectoryMembershipHashedEventData data, uint workerID, PathTable pathTable)
        {
            var directoryMembershipEvent = new DirectoryMembershipHashedEvent()
            {
                WorkerID = workerID,
                DirectoryFingerprint = new DirectoryFingerprint()
                {
                    Hash = new ContentHash() { Value = data.DirectoryFingerprint.Hash.ToString() }
                },
                Directory = data.Directory.ToAbsolutePath(pathTable),
                IsStatic = data.IsSearchPath,
                IsSearchPath = data.IsSearchPath,
                PipID = data.PipId.Value,
                EnumeratePatternRegex = data.EnumeratePatternRegex ?? ""
            };

            directoryMembershipEvent.Members.AddRange(data.Members.Select(member => member.ToAbsolutePath(pathTable)));

            return directoryMembershipEvent;
        }

        /// <nodoc />
        public static ProcessExecutionMonitoringReportedEvent ToProcessExecutionMonitoringReportedEvent(this ProcessExecutionMonitoringReportedEventData data, uint workerID, PathTable pathTable)
        {
            var processExecutionMonitoringReportedEvent = new ProcessExecutionMonitoringReportedEvent
            {
                WorkerID = workerID,
                PipID = data.PipId.Value
            };

            processExecutionMonitoringReportedEvent.ReportedProcesses.AddRange(
                data.ReportedProcesses.Select(rp => rp.ToReportedProcess()));
            processExecutionMonitoringReportedEvent.ReportedFileAccesses.AddRange(
                data.ReportedFileAccesses.Select(reportedFileAccess => reportedFileAccess.ToReportedFileAccess(pathTable)));
            processExecutionMonitoringReportedEvent.WhitelistedReportedFileAccesses.AddRange(
                data.WhitelistedReportedFileAccesses.Select(
                    whiteListReportedFileAccess => whiteListReportedFileAccess.ToReportedFileAccess(pathTable)));

            foreach (var processDetouringStatus in data.ProcessDetouringStatuses)
            {
                processExecutionMonitoringReportedEvent.ProcessDetouringStatuses.Add(new Xldb.Proto.ProcessDetouringStatusData()
                {
                    ProcessID = processDetouringStatus.ProcessId,
                    ReportStatus = processDetouringStatus.ReportStatus,
                    ProcessName = processDetouringStatus.ProcessName,
                    StartApplicationName = processDetouringStatus.StartApplicationName,
                    StartCommandLine = processDetouringStatus.StartCommandLine,
                    NeedsInjection = processDetouringStatus.NeedsInjection,
                    Job = processDetouringStatus.Job,
                    DisableDetours = processDetouringStatus.DisableDetours,
                    CreationFlags = processDetouringStatus.CreationFlags,
                    Detoured = processDetouringStatus.Detoured,
                    Error = processDetouringStatus.Error
                });
            }

            return processExecutionMonitoringReportedEvent;
        }

        /// <nodoc />
        public static ProcessFingerprintComputationEvent ToProcessFingerprintComputationEvent(this ProcessFingerprintComputationEventData data, uint workerID, PathTable pathTable)
        {
            var processFingerprintComputationEvent = new ProcessFingerprintComputationEvent
            {
                WorkerID = workerID,
                Kind = (Xldb.Proto.FingerprintComputationKind)(data.Kind + 1),
                PipID = data.PipId.Value,
                WeakFingerprint = new WeakContentFingerPrint()
                {
                    Hash = data.WeakFingerprint.Hash.ToFingerprint()
                },
            };

            foreach (var strongFingerprintComputation in data.StrongFingerprintComputations)
            {
                var processStrongFingerprintComputationData = new Xldb.Proto.ProcessStrongFingerprintComputationData()
                {
                    PathSet = strongFingerprintComputation.PathSet.ToObservedPathSet(pathTable),
                    PathSetHash = new ContentHash()
                    {
                        Value = strongFingerprintComputation.PathSetHash.ToString()
                    },
                    UnsafeOptions = strongFingerprintComputation.UnsafeOptions.ToUnsafeOptions(),
                    Succeeded = strongFingerprintComputation.Succeeded,
                    IsStrongFingerprintHit = strongFingerprintComputation.IsStrongFingerprintHit,
                    ComputedStrongFingerprint = new StrongContentFingerPrint()
                    {
                        Hash = strongFingerprintComputation.ComputedStrongFingerprint.Hash.ToFingerprint()
                    }
                };

                processStrongFingerprintComputationData.PathEntries.AddRange(
                    strongFingerprintComputation.PathEntries.Select(
                        pathEntry => pathEntry.ToObservedPathEntry(pathTable)));
                processStrongFingerprintComputationData.ObservedAccessedFileNames.AddRange(
                    strongFingerprintComputation.ObservedAccessedFileNames.Select(
                        observedAccessedFileName => observedAccessedFileName.ToString(pathTable)));
                processStrongFingerprintComputationData.PriorStrongFingerprints.AddRange(
                    strongFingerprintComputation.PriorStrongFingerprints.Select(
                        priorStrongFingerprint => new StrongContentFingerPrint() { Hash = priorStrongFingerprint.Hash.ToFingerprint() }));

                foreach (var observedInput in strongFingerprintComputation.ObservedInputs)
                {
                    processStrongFingerprintComputationData.ObservedInputs.Add(new ObservedInput()
                    {
                        Type = (ObservedInputType)(observedInput.Type + 1),
                        Hash = new ContentHash()
                        {
                            Value = observedInput.Hash.ToString()
                        },
                        PathEntry = observedInput.PathEntry.ToObservedPathEntry(pathTable),
                        Path = observedInput.Path.ToAbsolutePath(pathTable),
                        IsSearchPath = observedInput.IsSearchPath,
                        IsDirectoryPath = observedInput.IsDirectoryPath,
                        DirectoryEnumeration = observedInput.DirectoryEnumeration
                    });
                }

                processFingerprintComputationEvent.StrongFingerprintComputations.Add(processStrongFingerprintComputationData);
            }

            return processFingerprintComputationEvent;
        }

        /// <nodoc />
        public static ExtraEventDataReported ToExtraEventDataReported(this ExtraEventData data, uint workerID)
        {
            return new ExtraEventDataReported
            {
                WorkerID = workerID,
                DisableDetours = data.DisableDetours,
                IgnoreReparsePoints = data.IgnoreReparsePoints,
                IgnorePreloadedDlls = data.IgnorePreloadedDlls,
                ExistingDirectoryProbesAsEnumerations = data.ExistingDirectoryProbesAsEnumerations,
                NtFileCreateMonitored = data.NtFileCreateMonitored,
                ZwFileCreateOpenMonitored = data.ZwFileCreateOpenMonitored,
                IgnoreZwRenameFileInformation = data.IgnoreZwRenameFileInformation,
                IgnoreZwOtherFileInformation = data.IgnoreZwOtherFileInformation,
                IgnoreNonCreateFileReparsePoints = data.IgnoreNonCreateFileReparsePoints,
                IgnoreSetFileInformationByHandle = data.IgnoreSetFileInformationByHandle,
                IgnoreGetFinalPathNameByHandle = data.IgnoreGetFinalPathNameByHandle,
                FingerprintVersion = (int)data.FingerprintVersion,
                FingerprintSalt = data.FingerprintSalt,
                SearchPathToolsHash = new ContentHash() { Value = data.SearchPathToolsHash.ToString() },
                UnexpectedFileAccessesAreErrors = data.UnexpectedFileAccessesAreErrors,
                MonitorFileAccesses = data.MonitorFileAccesses,
                MaskUntrackedAccesses = data.MaskUntrackedAccesses,
                NormalizeReadTimestamps = data.NormalizeReadTimestamps,
                PipWarningsPromotedToErrors = data.PipWarningsPromotedToErrors,
                ValidateDistribution = data.ValidateDistribution,
                RequiredKextVersionNumber = data.RequiredKextVersionNumber
            };
        }

        /// <nodoc />
        public static DependencyViolationReportedEvent ToDependencyViolationReportedEvent(this DependencyViolationEventData data, uint workerID, PathTable pathTable)
        {
            return new DependencyViolationReportedEvent()
            {
                WorkerID = workerID,
                ViolatorPipID = data.ViolatorPipId.Value,
                RelatedPipID = data.RelatedPipId.Value,
                ViolationType = (FileMonitoringViolationAnalyzer_DependencyViolationType)(data.ViolationType + 1),
                AccessLevel = (FileMonitoringViolationAnalyzer_AccessLevel)(data.AccessLevel + 1),
                Path = data.Path.ToAbsolutePath(pathTable)
            };
        }

        /// <nodoc />
        public static PipExecutionStepPerformanceReportedEvent ToPipExecutionStepPerformanceReportedEvent(this PipExecutionStepPerformanceEventData data, uint workerID)
        {
            var pipExecStepPerformanceEvent = new PipExecutionStepPerformanceReportedEvent
            {
                WorkerID = workerID,
                PipID = data.PipId.Value,
                StartTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(data.StartTime),
                Duration = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(data.Duration),
                Step = (PipExecutionStep)(data.Step + 1),
                Dispatcher = (WorkDispatcher_DispatcherKind)(data.Dispatcher + 1)
            };

            return pipExecStepPerformanceEvent;
        }

        /// <nodoc />
        public static PipCacheMissEvent ToPipCacheMissEvent(this PipCacheMissEventData data, uint workerID)
        {
            return new PipCacheMissEvent()
            {
                WorkerID = workerID,
                PipID = data.PipId.Value,
                CacheMissType = (PipCacheMissType)data.CacheMissType
            };
        }

        /// <nodoc />
        public static StatusReportedEvent ToResourceUsageReportedEvent(this StatusEventData data, uint workerID)
        {
            var statusReportedEvent = new StatusReportedEvent()
            {
                WorkerID = workerID,
                Time = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(data.Time),
                CpuPercent = data.CpuPercent,
                RamPercent = data.RamPercent,
                MachineRamUtilizationMB = data.MachineRamUtilizationMB,
                CommitPercent = data.CommitPercent,
                CommitTotalMB = data.CommitTotalMB,
                ProcessCpuPercent = data.ProcessCpuPercent,
                ProcessWorkingSetMB = data.ProcessWorkingSetMB,
                CpuWaiting = data.CpuWaiting,
                CpuRunning = data.CpuRunning,
                IoCurrentMax = data.IoCurrentMax,
                IoWaiting = data.IoWaiting,
                IoRunning = data.IoRunning,
                LookupWaiting = data.LookupWaiting,
                LookupRunning = data.LookupRunning,
                ExternalProcesses = data.ExternalProcesses,
                LimitingResource = (ExecutionSampler_LimitingResource)(data.LimitingResource + 1),
                UnresponsivenessFactor = data.UnresponsivenessFactor,
                ProcessPipsPending = data.ProcessPipsPending,
                ProcessPipsAllocatedSlots = data.ProcessPipsAllocatedSlots
            };

            statusReportedEvent.DiskPercents.AddRange(data.DiskPercents);
            statusReportedEvent.DiskQueueDepths.AddRange(data.DiskQueueDepths);
            statusReportedEvent.PipsSucceededAllTypes.AddRange(data.PipsSucceededAllTypes);

            return statusReportedEvent;
        }

        /// <nodoc />
        public static BXLInvocationEvent ToBXLInvocationEvent(this DominoInvocationEventData data, uint workerID, PathTable pathTable)
        {
            var loggingConfig = data.Configuration.Logging;

            var bxlInvEvent = new BXLInvocationEvent
            {
                WorkerID = workerID,
                SubstSource = loggingConfig.SubstSource.ToAbsolutePath(pathTable),
                SubstTarget = loggingConfig.SubstTarget.ToAbsolutePath(pathTable),
                IsSubstSourceValid = loggingConfig.SubstSource.IsValid,
                IsSubstTargetValid = loggingConfig.SubstTarget.IsValid
            };

            return bxlInvEvent;
        }

        /// <nodoc />
        public static Xldb.Proto.ReportedFileAccess ToReportedFileAccess(this ReportedFileAccess reportedFileAccess, PathTable pathTable)
        {
            return new Xldb.Proto.ReportedFileAccess()
            {
                // No need to + 1 here since the Bxl version of the enum never conained a 0 value, so adding Unspecified=0 did not change the bxl->protobuf enum mapping
                CreationDisposition = (Xldb.Proto.CreationDisposition)reportedFileAccess.CreationDisposition,
                // No need to + 1 here since the Bxl version of the enum never conained a 0 value, so adding Unspecified=0 did not change the bxl->protobuf enum mapping
                // However, GENERIC_READ is of value 2^31 in bxl code, but -2^31 in protobuf enum due to 2^31 - 1 being the maximum value of an enum in protobuf. Thus special ternary assignment here.
                DesiredAccess = reportedFileAccess.DesiredAccess == Processes.DesiredAccess.GENERIC_READ ? Xldb.Proto.DesiredAccess.GenericRead : (Xldb.Proto.DesiredAccess)reportedFileAccess.DesiredAccess,
                Error = reportedFileAccess.Error,
                Usn = reportedFileAccess.Usn.Value,
                // No need to + 1 here since the Bxl version of the enum never conained a 0 value, so adding Unspecified=0 did not change the bxl->protobuf enum mapping
                // However, WRITE_THROUGH is of value 2^31 in bxl code, but -2^31 in protobuf enum due to 2^31 - 1 being the maximum value of an enum in protobuf. Thus special ternary assignment here.
                FlagsAndAttributes = reportedFileAccess.FlagsAndAttributes == Processes.FlagsAndAttributes.FILE_FLAG_WRITE_THROUGH ? Xldb.Proto.FlagsAndAttributes.FileFlagWriteThrough : (Xldb.Proto.FlagsAndAttributes)reportedFileAccess.FlagsAndAttributes,
                Path = reportedFileAccess.Path,
                ManifestPath = reportedFileAccess.ManifestPath.ToString(pathTable, PathFormat.Windows),
                Process = reportedFileAccess.Process.ToReportedProcess(),
                ShareMode = reportedFileAccess.ShareMode == Processes.ShareMode.FILE_SHARE_NONE ? Xldb.Proto.ShareMode.FileShareNone : (Xldb.Proto.ShareMode)((int)reportedFileAccess.ShareMode << 1),
                Status = (Xldb.Proto.FileAccessStatus)(reportedFileAccess.Status + 1),
                Method = (Xldb.Proto.FileAccessStatusMethod)(reportedFileAccess.Method + 1),
                RequestedAccess = reportedFileAccess.RequestedAccess == Processes.RequestedAccess.None ? Xldb.Proto.RequestedAccess.None : (Xldb.Proto.RequestedAccess)((int)reportedFileAccess.RequestedAccess << 1),
                Operation = (Xldb.Proto.ReportedFileOperation)(reportedFileAccess.Operation + 1),
                ExplicitlyReported = reportedFileAccess.ExplicitlyReported,
                EnumeratePattern = reportedFileAccess.EnumeratePattern
            };
        }

        /// <nodoc />
        public static Xldb.Proto.ObservedPathSet ToObservedPathSet(this ObservedPathSet pathSet, PathTable pathTable)
        {
            var observedPathSet = new Xldb.Proto.ObservedPathSet();
            observedPathSet.Paths.AddRange(pathSet.Paths.Select(pathEntry => pathEntry.ToObservedPathEntry(pathTable)));
            observedPathSet.ObservedAccessedFileNames.AddRange(
                pathSet.ObservedAccessedFileNames.Select(
                    observedAccessedFileName => observedAccessedFileName.ToString(pathTable)));
            observedPathSet.UnsafeOptions = pathSet.UnsafeOptions.ToUnsafeOptions();

            return observedPathSet;
        }

        /// <nodoc />
        public static Xldb.Proto.UnsafeOptions ToUnsafeOptions(this UnsafeOptions unsafeOption)
        {
            var unsafeOpt = new Xldb.Proto.UnsafeOptions()
            {
                PreserveOutputsSalt = new ContentHash()
                {
                    Value = unsafeOption.PreserveOutputsSalt.ToString()
                },
                UnsafeConfiguration = new UnsafeSandboxConfiguration()
                {
                    PreserveOutputs = (PreserveOutputsMode)(unsafeOption.UnsafeConfiguration.PreserveOutputs + 1),
                    MonitorFileAccesses = unsafeOption.UnsafeConfiguration.MonitorFileAccesses,
                    IgnoreZwRenameFileInformation = unsafeOption.UnsafeConfiguration.IgnoreZwRenameFileInformation,
                    IgnoreZwOtherFileInformation = unsafeOption.UnsafeConfiguration.IgnoreZwOtherFileInformation,
                    IgnoreNonCreateFileReparsePoints = unsafeOption.UnsafeConfiguration.IgnoreNonCreateFileReparsePoints,
                    IgnoreSetFileInformationByHandle = unsafeOption.UnsafeConfiguration.IgnoreSetFileInformationByHandle,
                    IgnoreReparsePoints = unsafeOption.UnsafeConfiguration.IgnoreReparsePoints,
                    IgnorePreloadedDlls = unsafeOption.UnsafeConfiguration.IgnorePreloadedDlls,
                    ExistingDirectoryProbesAsEnumerations = unsafeOption.UnsafeConfiguration.ExistingDirectoryProbesAsEnumerations,
                    MonitorNtCreateFile = unsafeOption.UnsafeConfiguration.MonitorNtCreateFile,
                    MonitorZwCreateOpenQueryFile = unsafeOption.UnsafeConfiguration.MonitorZwCreateOpenQueryFile,
                    SandboxKind = (SandboxKind)(unsafeOption.UnsafeConfiguration.SandboxKind + 1),
                    UnexpectedFileAccessesAreErrors = unsafeOption.UnsafeConfiguration.UnexpectedFileAccessesAreErrors,
                    IgnoreGetFinalPathNameByHandle = unsafeOption.UnsafeConfiguration.IgnoreGetFinalPathNameByHandle,
                    IgnoreDynamicWritesOnAbsentProbes = unsafeOption.UnsafeConfiguration.IgnoreDynamicWritesOnAbsentProbes,
                    IgnoreUndeclaredAccessesUnderSharedOpaques = unsafeOption.UnsafeConfiguration.IgnoreUndeclaredAccessesUnderSharedOpaques,
                }
            };

            if (unsafeOption.UnsafeConfiguration.DoubleWritePolicy != null)
            {
                unsafeOpt.UnsafeConfiguration.DoubleWritePolicy = (DoubleWritePolicy)(unsafeOption.UnsafeConfiguration.DoubleWritePolicy + 1);
            }

            return unsafeOpt;
        }

        public static string ToString(this StringId stringId, PathTable pathTable)
        {
            return stringId.IsValid ? stringId.ToString(pathTable.StringTable) : "";
        }

        /// <nodoc />
        public static Xldb.Proto.AbsolutePath ToAbsolutePath(this AbsolutePath path, PathTable pathTable)
        {
            return new Xldb.Proto.AbsolutePath()
            {
                Value = path.ToString(pathTable, PathFormat.Windows)
            };
        }

        /// <nodoc />
        public static Xldb.Proto.FileArtifact ToFileArtifact(this FileArtifact fileArtifact, PathTable pathTable)
        {
            return !fileArtifact.IsValid ? null : new Xldb.Proto.FileArtifact
            {
                Path = fileArtifact.Path.ToAbsolutePath(pathTable),
                RewriteCount = fileArtifact.RewriteCount,
            };
        }

        /// <nodoc />
        public static Xldb.Proto.ReportedProcess ToReportedProcess(this ReportedProcess reportedProcess)
        {
            return new Xldb.Proto.ReportedProcess()
            {
                Path = reportedProcess.Path,
                ProcessId = reportedProcess.ProcessId,
                ProcessArgs = reportedProcess.ProcessArgs,
                ReadCounters = new IOTypeCounters
                {
                    OperationCount = reportedProcess.IOCounters.ReadCounters.OperationCount,
                    TransferCOunt = reportedProcess.IOCounters.ReadCounters.TransferCount
                },
                WriteCounters = new IOTypeCounters
                {
                    OperationCount = reportedProcess.IOCounters.WriteCounters.OperationCount,
                    TransferCOunt = reportedProcess.IOCounters.WriteCounters.TransferCount
                },
                OtherCounters = new IOTypeCounters
                {
                    OperationCount = reportedProcess.IOCounters.OtherCounters.OperationCount,
                    TransferCOunt = reportedProcess.IOCounters.OtherCounters.TransferCount
                },
                CreationTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(reportedProcess.CreationTime),
                ExitTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(reportedProcess.ExitTime),
                KernelTime = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(reportedProcess.KernelTime),
                UserTime = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(reportedProcess.UserTime),
                ExitCode = reportedProcess.ExitCode,
                ParentProcessId = reportedProcess.ParentProcessId
            };
        }

        /// <nodoc />
        public static Xldb.Proto.ObservedPathEntry ToObservedPathEntry(this ObservedPathEntry pathEntry, PathTable pathTable)
        {
            return new Xldb.Proto.ObservedPathEntry()
            {
                Path = pathEntry.Path.ToAbsolutePath(pathTable),
                EnumeratePatternRegex = pathEntry.EnumeratePatternRegex ?? ""
            };
        }

        /// <nodoc />
        public static Xldb.Proto.Fingerprint ToFingerprint(this Fingerprint fingerprint)
        {
            return new Xldb.Proto.Fingerprint()
            {
                Length = fingerprint.Length,
                Bytes = Google.Protobuf.ByteString.CopyFrom(fingerprint.ToByteArray())
            };
        }

        /// <nodoc />
        public static Xldb.Proto.DirectoryArtifact ToDirectoryArtifact(this DirectoryArtifact artifact, PathTable pathTable)
        {
            return !artifact.IsValid ? null : new Xldb.Proto.DirectoryArtifact()
            {
                Path = artifact.Path.ToAbsolutePath(pathTable),
                PartialSealID = artifact.PartialSealId,
                IsSharedOpaque = artifact.IsSharedOpaque
            };
        }

        /// <nodoc />
        public static Xldb.Proto.PipProvenance ToPipProvenance(this PipProvenance provenance, PathTable pathTable)
        {
            return provenance == null ? null : new Xldb.Proto.PipProvenance()
            {
                Usage = provenance.Usage.IsValid ? provenance.Usage.ToString(pathTable) : "",
                ModuleId = provenance.ModuleId.Value.ToString(pathTable),
                ModuleName = provenance.ModuleName.ToString(pathTable),
                SemiStableHash = provenance.SemiStableHash
            };
        }

        /// <nodoc />
        public static Xldb.Proto.FileOrDirectoryArtifact ToFileOrDirectoryArtifact(this FileOrDirectoryArtifact artifact, PathTable pathTable)
        {
            if (!artifact.IsValid)
            {
                return null;
            }

            var xldbFileOrDirectoryArtifact = new Xldb.Proto.FileOrDirectoryArtifact();
            if (artifact.IsDirectory)
            {
                xldbFileOrDirectoryArtifact.IsDirectory = true;
                xldbFileOrDirectoryArtifact.DirectoryArtifact = artifact.DirectoryArtifact.ToDirectoryArtifact(pathTable);
            }
            else
            {
                xldbFileOrDirectoryArtifact.IsFile = true;
                xldbFileOrDirectoryArtifact.FileArtifact = artifact.FileArtifact.ToFileArtifact(pathTable);
            }

            return xldbFileOrDirectoryArtifact;
        }

        /// <nodoc />
        public static Xldb.Proto.Pip ToPip(this Pip pip, CachedGraph cachedGraph)
        {
            var xldbPip = new Xldb.Proto.Pip()
            {
                SemiStableHash = pip.SemiStableHash,
                PipId = pip.PipId.Value,
            };

            foreach (var incomingEdge in cachedGraph.DataflowGraph.GetIncomingEdges(pip.PipId.ToNodeId()))
            {
                var pipType = cachedGraph.PipTable.HydratePip(incomingEdge.OtherNode.ToPipId(), Pips.PipQueryContext.Explorer).PipType;

                if (pipType != PipType.Value && pipType != PipType.HashSourceFile && pipType != PipType.SpecFile && pipType != PipType.Module)
                {
                    xldbPip.IncomingEdges.Add(incomingEdge.ToEdge());
                }
            }

            foreach (var outgoingEdge in cachedGraph.DataflowGraph.GetOutgoingEdges(pip.PipId.ToNodeId()))
            {
                var pipType = cachedGraph.PipTable.HydratePip(outgoingEdge.OtherNode.ToPipId(), Pips.PipQueryContext.Explorer).PipType;

                if (pipType != PipType.Value && pipType != PipType.HashSourceFile && pipType != PipType.SpecFile && pipType != PipType.Module)
                {
                    xldbPip.OutgoingEdges.Add(outgoingEdge.ToEdge());
                }
            }

            return xldbPip;
        }

        /// <nodoc />
        public static Xldb.Proto.SealDirectory ToSealDirectory(this SealDirectory pip, PathTable pathTable, Xldb.Proto.Pip parentPip)
        {
            var xldbSealDirectory = new Xldb.Proto.SealDirectory
            {
                GraphInfo = parentPip,
                Kind = (SealDirectoryKind)(pip.Kind + 1),
                IsComposite = pip.IsComposite,
                Scrub = pip.Scrub,
                Directory = pip.Directory.ToDirectoryArtifact(pathTable),
                IsSealSourceDirectory = pip.IsSealSourceDirectory,
                Provenance = pip.Provenance.ToPipProvenance(pathTable),
            };

            xldbSealDirectory.Patterns.AddRange(pip.Patterns.Select(key => key.ToString(pathTable)));
            xldbSealDirectory.Contents.AddRange(pip.Contents.Select(file => file.ToFileArtifact(pathTable)));
            xldbSealDirectory.ComposedDirectories.AddRange(pip.ComposedDirectories.Select(dir => dir.ToDirectoryArtifact(pathTable)));

            if (pip.Tags.IsValid)
            {
                xldbSealDirectory.Tags.AddRange(pip.Tags.Select(key => key.ToString(pathTable)));
            }

            return xldbSealDirectory;
        }

        /// <nodoc />
        public static Xldb.Proto.CopyFile ToCopyFile(this CopyFile pip, PathTable pathTable, Xldb.Proto.Pip parentPip)
        {
            var xldbCopyFile = new Xldb.Proto.CopyFile
            {
                GraphInfo = parentPip,
                Source = pip.Source.ToFileArtifact(pathTable),
                Destination = pip.Destination.ToFileArtifact(pathTable),
                OutputsMustRemainWritable = pip.OutputsMustRemainWritable,
                Provenance = pip.Provenance.ToPipProvenance(pathTable),
            };

            if (pip.Tags.IsValid)
            {
                xldbCopyFile.Tags.AddRange(pip.Tags.Select(key => key.ToString(pathTable)));
            }

            return xldbCopyFile;
        }

        /// <nodoc />
        public static Xldb.Proto.WriteFile ToWriteFile(this WriteFile pip, PathTable pathTable, Xldb.Proto.Pip parentPip)
        {
            var xldbWriteFile = new Xldb.Proto.WriteFile
            {
                GraphInfo = parentPip,
                Destination = pip.Destination.ToFileArtifact(pathTable),
                Contents = pip.Contents.IsValid ? pip.Contents.ToString(pathTable) : "",
                Encoding = (WriteFileEncoding)(pip.Encoding + 1),
                Provenance = pip.Provenance.ToPipProvenance(pathTable),
            };

            if (pip.Tags.IsValid)
            {
                xldbWriteFile.Tags.AddRange(pip.Tags.Select(key => key.ToString(pathTable)));
            }

            return xldbWriteFile;
        }

        /// <nodoc />
        public static ProcessPip ToProcessPip(this Process pip, PathTable pathTable, Xldb.Proto.Pip parentPip)
        {
            var xldbProcessPip = new ProcessPip
            {
                GraphInfo = parentPip,
                ProcessOptions = pip.ProcessOptions == Process.Options.None ? Options.None : (Options)((int)pip.ProcessOptions << 1),
                StandardInputFile = pip.StandardInputFile.ToFileArtifact(pathTable),
                StandardInputData = pip.StandardInputData.IsValid ? pip.StandardInputData.ToString(pathTable) : "",
                StandardInput = !pip.StandardInput.IsValid ? null : new StandardInput()
                {
                    File = pip.StandardInput.File.ToFileArtifact(pathTable),
                    Data = pip.StandardInput.Data.ToString(pathTable),
                },
                ResponseFile = pip.ResponseFile.ToFileArtifact(pathTable),
                ResponseFileData = pip.ResponseFileData.IsValid ? pip.ResponseFileData.ToString(pathTable) : "",
                Executable = pip.Executable.ToFileArtifact(pathTable),
                ToolDescription = pip.ToolDescription.ToString(pathTable),
                WorkingDirectory = pip.WorkingDirectory.ToAbsolutePath(pathTable),
                Arguments = pip.Arguments.IsValid ? pip.Arguments.ToString(pathTable) : "",
                TempDirectory = pip.TempDirectory.ToAbsolutePath(pathTable),
                Provenance = pip.Provenance.ToPipProvenance(pathTable),
            };

            if (pip.ServiceInfo.IsValid)
            {
                var serviceInfo = new ServiceInfo
                {
                    Kind = (ServicePipKind)(pip.ServiceInfo.Kind + 1),
                    ShutdownPipId = pip.ServiceInfo.ShutdownPipId.Value,
                    IsStartOrShutdownKind = pip.ServiceInfo.IsStartOrShutdownKind
                };

                serviceInfo.ServicePipDependencies.AddRange(pip.ServiceInfo.ServicePipDependencies.Select(key => key.Value));
                serviceInfo.FinalizationPipIds.AddRange(pip.ServiceInfo.FinalizationPipIds.Select(key => key.Value));
                xldbProcessPip.ServiceInfo = serviceInfo;
            }

            xldbProcessPip.EnvironmentVariable.AddRange(pip.EnvironmentVariables.Select(
                envVar => new EnvironmentVariable()
                {
                    Name = envVar.Name.ToString(pathTable),
                    Value = envVar.Value.IsValid ? envVar.Value.ToString(pathTable) : "",
                    IsPassThrough = envVar.IsPassThrough
                }));
            xldbProcessPip.Dependencies.AddRange(pip.Dependencies.Select(file => file.ToFileArtifact(pathTable)));
            xldbProcessPip.DirectoryDependencies.AddRange(pip.DirectoryDependencies.Select(dir => dir.ToDirectoryArtifact(pathTable)));
            xldbProcessPip.UntrackedPaths.AddRange(pip.UntrackedPaths.Select(path => path.ToAbsolutePath(pathTable)));
            xldbProcessPip.UntrackedScopes.AddRange(pip.UntrackedScopes.Select(path => path.ToAbsolutePath(pathTable)));
            xldbProcessPip.FileOutputs.AddRange(pip.FileOutputs.Select(output => !output.IsValid ? null : new Xldb.Proto.FileArtifactWithAttributes()
            {
                Path = output.Path.ToAbsolutePath(pathTable),
                RewriteCount = output.RewriteCount,
                FileExistence = (Xldb.Proto.FileExistence)(output.FileExistence + 1)
            }));
            xldbProcessPip.DirectoryOutputs.AddRange(pip.DirectoryOutputs.Select(dir => dir.ToDirectoryArtifact(pathTable)));
            xldbProcessPip.AdditionalTempDirectories.AddRange(pip.AdditionalTempDirectories.Select(dir => dir.ToAbsolutePath(pathTable)));
            xldbProcessPip.PreserveOutputWhitelist.AddRange(pip.PreserveOutputWhitelist.Select(path => path.ToAbsolutePath(pathTable)));

            if (pip.Tags.IsValid)
            {
                xldbProcessPip.Tags.AddRange(pip.Tags.Select(key => key.ToString(pathTable)));
            }

            return xldbProcessPip;
        }

        /// <nodoc />
        public static Xldb.Proto.IpcPip ToIpcPip(this IpcPip pip, PathTable pathTable, Xldb.Proto.Pip parentPip)
        {
            var xldbIpcPip = new Xldb.Proto.IpcPip()
            {
                GraphInfo = parentPip,
                IpcInfo = new IpcClientInfo()
                {
                    IpcMonikerId = pip.IpcInfo.IpcMonikerId.ToString(pathTable),
                },
                MessageBody = pip.MessageBody.IsValid ? pip.MessageBody.ToString(pathTable) : "",
                IsServiceFinalization = pip.IsServiceFinalization,
                Provenance = pip.Provenance.ToPipProvenance(pathTable),
            };

            if (pip.Tags.IsValid)
            {
                xldbIpcPip.Tags.AddRange(pip.Tags.Select(key => key.ToString(pathTable)));
            }

            xldbIpcPip.ServicePipDependencies.AddRange(pip.ServicePipDependencies.Select(pipId => pipId.Value));
            xldbIpcPip.FileDependencies.AddRange(pip.FileDependencies.Select(file => file.ToFileArtifact(pathTable)));
            xldbIpcPip.DirectoryDependencies.AddRange(pip.DirectoryDependencies.Select(directory => directory.ToDirectoryArtifact(pathTable)));
            xldbIpcPip.LazilyMaterializedDependencies.AddRange(pip.LazilyMaterializedDependencies.Select(dep => dep.ToFileOrDirectoryArtifact(pathTable)));

            return xldbIpcPip;
        }

        /// <nodoc />
        public static Xldb.Proto.NodeId ToNodeId(this NodeId nodeId)
        {
            return !nodeId.IsValid ? null : new Xldb.Proto.NodeId()
            {
                Value = nodeId.Value
            };
        }

        /// <nodoc />
        public static Xldb.Proto.Edge ToEdge(this Edge edge)
        {
            return new Xldb.Proto.Edge()
            {
                OtherNode = edge.OtherNode.ToNodeId(),
                IsLight = edge.IsLight,
                Value = edge.Value
            };
        }

        /// <nodoc />
        public static Xldb.Proto.NodeRange ToNodeRange(this NodeRange nodeRange)
        {
            return new Xldb.Proto.NodeRange()
            {
                IsEmpty = nodeRange.IsEmpty,
                Size = nodeRange.Size,
                FromInclusive = nodeRange.FromInclusive.ToNodeId(),
                ToInclusive = nodeRange.ToInclusive.ToNodeId()
            };
        }

        /// <nodoc />
        public static Xldb.Proto.PipGraph ToPipGraph(this PipGraph pipGraph, PathTable pathTable, PipTable pipTable)
        {
            var xldbPipGraph = new Xldb.Proto.PipGraph()
            {
                GraphId = pipGraph.GraphId.ToString(),
                SemistableFingerprint = new ContentFingerprint() { Hash = pipGraph.SemistableFingerprint.Hash.ToFingerprint() },
                NodeRange = pipGraph.NodeRange.ToNodeRange(),
                MaxAbsolutePathIndex = pipGraph.MaxAbsolutePathIndex,
                FileCount = pipGraph.FileCount,
                ContentCount = pipGraph.ContentCount,
                ArtifactContentCount = pipGraph.ArtifactContentCount,
                ApiServerMoniker = pipGraph.ApiServerMoniker.ToString(pathTable)
            };

            xldbPipGraph.AllSealDirectoriesAndProducers.AddRange(pipGraph.AllSealDirectoriesAndProducers.Select(kvp => new DirectoryArtifactMap()
            {
                Artifact = kvp.Key.ToDirectoryArtifact(pathTable),
                PipId = kvp.Value.Value
            }));
            xldbPipGraph.StableKeys.AddRange(pipTable.StableKeys.Select(stableKey => stableKey.Value));

            foreach (var kvp in pipGraph.Modules)
            {
                xldbPipGraph.Modules.Add(kvp.Key.Value.ToString(pathTable), kvp.Value.ToNodeId());
            }

            return xldbPipGraph;
        }
    }
}
