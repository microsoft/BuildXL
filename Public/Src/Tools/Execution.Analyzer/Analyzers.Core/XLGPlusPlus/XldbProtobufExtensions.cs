// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using BuildXL.Execution.Analyzer.Xldb;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using AbsolutePath = BuildXL.Utilities.AbsolutePath;
using CopyFile = BuildXL.Pips.Operations.CopyFile;
using DirectoryArtifact = BuildXL.Utilities.DirectoryArtifact;
using FileArtifact = BuildXL.Utilities.FileArtifact;
using Fingerprint = BuildXL.Cache.MemoizationStore.Interfaces.Sessions.Fingerprint;
using ModulePip = BuildXL.Pips.Operations.ModulePip;
using ObservedPathEntry = BuildXL.Scheduler.Fingerprints.ObservedPathEntry;
using ObservedPathSet = BuildXL.Scheduler.Fingerprints.ObservedPathSet;
using Pip = BuildXL.Pips.Operations.Pip;
using PipData = BuildXL.Pips.Operations.PipData;
using PipProvenance = BuildXL.Pips.Operations.PipProvenance;
using PipTable = BuildXL.Pips.PipTable;
using ProcessPipExecutionPerformance = BuildXL.Pips.ProcessPipExecutionPerformance;
using ReportedFileAccess = BuildXL.Processes.ReportedFileAccess;
using ReportedProcess = BuildXL.Processes.ReportedProcess;
using SealDirectory = BuildXL.Pips.Operations.SealDirectory;
using UnsafeOptions = BuildXL.Scheduler.Fingerprints.UnsafeOptions;
using WriteFile = BuildXL.Pips.Operations.WriteFile;
using Process = BuildXL.Pips.Operations.Process;
using HashSourceFile = BuildXL.Pips.Operations.HashSourceFile;
using SpecFilePip = BuildXL.Pips.Operations.SpecFilePip;
using System.Threading.Tasks;
using IpcPip = BuildXL.Pips.Operations.IpcPip;
using FileOrDirectoryArtifact = BuildXL.Utilities.FileOrDirectoryArtifact;

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
            var Uuid = Guid.NewGuid().ToString();

            return new FileArtifactContentDecidedEvent()
            {
                UUID = Uuid,
                WorkerID = workerID,
                FileArtifact = data.FileArtifact.ToFileArtifact(pathTable),
                FileContentInfo = new FileContentInfo
                {
                    LengthAndExistence = data.FileContentInfo.SerializedLengthAndExistence,
                    Hash = new ContentHash() { Value = data.FileContentInfo.Hash.ToString() }
                },
                OutputOrigin = (PipOutputOrigin)data.OutputOrigin
            };
        }

        /// <nodoc />
        public static WorkerListEvent ToWorkerListEvent(this WorkerListEventData data, uint workerID)
        {
            var Uuid = Guid.NewGuid().ToString();

            var workerListEvent = new WorkerListEvent
            {
                UUID = Uuid,
                WorkerID = workerID
            };

            workerListEvent.Workers.AddRange(data.Workers.Select(worker => worker));
            return workerListEvent;
        }

        /// <nodoc />
        public static PipExecutionPerformanceEvent ToPipExecutionPerformanceEvent(this PipExecutionPerformanceEventData data)
        {
            var pipExecPerfEvent = new PipExecutionPerformanceEvent();
            var Uuid = Guid.NewGuid().ToString();

            var pipExecPerformance = new PipExecutionPerformance();
            pipExecPerformance.PipExecutionLevel = (int)data.ExecutionPerformance.ExecutionLevel;
            pipExecPerformance.ExecutionStart = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(data.ExecutionPerformance.ExecutionStart);
            pipExecPerformance.ExecutionStop = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(data.ExecutionPerformance.ExecutionStop);

            var processPipExecPerformance = new Xldb.ProcessPipExecutionPerformance();
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

            pipExecPerfEvent.UUID = Uuid;
            pipExecPerfEvent.WorkerID = data.ExecutionPerformance.WorkerId;
            pipExecPerfEvent.PipID = data.PipId.Value;
            pipExecPerfEvent.PipExecutionPerformance = pipExecPerformance;
            pipExecPerfEvent.ProcessPipExecutionPerformance = processPipExecPerformance;
            return pipExecPerfEvent;
        }

        /// <nodoc />
        public static DirectoryMembershipHashedEvent ToDirectoryMembershipHashedEvent(this DirectoryMembershipHashedEventData data, uint workerID, PathTable pathTable)
        {
            var Uuid = Guid.NewGuid().ToString();

            var directoryMembershipEvent = new DirectoryMembershipHashedEvent()
            {
                UUID = Uuid,
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
            var Uuid = Guid.NewGuid().ToString();

            var processExecutionMonitoringReportedEvent = new ProcessExecutionMonitoringReportedEvent
            {
                UUID = Uuid,
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
                processExecutionMonitoringReportedEvent.ProcessDetouringStatuses.Add(new ProcessDetouringStatusData()
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
            var Uuid = Guid.NewGuid().ToString();

            var processFingerprintComputationEvent = new ProcessFingerprintComputationEvent
            {
                UUID = Uuid,
                WorkerID = workerID,
                Kind = (Xldb.FingerprintComputationKind)data.Kind,
                PipID = data.PipId.Value,
                WeakFingerprint = new WeakContentFingerPrint()
                {
                    Hash = data.WeakFingerprint.Hash.ToFingerprint()
                },
            };

            foreach (var strongFingerprintComputation in data.StrongFingerprintComputations)
            {
                var processStrongFingerprintComputationData = new Xldb.ProcessStrongFingerprintComputationData()
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
                        observedAccessedFileName => new Xldb.StringId() { Value = observedAccessedFileName.Value }));
                processStrongFingerprintComputationData.PriorStrongFingerprints.AddRange(
                    strongFingerprintComputation.PriorStrongFingerprints.Select(
                        priorStrongFingerprint => new StrongContentFingerPrint() { Hash = priorStrongFingerprint.Hash.ToFingerprint() }));

                foreach (var observedInput in strongFingerprintComputation.ObservedInputs)
                {
                    processStrongFingerprintComputationData.ObservedInputs.Add(new ObservedInput()
                    {
                        Type = (ObservedInputType)observedInput.Type,
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
            var Uuid = Guid.NewGuid().ToString();

            return new ExtraEventDataReported
            {
                UUID = Uuid,
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
            var Uuid = Guid.NewGuid().ToString();

            return new DependencyViolationReportedEvent()
            {
                UUID = Uuid,
                WorkerID = workerID,
                ViolatorPipID = data.ViolatorPipId.Value,
                RelatedPipID = data.RelatedPipId.Value,
                ViolationType = (FileMonitoringViolationAnalyzer_DependencyViolationType)data.ViolationType,
                AccessLevel = (FileMonitoringViolationAnalyzer_AccessLevel)data.AccessLevel,
                Path = data.Path.ToAbsolutePath(pathTable)
            };
        }

        /// <nodoc />
        public static PipExecutionStepPerformanceReportedEvent ToPipExecutionStepPerformanceReportedEvent(this PipExecutionStepPerformanceEventData data, uint workerID)
        {
            var Uuid = Guid.NewGuid().ToString();

            var pipExecStepPerformanceEvent = new PipExecutionStepPerformanceReportedEvent
            {
                UUID = Uuid,
                WorkerID = workerID,
                PipID = data.PipId.Value,
                StartTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(data.StartTime),
                Duration = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(data.Duration),
                Step = (PipExecutionStep)data.Step,
                Dispatcher = (WorkDispatcher_DispatcherKind)data.Dispatcher
            };

            return pipExecStepPerformanceEvent;
        }

        /// <nodoc />
        public static PipCacheMissEvent ToPipCacheMissEvent(this PipCacheMissEventData data, uint workerID)
        {
            var Uuid = Guid.NewGuid().ToString();
            return new PipCacheMissEvent()
            {
                UUID = Uuid,
                WorkerID = workerID,
                PipID = data.PipId.Value,
                CacheMissType = (PipCacheMissType)data.CacheMissType
            };
        }

        /// <nodoc />
        public static StatusReportedEvent ToResourceUsageReportedEvent(this StatusEventData data, uint workerID)
        {
            var Uuid = Guid.NewGuid().ToString();

            var statusReportedEvent = new StatusReportedEvent()
            {
                UUID = Uuid,
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
                LimitingResource = (ExecutionSampler_LimitingResource)data.LimitingResource,
                UnresponsivenessFactor = data.UnresponsivenessFactor,
                ProcessPipsPending = data.ProcessPipsPending,
                ProcessPipsAllocatedSlots = data.ProcessPipsAllocatedSlots
            };

            statusReportedEvent.DiskPercents.AddRange(data.DiskPercents.Select(percent => percent));
            statusReportedEvent.DiskQueueDepths.AddRange(data.DiskQueueDepths.Select(depth => depth));
            statusReportedEvent.PipsSucceededAllTypes.AddRange(data.PipsSucceededAllTypes.Select(type => type));

            return statusReportedEvent;
        }

        /// <nodoc />
        public static BXLInvocationEvent ToBXLInvocationEvent(this DominoInvocationEventData data, uint workerID, PathTable pathTable)
        {
            var loggingConfig = data.Configuration.Logging;
            var Uuid = Guid.NewGuid().ToString();

            var bxlInvEvent = new BXLInvocationEvent
            {
                UUID = Uuid,
                WorkerID = workerID,
                SubstSource = loggingConfig.SubstSource.ToAbsolutePath(pathTable),
                SubstTarget = loggingConfig.SubstTarget.ToAbsolutePath(pathTable),
                IsSubstSourceValid = loggingConfig.SubstSource.IsValid,
                IsSubstTargetValid = loggingConfig.SubstTarget.IsValid
            };

            return bxlInvEvent;
        }

        /// <nodoc />
        public static PipExecutionDirectoryOutputsEvent ToPipExecutionDirectoryOutputsEvent(this PipExecutionDirectoryOutputs data, uint workerID, PathTable pathTable)
        {
            var Uuid = Guid.NewGuid().ToString();
            var pipExecDirectoryOutputEvent = new PipExecutionDirectoryOutputsEvent();
            pipExecDirectoryOutputEvent.UUID = Uuid;
            pipExecDirectoryOutputEvent.WorkerID = workerID;

            foreach (var (directoryArtifact, fileArtifactArray) in data.DirectoryOutputs)
            {
                var directoryOutput = new DirectoryOutput()
                {
                    DirectoryArtifact = directoryArtifact.ToDirectoryArtifact(pathTable)
                };

                directoryOutput.FileArtifactArray.AddRange(
                    fileArtifactArray.Select(
                        file => file.ToFileArtifact(pathTable)));
                pipExecDirectoryOutputEvent.DirectoryOutput.Add(directoryOutput);
            }

            return pipExecDirectoryOutputEvent;
        }

        /// <nodoc />
        public static Xldb.ReportedFileAccess ToReportedFileAccess(this ReportedFileAccess reportedFileAccess, PathTable pathTable)
        {
            return new Xldb.ReportedFileAccess()
            {
                CreationDisposition = (CreationDisposition)reportedFileAccess.CreationDisposition,
                DesiredAccess = (DesiredAccess)reportedFileAccess.DesiredAccess,
                Error = reportedFileAccess.Error,
                Usn = reportedFileAccess.Usn.Value,
                FlagsAndAttributes = (FlagsAndAttributes)reportedFileAccess.FlagsAndAttributes,
                Path = reportedFileAccess.Path,
                ManifestPath = reportedFileAccess.ManifestPath.ToString(pathTable, PathFormat.HostOs),
                Process = reportedFileAccess.Process.ToReportedProcess(),
                ShareMode = (ShareMode)reportedFileAccess.ShareMode,
                Status = (FileAccessStatus)reportedFileAccess.Status,
                Method = (FileAccessStatusMethod)reportedFileAccess.Method,
                RequestedAccess = (RequestedAccess)reportedFileAccess.RequestedAccess,
                Operation = (ReportedFileOperation)reportedFileAccess.Operation,
                ExplicitlyReported = reportedFileAccess.ExplicitlyReported,
                EnumeratePattern = reportedFileAccess.EnumeratePattern
            };
        }

        /// <nodoc />
        public static Xldb.ObservedPathSet ToObservedPathSet(this ObservedPathSet pathSet, PathTable pathTable)
        {
            var observedPathSet = new Xldb.ObservedPathSet();
            observedPathSet.Paths.AddRange(pathSet.Paths.Select(pathEntry => pathEntry.ToObservedPathEntry(pathTable)));
            observedPathSet.ObservedAccessedFileNames.AddRange(
                pathSet.ObservedAccessedFileNames.Select(
                    observedAccessedFileName => new Xldb.StringId() { Value = observedAccessedFileName.Value }));
            observedPathSet.UnsafeOptions = pathSet.UnsafeOptions.ToUnsafeOptions();

            return observedPathSet;
        }

        /// <nodoc />
        public static Xldb.UnsafeOptions ToUnsafeOptions(this UnsafeOptions unsafeOption)
        {
            var unsafeOpt = new Xldb.UnsafeOptions()
            {
                PreserveOutputsSalt = new ContentHash()
                {
                    Value = unsafeOption.PreserveOutputsSalt.ToString()
                },
                UnsafeConfiguration = new UnsafeSandboxConfiguration()
                {
                    PreserveOutputs = (PreserveOutputsMode)unsafeOption.UnsafeConfiguration.PreserveOutputs,
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
                    SandboxKind = (SandboxKind)unsafeOption.UnsafeConfiguration.SandboxKind,
                    UnexpectedFileAccessesAreErrors = unsafeOption.UnsafeConfiguration.UnexpectedFileAccessesAreErrors,
                    IgnoreGetFinalPathNameByHandle = unsafeOption.UnsafeConfiguration.IgnoreGetFinalPathNameByHandle,
                    IgnoreDynamicWritesOnAbsentProbes = unsafeOption.UnsafeConfiguration.IgnoreDynamicWritesOnAbsentProbes,
                    IgnoreUndeclaredAccessesUnderSharedOpaques = unsafeOption.UnsafeConfiguration.IgnoreUndeclaredAccessesUnderSharedOpaques,
                }
            };

            if (unsafeOption.UnsafeConfiguration.DoubleWritePolicy != null)
            {
                unsafeOpt.UnsafeConfiguration.DoubleWritePolicy = (DoubleWritePolicy)unsafeOption.UnsafeConfiguration.DoubleWritePolicy;
            }

            return unsafeOpt;
        }

        /// <nodoc />
        public static Xldb.AbsolutePath ToAbsolutePath(this AbsolutePath path, PathTable pathTable)
        {
            return new Xldb.AbsolutePath()
            {
                Value = path.ToString(pathTable, PathFormat.HostOs)
            };
        }

        /// <nodoc />
        public static Xldb.FileArtifact ToFileArtifact(this FileArtifact fileArtifact, PathTable pathTable)
        {
            return new Xldb.FileArtifact
            {
                Path = fileArtifact.Path.ToAbsolutePath(pathTable),
                RewriteCount = fileArtifact.RewriteCount,
                IsValid = fileArtifact.IsValid
            };
        }

        /// <nodoc />
        public static Xldb.ReportedProcess ToReportedProcess(this ReportedProcess reportedProcess)
        {
            return new Xldb.ReportedProcess()
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
        public static Xldb.ObservedPathEntry ToObservedPathEntry(this ObservedPathEntry pathEntry, PathTable pathTable)
        {
            return new Xldb.ObservedPathEntry()
            {
                Path = pathEntry.Path.ToAbsolutePath(pathTable),
                EnumeratePatternRegex = pathEntry.EnumeratePatternRegex ?? ""
            };
        }

        /// <nodoc />
        public static Xldb.Fingerprint ToFingerprint(this Fingerprint fingerprint)
        {
            return new Xldb.Fingerprint()
            {
                Length = fingerprint.Length,
                Bytes = Google.Protobuf.ByteString.CopyFrom(fingerprint.ToByteArray())
            };
        }

        /// <nodoc />
        public static Xldb.DirectoryArtifact ToDirectoryArtifact(this DirectoryArtifact artifact, PathTable pathTable)
        {
            return new Xldb.DirectoryArtifact()
            {
                IsValid = artifact.IsValid,
                Path = artifact.Path.ToAbsolutePath(pathTable),
                PartialSealID = artifact.PartialSealId,
                IsSharedOpaque = artifact.IsSharedOpaque
            };
        }

        /// <nodoc />
        public static Xldb.PipTable ToPipTable(this PipTable pipTable)
        {
            var xldbPipTable = new Xldb.PipTable
            {
                IsDisposed = pipTable.IsDisposed,
                Reads = pipTable.Reads,
                Writes = pipTable.Writes,
                Count = pipTable.Count,
                PageStreamsCount = pipTable.PageStreamsCount,
                Size = pipTable.Size,
                WritesMilliseconds = pipTable.WritesMilliseconds,
                ReadsMilliseconds = pipTable.ReadsMilliseconds,
                Used = pipTable.Used,
                Alive = pipTable.Alive
            };

            xldbPipTable.StableKeys.AddRange(pipTable.StableKeys.Select(stableKey => stableKey.Value));
            xldbPipTable.Keys.AddRange(pipTable.Keys.Select(key => key.Value));
            xldbPipTable.DeserializationContexts.AddRange(pipTable.DeserializationContexts.Select(
                context => new PipDeserializationContext() { Key = (PipQueryContext)context.Key, Value = context.Value }));
            return xldbPipTable;
        }

        /// <nodoc />
        public static Xldb.PipData ToPipData(this PipData pipData)
        {
            return !pipData.IsValid ? null : new Xldb.PipData
            {
                IsValid = pipData.IsValid,
                FragmentSeparator = pipData.FragmentSeparator.ToString(),
                FragmentCount = pipData.FragmentCount,
                FragmentEscaping = (Xldb.PipDataFragmentEscaping)pipData.FragmentEscaping
            };

        }

        /// <nodoc />
        public static Xldb.PipProvenance ToPipProvenance(this PipProvenance provenance)
        {
            return provenance == null ? null : new Xldb.PipProvenance()
            {
                Usage = provenance.Usage.ToPipData(),
                ModuleId = provenance.ModuleId.Value,
                ModuleName = provenance.ModuleName.ToString(),
                SemiStableHash = provenance.SemiStableHash
            };
        }

        public static Xldb.FileOrDirectoryArtifact ToFileOrDirectoryArtifact(this FileOrDirectoryArtifact artifact, PathTable pathTable)
        {
            var xldbFileOrDirectoryArtifact = new Xldb.FileOrDirectoryArtifact()
            {
                IsValid = artifact.IsValid
            };

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
        public static Xldb.Pip ToPip(this Pip pip)
        {
            var xldbPip = new Xldb.Pip()
            {
                SemiStableHash = pip.SemiStableHash,
                FormattedSemiStableHash = pip.FormattedSemiStableHash,
                ProcessAllowsUndeclaredSourceReads = pip.ProcessAllowsUndeclaredSourceReads,
                PipId = pip.PipId.Value,
                PipType = (Xldb.PipType)pip.PipType,
                Provenance = pip.Provenance.ToPipProvenance(),
            };

            if (pip.Tags.IsValid)
            {
                xldbPip.Tags.AddRange(pip.Tags.Select(key => key.ToString()));
            }

            return xldbPip;
        }

        /// <nodoc />
        public static Xldb.ModulePip ToModulePip(this ModulePip pip, PathTable pathTable, Xldb.Pip parentPip)
        {
            var xldbModulePip = new Xldb.ModulePip
            {
                ParentPipInfo = parentPip,
                Module = pip.Module.Value,
                Identity = pip.Identity.ToString(),
                ResolverKind = pip.ResolverKind.ToString(),
                ResolverName = pip.ResolverName.ToString(),
                Version = pip.Version.ToString(),
                Location = new Xldb.LocationData()
                {
                    IsValid = pip.Location.IsValid,
                    Line = pip.Location.Line,
                    Path = pip.Location.Path.ToAbsolutePath(pathTable),
                    Position = pip.Location.Position
                },
                Provenance = pip.Provenance.ToPipProvenance(),
                PipType = (Xldb.PipType)pip.PipType
            };

            if (pip.Tags.IsValid)
            {
                xldbModulePip.Tags.AddRange(pip.Tags.Select(key => key.ToString()));
            }

            return xldbModulePip;
        }

        /// <nodoc />
        public static Xldb.SealDirectory ToSealDirectory(this SealDirectory pip, PathTable pathTable, Xldb.Pip parentPip)
        {
            var xldbSealDirectory = new Xldb.SealDirectory
            {
                ParentPipInfo = parentPip,
                Kind = (Xldb.SealDirectoryKind)pip.Kind,
                DirectoryRoot = pip.DirectoryRoot.ToAbsolutePath(pathTable),
                IsComposite = pip.IsComposite,
                Scrub = pip.Scrub,
                IsInitialzed = pip.IsInitialized,
                Directory = pip.Directory.ToDirectoryArtifact(pathTable),
                IsSealSourceDirectory = pip.IsSealSourceDirectory,
                Provenance = pip.Provenance.ToPipProvenance(),
                PipType = (Xldb.PipType)pip.PipType

            };

            xldbSealDirectory.Patterns.AddRange(pip.Patterns.Select(key => key.ToString()));
            xldbSealDirectory.Contents.AddRange(pip.Contents.Select(file => file.ToFileArtifact(pathTable)));
            xldbSealDirectory.ComposedDirectories.AddRange(pip.ComposedDirectories.Select(dir => dir.ToDirectoryArtifact(pathTable)));

            if (pip.Tags.IsValid)
            {
                xldbSealDirectory.Tags.AddRange(pip.Tags.Select(key => key.ToString()));
            }

            return xldbSealDirectory;
        }

        /// <nodoc />
        public static Xldb.CopyFile ToCopyFile(this CopyFile pip, PathTable pathTable, Xldb.Pip parentPip)
        {
            var xldbCopyFile = new Xldb.CopyFile
            {
                ParentPipInfo = parentPip,
                Source = pip.Source.ToFileArtifact(pathTable),
                Destination = pip.Destination.ToFileArtifact(pathTable),
                OutputsMustRemainWritable = pip.OutputsMustRemainWritable,
                Provenance = pip.Provenance.ToPipProvenance(),
                PipType = (Xldb.PipType)pip.PipType
            };

            if (pip.Tags.IsValid)
            {
                xldbCopyFile.Tags.AddRange(pip.Tags.Select(key => key.ToString()));
            }

            return xldbCopyFile;
        }

        /// <nodoc />
        public static Xldb.WriteFile ToWriteFile(this WriteFile pip, PathTable pathTable, Xldb.Pip parentPip)
        {
            var xldbWriteFile = new Xldb.WriteFile
            {
                ParentPipInfo = parentPip,
                Destination = pip.Destination.ToFileArtifact(pathTable),
                Contents = pip.Contents.ToPipData(),
                Encoding = (Xldb.WriteFileEncoding)pip.Encoding,
                Provenance = pip.Provenance.ToPipProvenance(),
                PipType = (Xldb.PipType)pip.PipType
            };

            if (pip.Tags.IsValid)
            {
                xldbWriteFile.Tags.AddRange(pip.Tags.Select(key => key.ToString()));
            }

            return xldbWriteFile;
        }

        /// <nodoc />
        public static ProcessPip ToProcessPip(this Process pip, PathTable pathTable, Xldb.Pip parentPip)
        {
            var xldbProcessPip = new ProcessPip
            {
                ParentPipInfo = parentPip,
                ProcessOptions = (Options)pip.ProcessOptions,
                ProcessAbsentPathProbeInUndeclaredOpaquesMode = (AbsentPathProbeInUndeclaredOpaquesMode)pip.ProcessAbsentPathProbeInUndeclaredOpaquesMode,
                StandardInputFile = pip.StandardInputFile.ToFileArtifact(pathTable),
                StandardInputData = pip.StandardInputData.ToPipData(),
                StandardInput = new StandardInput()
                {
                    File = pip.StandardInput.File.ToFileArtifact(pathTable),
                    Data = pip.StandardInput.Data.ToPipData(),
                    IsValid = pip.StandardInput.IsValid
                },
                StandardOutput = pip.StandardOutput.ToFileArtifact(pathTable),
                StandardError = pip.StandardError.ToFileArtifact(pathTable),
                StandardDirectory = pip.StandardDirectory.ToAbsolutePath(pathTable),
                UniqueOutputDirectory = pip.UniqueOutputDirectory.ToAbsolutePath(pathTable),
                UniqueRedirectedDirectoryRoot = pip.UniqueRedirectedDirectoryRoot.ToAbsolutePath(pathTable),
                ResponseFile = pip.ResponseFile.ToFileArtifact(pathTable),
                ResponseFileData = pip.ResponseFileData.ToPipData(),
                Executable = pip.Executable.ToFileArtifact(pathTable),
                ToolDescription = pip.ToolDescription.ToString(),
                WorkingDirectory = pip.WorkingDirectory.ToAbsolutePath(pathTable),
                Arguments = pip.Arguments.ToPipData(),
                WarningRegex = new Xldb.RegexDescriptor()
                {
                    Pattern = pip.WarningRegex.Pattern.ToString(),
                    Options = (RegexOptions)pip.WarningRegex.Options,
                    IsValid = pip.WarningRegex.IsValid
                },
                ErrorRegex = new Xldb.RegexDescriptor()
                {
                    Pattern = pip.ErrorRegex.Pattern.ToString(),
                    Options = (RegexOptions)pip.ErrorRegex.Options,
                    IsValid = pip.ErrorRegex.IsValid
                },
                TempDirectory = pip.TempDirectory.ToAbsolutePath(pathTable),
                Weight = pip.Weight,
                Priority = pip.Priority,
                TestRetries = pip.TestRetries,
                IsStartOrShutdownKind = pip.IsStartOrShutdownKind,
                Provenance = pip.Provenance.ToPipProvenance(),
                PipType = (Xldb.PipType)pip.PipType,
                HasUntrackedChildProcesses = pip.HasUntrackedChildProcesses,
                ProducesPathIndependentOutputs = pip.ProducesPathIndependentOutputs,
                OutputsMustRemainWritable = pip.OutputsMustRemainWritable,
                RequiresAdmin = pip.RequiresAdmin,
                AllowPreserveOutputs = pip.AllowPreserveOutputs,
                IsLight = pip.IsLight,
                IsService = pip.IsService,
                AllowUndeclaredSourceReads = pip.AllowUndeclaredSourceReads,
                NeedsToRunInContainer = pip.NeedsToRunInContainer,
                ShutdownProcessPipId = pip.ShutdownProcessPipId.Value,
                DisableCacheLookup = pip.DisableCacheLookup,
                DoubleWritePolicy = (DoubleWritePolicy)pip.DoubleWritePolicy,
                ContainerIsolationLevel = (ContainerIsolationLevel)pip.ContainerIsolationLevel
            };

            if (pip.WarningTimeout != null)
            {
                xldbProcessPip.WarningTimeout = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan((TimeSpan)pip.WarningTimeout);
            }

            if (pip.Timeout != null)
            {
                xldbProcessPip.Timeout = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan((TimeSpan)pip.Timeout);
            }

            if (pip.NestedProcessTerminationTimeout != null)
            {
                xldbProcessPip.NestedProcessTerminationTimeout = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan((TimeSpan)pip.NestedProcessTerminationTimeout);
            }

            var serviceInfo = new Xldb.ServiceInfo
            {
                Kind = (Xldb.ServicePipKind)pip.ServiceInfo.Kind,
                ShutdownPipId = pip.ServiceInfo.ShutdownPipId.Value,
                IsValid = pip.ServiceInfo.IsValid,
                IsStartOrShutdownKind = pip.ServiceInfo.IsStartOrShutdownKind
            };

            serviceInfo.ServicePipDependencies.AddRange(pip.ServiceInfo.ServicePipDependencies.Select(key => key.Value));
            serviceInfo.FinalizationPipIds.AddRange(pip.ServiceInfo.FinalizationPipIds.Select(key => key.Value));

            xldbProcessPip.ServiceInfo = serviceInfo;
            xldbProcessPip.EnvironmentVariable.AddRange(pip.EnvironmentVariables.Select(
                envVar => new Xldb.EnvironmentVariable() { Name = envVar.Name.ToString(), Value = envVar.Value.ToPipData(), IsPassThrough = envVar.IsPassThrough }));
            xldbProcessPip.Dependencies.AddRange(pip.Dependencies.Select(file => file.ToFileArtifact(pathTable)));
            xldbProcessPip.DirectoryDependencies.AddRange(pip.DirectoryDependencies.Select(dir => dir.ToDirectoryArtifact(pathTable)));
            xldbProcessPip.OrderDependencies.AddRange(pip.OrderDependencies.Select(order => order.Value));
            xldbProcessPip.UntrackedPaths.AddRange(pip.UntrackedPaths.Select(path => path.ToAbsolutePath(pathTable)));
            xldbProcessPip.UntrackedScopes.AddRange(pip.UntrackedScopes.Select(path => path.ToAbsolutePath(pathTable)));
            xldbProcessPip.SuccessExitCodes.AddRange(pip.SuccessExitCodes.Select(code => code));
            xldbProcessPip.RetryExitCodes.AddRange(pip.RetryExitCodes.Select(code => code));
            xldbProcessPip.FileOutputs.AddRange(pip.FileOutputs.Select(
                output => new Xldb.FileArtifactWithAttributes()
                { IsValid = output.IsValid, Path = output.Path.ToAbsolutePath(pathTable), RewriteCount = output.RewriteCount, FileExistence = (Xldb.FileExistence)output.FileExistence }));
            xldbProcessPip.DirectoryOutputs.AddRange(pip.DirectoryOutputs.Select(dir => dir.ToDirectoryArtifact(pathTable)));
            xldbProcessPip.Semaphores.AddRange(pip.Semaphores.Select(
                semaphore => new Xldb.ProcessSemaphoreInfo() { Name = semaphore.Name.ToString(), Value = semaphore.Value, Limit = semaphore.Limit, IsValid = semaphore.IsValid }));
            xldbProcessPip.AdditionalTempDirectories.AddRange(pip.AdditionalTempDirectories.Select(dir => dir.ToAbsolutePath(pathTable)));
            xldbProcessPip.PreserveOutputWhitelist.AddRange(pip.PreserveOutputWhitelist.Select(path => path.ToAbsolutePath(pathTable)));
            xldbProcessPip.ServicePipDependencies.AddRange(pip.ServicePipDependencies.Select(dep => dep.Value));

            if (pip.Tags.IsValid)
            {
                xldbProcessPip.Tags.AddRange(pip.Tags.Select(key => key.ToString()));
            }

            return xldbProcessPip;
        }

        /// <nodoc />
        public static Xldb.HashSourceFile ToHashSourceFile(this HashSourceFile pip, PathTable pathTable, Xldb.Pip parentPip)
        {
            var xldbHashSourceFile = new Xldb.HashSourceFile()
            {
                ParentPipInfo = parentPip,
                Artifact = pip.Artifact.ToFileArtifact(pathTable),
                Provenance = pip.Provenance.ToPipProvenance(),
                PipType = (Xldb.PipType)pip.PipType
            };

            if (pip.Tags.IsValid)
            {
                xldbHashSourceFile.Tags.AddRange(pip.Tags.Select(key => key.ToString()));
            }

            return xldbHashSourceFile;
        }

        /// <nodoc />
        public static Xldb.SpecFilePip ToSpecFilePip(this SpecFilePip pip, PathTable pathTable, Xldb.Pip parentPip)
        {
            var xldbSpecFilePip = new Xldb.SpecFilePip()
            {
                ParentPipInfo = parentPip,
                SpecFile = pip.SpecFile.ToFileArtifact(pathTable),
                DefinitionLocation = new Xldb.LocationData()
                {
                    IsValid = pip.DefinitionLocation.IsValid,
                    Line = pip.DefinitionLocation.Line,
                    Path = pip.DefinitionLocation.Path.ToAbsolutePath(pathTable),
                    Position = pip.DefinitionLocation.Position
                },
                OwningModule = pip.OwningModule.Value,
                Provenance = pip.Provenance.ToPipProvenance(),
                PipType = (Xldb.PipType)pip.PipType
            };

            if (pip.Tags.IsValid)
            {
                xldbSpecFilePip.Tags.AddRange(pip.Tags.Select(key => key.ToString()));
            }

            return xldbSpecFilePip;
        }

        /// <nodoc />
        public static Xldb.IpcPip ToIpcPip(this IpcPip pip, PathTable pathTable, Xldb.Pip parentPip)
        {
            var xldbIpcPip = new Xldb.IpcPip()
            {
                ParentPipInfo = parentPip,
                IpcInfo = new Xldb.IpcClientInfo()
                {
                    IpcMonikerId = pip.IpcInfo.IpcMonikerId.ToString(),
                    IpcClientConfig = new ClientConfig()
                    {
                        MaxConnectRetries = pip.IpcInfo.IpcClientConfig.MaxConnectRetries,
                        ConnectRetryDelay = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(pip.IpcInfo.IpcClientConfig.ConnectRetryDelay)
                    }
                },
                MessageBody = pip.MessageBody.ToPipData(),
                OutputFile = pip.OutputFile.ToFileArtifact(pathTable),
                IsServiceFinalization = pip.IsServiceFinalization,
                MustRunOnMaster = pip.MustRunOnMaster,
                Provenance = pip.Provenance.ToPipProvenance(),
                PipType = (Xldb.PipType)pip.PipType
            };

            if (pip.Tags.IsValid)
            {
                xldbIpcPip.Tags.AddRange(pip.Tags.Select(key => key.ToString()));
            }

            xldbIpcPip.ServicePipDependencies.AddRange(pip.ServicePipDependencies.Select(pipId => pipId.Value));
            xldbIpcPip.FileDependencies.AddRange(pip.FileDependencies.Select(file => file.ToFileArtifact(pathTable)));
            xldbIpcPip.DirectoryDependencies.AddRange(pip.DirectoryDependencies.Select(directory => directory.ToDirectoryArtifact(pathTable)));
            xldbIpcPip.LazilyMaterializedDependencies.AddRange(pip.LazilyMaterializedDependencies.Select(dep => dep.ToFileOrDirectoryArtifact(pathTable)));

            return xldbIpcPip;
        }
    }
}
